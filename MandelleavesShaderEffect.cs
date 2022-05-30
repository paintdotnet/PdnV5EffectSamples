// This work is licensed under a Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported License.
// Ported from: https://www.shadertoy.com/view/MttBz8

using ComputeSharp;
using ComputeSharp.D2D1;
using ComputeSharp.D2D1.Interop;
using PaintDotNet.Direct2D1;
using PaintDotNet.Direct2D1.Effects;
using PaintDotNet.PropertySystem;
using PaintDotNet.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PaintDotNet.Effects.Gpu.Samples;

// This is a port of a colorful fractal shader from ShaderToy
// It also shows how to implement multisample antialiasing (MSAA)

internal sealed partial class MandelleavesShaderEffect
    : PropertyBasedGpuImageEffect
{
    public MandelleavesShaderEffect()
        : base(
            "Mandelleaves (GPU Samples)",
            null, // no image
            "GPU Samples",
            new GpuImageEffectOptions()
            {
                Flags = EffectFlags.Configurable
            })
    {
    }

    private enum PropertyNames
    {
        Zoom,
        Quality,
        Link
    }

    protected override PropertyCollection OnCreatePropertyCollection()
    {
        List<Property> properties = new List<Property>();

        properties.Add(new DoubleProperty(PropertyNames.Zoom, 1.0, 0.1, 100.0));
        properties.Add(new Int32Property(PropertyNames.Quality, 2, 1, 8));
        properties.Add(new UriProperty(PropertyNames.Link, new Uri("https://www.shadertoy.com/view/MttBz8")));

        return new PropertyCollection(properties);
    }

    private Guid mandelleavesShaderEffectID;
    private Vector2Float[]? subPixelOffsets;
    private AddConstEffect[]? subPxScenePosEffects;
    private IDeviceEffect[]? mandelleavesShaderEffects;

    protected override void OnSetDeviceContext(IDeviceContext deviceContext)
    {
        deviceContext.Factory.RegisterEffectFromBlob(
            D2D1PixelShaderEffect.GetRegistrationBlob<MandelleavesShader>(out this.mandelleavesShaderEffectID));

        base.OnSetDeviceContext(deviceContext);
    }

    protected override void OnInvalidateDeviceResources()
    {
        if (this.mandelleavesShaderEffects != null)
        {
            foreach (IDeviceEffect mandelleavesShaderEffect in this.mandelleavesShaderEffects)
            {
                mandelleavesShaderEffect.Dispose();
            }

            this.mandelleavesShaderEffects = null;
        }

        if (this.subPxScenePosEffects != null)
        {
            foreach (IDeviceEffect subPxScenePosEffect in this.subPxScenePosEffects)
            {
                subPxScenePosEffect.Dispose();
            }

            this.subPxScenePosEffects = null;
        }

        base.OnInvalidateDeviceResources();
    }

    protected override IDeviceImage OnCreateOutput(IDeviceContext deviceContext)
    {
        int quality = this.Token.GetProperty<Int32Property>(PropertyNames.Quality).Value;
        this.subPixelOffsets = EffectHelpers.GetRgssOffsets(quality);

        using CompositeEffect compositeEffect = new CompositeEffect(deviceContext);
        compositeEffect.InputCount = this.subPixelOffsets.Length;
        compositeEffect.Properties.Mode.SetValue(CompositeMode.Plus);

        this.subPxScenePosEffects = new AddConstEffect[this.subPixelOffsets!.Length];
        this.mandelleavesShaderEffects = new IDeviceEffect[this.subPixelOffsets.Length];
        for (int i = 0; i < this.mandelleavesShaderEffects.Length; ++i)
        {
            ScenePositionEffect scenePosEffect = new ScenePositionEffect(deviceContext);

            this.subPxScenePosEffects[i] = new AddConstEffect(deviceContext);
            this.subPxScenePosEffects[i].Properties.Input.Set(scenePosEffect);

            this.mandelleavesShaderEffects[i] = deviceContext.CreateEffect(this.mandelleavesShaderEffectID);
            this.mandelleavesShaderEffects[i].SetInput(0, this.subPxScenePosEffects[i]);

            compositeEffect.SetInput(i, this.mandelleavesShaderEffects[i]);
        }

        MultiplyConstEffect output = new MultiplyConstEffect(deviceContext);
        output.Properties.Input.Set(compositeEffect);
        output.Properties.Value.SetValue((float)(1.0 / this.subPixelOffsets.Length));

        return output;
    }

    protected override InspectTokenAction OnInspectTokenChanges(PropertyBasedEffectConfigToken oldToken, PropertyBasedEffectConfigToken newToken)
    {
        int oldQuality = oldToken.GetProperty<Int32Property>(PropertyNames.Quality).Value;
        int newQuality = newToken.GetProperty<Int32Property>(PropertyNames.Quality).Value;
        return (oldQuality == newQuality)
            ? InspectTokenAction.UpdateOutput
            : InspectTokenAction.RecreateOutput;
    }

    protected override void OnUpdateOutput(IDeviceContext deviceContext)
    {
        double zoom = this.Token.GetProperty<DoubleProperty>(PropertyNames.Zoom).Value;

        for (int i = 0; i < this.subPixelOffsets!.Length; ++i)
        {
            this.subPxScenePosEffects![i].Properties.Value.SetValue(new Vector4Float(this.subPixelOffsets![i], 0, 0));

            this.mandelleavesShaderEffects![i].SetValue(
                D2D1PixelShaderEffectProperty.ConstantBuffer,
                PropertyType.Blob,
                D2D1PixelShader.GetConstantBuffer(new MandelleavesShader(
                    new Float2(this.SourceSize.Width, this.SourceSize.Height),
                    0,
                    (float)(1.0 / zoom))));
        }

        base.OnUpdateOutput(deviceContext);
    }

    [D2DInputCount(1)]
    [D2DInputSimple(0)]
    [D2DInputDescription(0, D2D1Filter.MinMagMipPoint)]
    [D2DEmbeddedBytecode(D2D1ShaderProfile.PixelShader50)]
    [D2DRequiresScenePosition]
    [D2DCompileOptions(D2D1CompileOptions.Default | D2D1CompileOptions.EnableLinking | D2D1CompileOptions.IeeeStrictness)]
    [AutoConstructor]
    private readonly partial struct MandelleavesShader
        : ID2D1PixelShader
    {
        private readonly float2 iResolution;
        private readonly float iTime;
        private readonly float zoom;

        public float4 Execute()
        {
            float2 fragCoord = D2D.GetInput(0).XY;
            mainImage(out float4 fragColor, fragCoord);
            return fragColor;
        }

        float2 cmul(float2 a, float2 b) 
        { 
            return new float2(a.X * b.X - a.Y * b.Y, a.X * b.Y + a.Y * b.X); 
        }

        float3 hsv(float h, float s, float v)
        {
            float4 K = new float4(1.0f, 2.0f / 3.0f, 1.0f / 3.0f, 3.0f);
            float3 p = Hlsl.Abs(Hlsl.Frac(new float3(h, h, h) + K.XYZ) * 6.0f - K.WWW);
            return v * Hlsl.Lerp(K.XXX, Hlsl.Clamp(p - K.XXX, 0.0f, 1.0f), s);
        }

        void mainImage(out float4 fragColor, float2 fragCoord)
        {
            float2 surfacePosition = 0.5f * (2.0f * fragCoord - this.iResolution.XY) / Hlsl.Min(this.iResolution.X, this.iResolution.Y);

            //vec2 p = zoom * 2.0 * surfacePosition - vec2(0.7, 0.0);
            float2 p = this.zoom * 0.016f * surfacePosition - new float2(0.805f, -0.176f);
            //vec2 p = zoom * 0.001 * surfacePosition - vec2(1.924, 0.0);

            float2 z = p;
            float2 c = p;
            float2 dz = new float2(1.0f, 0.0f);
            float it = 0.0f;
            for (float i = 0.0f; i < 1024.0f; i += 1.0f)
            {
                dz = 2.0f * cmul(z, dz) + new float2(1.0f, 0.0f);
                z = cmul(z, z) + c;

                float a = Hlsl.Sin(this.iTime * 1.5f + i * 2.0f) * 0.3f + i * 1.3f;
                float2 t = Hlsl.Mul(new float2x2(Hlsl.Cos(a), Hlsl.Sin(a), -Hlsl.Sin(a), Hlsl.Cos(a)), z);
                if (Hlsl.Abs(t.X) > 2.0f && Hlsl.Abs(t.Y) > 2.0) 
                { 
                    it = i; 
                    break; 
                }
            }

            if (it == 0.0f)
            {
                fragColor = new float4(0, 0, 0, 1);
            }
            else
            {
                float z2 = z.X * z.X + z.Y * z.Y;
                float dist = Hlsl.Log(z2) * Hlsl.Sqrt(z2) / Hlsl.Length(dz);
                float r = Hlsl.Sqrt(z2);

                float pixelsize = Hlsl.Fwidth(p.X);
                float diagonal = Hlsl.Length(this.iResolution.XY);
                float glowsize = pixelsize * diagonal / 400.0f;
                float shadowsize = pixelsize * diagonal / 80.0f;

                float fadeout;
                float glow;
                if (dist < pixelsize)
                {
                    fadeout = dist / pixelsize;
                    glow = 1.0f;
                }
                else
                {
                    fadeout = Hlsl.Min(shadowsize / (dist + shadowsize - pixelsize) + 1.0f / (r + 1.0f), 1.0f);
                    glow = Hlsl.Min(glowsize / (dist + glowsize - pixelsize), 1.0f);
                }

                fragColor = new float4(hsv(
                    it / 32.0f + 0.4f,
                    1.0f - glow,
                    fadeout
                ), 1.0f);
            }
        }
    }
}

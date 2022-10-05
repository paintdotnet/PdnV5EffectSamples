// This work is licensed under a Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported License.
// Ported from: https://www.shadertoy.com/view/MttBz8

using ComputeSharp;
using ComputeSharp.D2D1;
using ComputeSharp.D2D1.Interop;
using PaintDotNet.Direct2D1;
using PaintDotNet.Direct2D1.Effects;
using PaintDotNet.Effects.Gpu;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;
using PaintDotNet.Rendering;
using System;
using System.Collections.Generic;

namespace PaintDotNet.Effects.Samples.Gpu;

// This is a port of a colorful fractal shader from ShaderToy, combined with a Polar Inversion sample map
// shader that transforms the scene positions before feeding them to the Mandelleaves shader. This results
// in higher quality rendering than if you were to render the fractal and then used the Polar Inversion
// effect that's built-in to Paint.NET.
// This effect also implements multisample antialiasing (MSAA).

internal sealed partial class PolarizedMandelleavesShaderEffect
    : PropertyBasedGpuImageEffect
{
    public PolarizedMandelleavesShaderEffect()
        : base(
            "Polarized Mandelleaves (GPU Samples)",
            "PDN v5 Samples (GPU)",
            new GpuImageEffectOptions()
            {
                IsConfigurable = true
            })
    {
    }

    private enum PropertyNames
    {
        FractalZoom,
        PolarInversionAmount,
        PolarInversionOffset,
        InfiniteCoordinateSpace,
        Quality,
        Link
    }

    protected override PropertyCollection OnCreatePropertyCollection()
    {
        List<Property> properties = new List<Property>();

        properties.Add(new DoubleProperty(PropertyNames.FractalZoom, 1.0, 0.01, 100.0));
        properties.Add(new DoubleProperty(PropertyNames.PolarInversionAmount, 0, -8, 8));
        properties.Add(new DoubleVectorProperty(PropertyNames.PolarInversionOffset, new Vector2Double(0, 0), new Vector2Double(-2, -2), new Vector2Double(2, 2)));
        properties.Add(new BooleanProperty(PropertyNames.InfiniteCoordinateSpace, false));
        properties.Add(new Int32Property(PropertyNames.Quality, 2, 1, 8));
        properties.Add(new UriProperty(PropertyNames.Link, new Uri("https://www.shadertoy.com/view/MttBz8")));

        return new PropertyCollection(properties);
    }

    protected override ControlInfo OnCreateConfigUI(PropertyCollection props)
    {
        ControlInfo configUI = CreateDefaultConfigUI(props);

        configUI.SetPropertyControlValue(PropertyNames.FractalZoom, ControlInfoPropertyNames.UseExponentialScale, true);

        return configUI;
    }

    private Guid polarInversionEffectID;
    private Guid sampleMapMirrorEffectID;
    private Guid mandelleavesEffectID;

    private Vector2Float[]? subPixelOffsets;
    private HlslBinaryOperatorEffect[]? subPxScenePosEffects;
    private IDeviceEffect[]? polarInversionEffects;
    private IDeviceEffect[]? sampleMapMirrorEffects;
    private IDeviceEffect[]? mandelleavesShaderEffects;

    protected override void OnSetDeviceContext(IDeviceContext deviceContext)
    {
        deviceContext.Factory.RegisterEffectFromBlob(
            D2D1PixelShaderEffect.GetRegistrationBlob<PolarInversionShader>(out this.polarInversionEffectID));

        deviceContext.Factory.RegisterEffectFromBlob(
            D2D1PixelShaderEffect.GetRegistrationBlob<SampleMapMirrorShader>(out this.sampleMapMirrorEffectID));

        deviceContext.Factory.RegisterEffectFromBlob(
            D2D1PixelShaderEffect.GetRegistrationBlob<MandelleavesShader>(out this.mandelleavesEffectID));

        base.OnSetDeviceContext(deviceContext);
    }

    protected override InspectTokenAction OnInspectTokenChanges(PropertyBasedEffectConfigToken oldToken, PropertyBasedEffectConfigToken newToken)
    {
        // If the Quality or InfiniteCoordinateSpace properties are changed, we need to rebuild the effect graph.

        int oldQuality = oldToken.GetProperty<Int32Property>(PropertyNames.Quality).Value;
        bool oldInfiniteCoordinateSpace = oldToken.GetProperty<BooleanProperty>(PropertyNames.InfiniteCoordinateSpace).Value;

        int newQuality = newToken.GetProperty<Int32Property>(PropertyNames.Quality).Value;
        bool newInfiniteCoordinateSpace = newToken.GetProperty<BooleanProperty>(PropertyNames.InfiniteCoordinateSpace).Value;

        return (oldQuality == newQuality && oldInfiniteCoordinateSpace == newInfiniteCoordinateSpace)
            ? InspectTokenAction.UpdateOutput
            : InspectTokenAction.RecreateOutput;
    }

    protected override IDeviceImage OnCreateOutput(IDeviceContext deviceContext)
    {
        int quality = this.Token.GetProperty<Int32Property>(PropertyNames.Quality).Value;
        this.subPixelOffsets = EffectHelpers.GetRgssOffsets(quality);

        bool infiniteCoordinateSpace = this.Token.GetProperty<BooleanProperty>(PropertyNames.InfiniteCoordinateSpace).Value;

        using CompositeEffect compositeEffect = new CompositeEffect(deviceContext);
        compositeEffect.InputCount = this.subPixelOffsets.Length;
        compositeEffect.Properties.Mode.SetValue(CompositeMode.Plus);

        this.subPxScenePosEffects = new HlslBinaryOperatorEffect[this.subPixelOffsets!.Length];
        this.polarInversionEffects = new IDeviceEffect[this.subPixelOffsets!.Length];
        this.sampleMapMirrorEffects = new IDeviceEffect[this.subPixelOffsets!.Length];
        this.mandelleavesShaderEffects = new IDeviceEffect[this.subPixelOffsets!.Length];
        for (int i = 0; i < this.mandelleavesShaderEffects.Length; ++i)
        {
            ScenePositionEffect scenePosEffect = new ScenePositionEffect(deviceContext);

            this.subPxScenePosEffects[i] = new HlslBinaryOperatorEffect(deviceContext);
            this.subPxScenePosEffects[i].Properties.Parameter1.SetValue(HlslEffectParameter.Input);
            this.subPxScenePosEffects[i].Properties.Input1.Set(scenePosEffect);
            this.subPxScenePosEffects[i].Properties.Operator.SetValue(HlslBinaryOperator.Add);
            this.subPxScenePosEffects[i].Properties.Parameter2.SetValue(HlslEffectParameter.Value);
            // The Value2 property will be set during OnUpdateOutput()

            this.polarInversionEffects[i] = deviceContext.CreateEffect(this.polarInversionEffectID);
            this.polarInversionEffects[i].SetInput(0, this.subPxScenePosEffects[i]);

            this.sampleMapMirrorEffects[i] = deviceContext.CreateEffect(this.sampleMapMirrorEffectID);
            this.sampleMapMirrorEffects[i].SetInput(0, this.polarInversionEffects[i]);

            this.mandelleavesShaderEffects[i] = deviceContext.CreateEffect(this.mandelleavesEffectID);
            this.mandelleavesShaderEffects[i].SetInput(0, infiniteCoordinateSpace
                ? this.polarInversionEffects[i]
                : this.sampleMapMirrorEffects[i]);

            compositeEffect.SetInput(i, this.mandelleavesShaderEffects[i]);
        }

        // After accumulating the samples, take the average by dividing by the number of samples
        // Use multiplication of (1/n) instead of division because division is relatively slow
        HlslBinaryOperatorEffect output = new HlslBinaryOperatorEffect(deviceContext);
        output.Properties.Parameter1.SetValue(HlslEffectParameter.Input);
        output.Properties.Input1.Set(compositeEffect);
        output.Properties.Operator.SetValue(HlslBinaryOperator.Multiply);
        output.Properties.Parameter2.SetValue(HlslEffectParameter.Value);
        output.Properties.Value2.SetValue(new Vector4Float((float)(1.0 / this.subPixelOffsets.Length)));

        return output;
    }

    protected override void OnUpdateOutput(IDeviceContext deviceContext)
    {
        double zoom = this.Token.GetProperty<DoubleProperty>(PropertyNames.FractalZoom).Value;

        double radius = Math.Min(this.Environment.CanvasSize.Width, this.Environment.CanvasSize.Height) * 0.5;
        double radius2 = radius * radius;
        double polarInversionAmount = this.Token.GetProperty<DoubleProperty>(PropertyNames.PolarInversionAmount).Value;
        Vector2Double polarInversionOffset = this.Token.GetProperty<DoubleVectorProperty>(PropertyNames.PolarInversionOffset).Value;
        Vector2Double centerOffset = new Vector2Double(
            this.Environment.CanvasSize.Width * (1.0 + polarInversionOffset.X) * 0.5,
            this.Environment.CanvasSize.Height * (1.0 + polarInversionOffset.Y) * 0.5);

        for (int i = 0; i < this.subPixelOffsets!.Length; ++i)
        {
            this.subPxScenePosEffects![i].Properties.Value2.SetValue(new Vector4Float(this.subPixelOffsets![i], 0, 0));

            this.polarInversionEffects![i].SetValue(
                D2D1PixelShaderEffectProperty.ConstantBuffer,
                PropertyType.Blob,
                D2D1PixelShader.GetConstantBuffer(new PolarInversionShader(
                    new float2((float)centerOffset.X, (float)centerOffset.Y),
                    (float)radius2,
                    (float)polarInversionAmount)));

            this.sampleMapMirrorEffects![i].SetValue(
                D2D1PixelShaderEffectProperty.ConstantBuffer,
                PropertyType.Blob,
                D2D1PixelShader.GetConstantBuffer(new SampleMapMirrorShader(
                    new float2(this.Environment.CanvasSize.Width, this.Environment.CanvasSize.Height))));

            this.mandelleavesShaderEffects![i].SetValue(
                D2D1PixelShaderEffectProperty.ConstantBuffer,
                PropertyType.Blob,
                D2D1PixelShader.GetConstantBuffer(new MandelleavesShader(
                    new float2(this.Environment.CanvasSize.Width, this.Environment.CanvasSize.Height),
                    0,
                    (float)(1.0 / zoom))));
        }

        base.OnUpdateOutput(deviceContext);
    }

    [D2DInputCount(1)]
    [D2DInputSimple(0)]
    [D2DInputDescription(0, D2D1Filter.MinMagMipPoint)]
    [D2DOutputBuffer(D2D1BufferPrecision.Float32, D2D1ChannelDepth.Four)]
    [D2DCompileOptions(D2D1CompileOptions.Default | D2D1CompileOptions.EnableLinking | D2D1CompileOptions.IeeeStrictness)]
    [D2DShaderProfile(D2D1ShaderProfile.PixelShader50)]
    [AutoConstructor]
    private readonly partial struct PolarInversionShader
        : ID2D1PixelShader
    {
        private readonly float2 centerOffset;
        private readonly float radius2;
        private readonly float amount;

        public float4 Execute()
        {
            float2 scenePos = D2D.GetInput(0).XY;
            float2 relativePos = scenePos - this.centerOffset;

            float relativePosDot = Hlsl.Dot(relativePos, relativePos);
            float value = this.radius2 * Hlsl.Rcp(relativePosDot);
            float invertDistance = Hlsl.Lerp(1.0f, value, this.amount);
            float2 pos = relativePos * invertDistance;
            float2 samplePos = pos + this.centerOffset;

            return new float4(samplePos, 1, 0);
        }
    }

    [D2DInputCount(1)]
    [D2DInputSimple(0)]
    [D2DInputDescription(0, D2D1Filter.MinMagMipPoint)]
    [D2DOutputBuffer(D2D1BufferPrecision.Float32, D2D1ChannelDepth.Four)]
    [D2DCompileOptions(D2D1CompileOptions.Default | D2D1CompileOptions.EnableLinking | D2D1CompileOptions.IeeeStrictness)]
    [D2DShaderProfile(D2D1ShaderProfile.PixelShader50)]
    [AutoConstructor]
    private readonly partial struct SampleMapMirrorShader
        : ID2D1PixelShader
    {
        private readonly float2 size;

        public float4 Execute()
        {
            float4 samplePos = D2D.GetInput(0);
            float2 dxyabs = Hlsl.Abs(samplePos.XY);

            float2 wrappedDXY = Hlsl.Fmod(dxyabs, this.size);
            float2 iterationXY = dxyabs / this.size;
            float2 iterationXYF = Hlsl.Floor(iterationXY);
            float2 isMirroredXY = Hlsl.Fmod(iterationXYF, 2);
            float2 isNotMirroredXY = 1 - isMirroredXY;
            float2 mirroredDXY =
                isNotMirroredXY * wrappedDXY +
                isMirroredXY * (this.size - wrappedDXY);

            return new float4(mirroredDXY, samplePos.ZW);
        }
    }

    [D2DInputCount(1)]
    [D2DInputSimple(0)]
    [D2DInputDescription(0, D2D1Filter.MinMagMipPoint)]
    [D2DRequiresScenePosition]
    [D2DShaderProfile(D2D1ShaderProfile.PixelShader50)]
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

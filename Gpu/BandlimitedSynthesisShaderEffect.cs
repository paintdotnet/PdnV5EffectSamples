using ComputeSharp;
using ComputeSharp.D2D1;
using ComputeSharp.D2D1.Interop;
using PaintDotNet.Direct2D1;
using PaintDotNet.Effects.Gpu;
using PaintDotNet.PropertySystem;
using System;
using System.Collections.Generic;

namespace PaintDotNet.Effects.Samples.Gpu;

internal sealed partial class BandlimitedSynthesisShaderEffect
    : PropertyBasedGpuImageEffect
{
    private Guid shaderEffectID;
    private IDeviceEffect? shaderEffect;

    public BandlimitedSynthesisShaderEffect()
        : base(
            "Bandlimited Synthesis Shader (GPU Samples)",
            "PDN v5 Samples (GPU)",
            new GpuImageEffectOptions()
            {
                IsConfigurable = true
            })
    {
    }

    private enum PropertyNames
    {
        Time,
        Link
    }

    protected override PropertyCollection OnCreatePropertyCollection()
    {
        List<Property> properties = new List<Property>();
        properties.Add(new DoubleProperty(PropertyNames.Time, 0, 0, 100));
        properties.Add(new UriProperty(PropertyNames.Link, new Uri("https://www.shadertoy.com/view/WtScDt")));
        return new PropertyCollection(properties);
    }

    protected override void OnSetDeviceContext(IDeviceContext deviceContext)
    {
        deviceContext.Factory.RegisterEffectFromBlob(
            D2D1PixelShaderEffect.GetRegistrationBlob<Shader>(out this.shaderEffectID));

        base.OnSetDeviceContext(deviceContext);
    }

    protected override IDeviceImage OnCreateOutput(IDeviceContext deviceContext)
    {
        this.shaderEffect = deviceContext.CreateEffect(this.shaderEffectID);
        return this.shaderEffect;
    }

    protected override void OnUpdateOutput(IDeviceContext deviceContext)
    {
        this.shaderEffect!.SetValue(
            D2D1PixelShaderEffectProperty.ConstantBuffer,
            D2D1PixelShader.GetConstantBuffer(new Shader(
                new float3(
                    this.Environment.CanvasSize.Width,
                    this.Environment.CanvasSize.Height,
                    0),
                (float)this.Token.GetProperty<DoubleProperty>(PropertyNames.Time).Value)));

        base.OnUpdateOutput(deviceContext);
    }

    // Ported from ShaderToy: https://www.shadertoy.com/view/WtScDt
    [D2DInputCount(0)]
    [D2DRequiresScenePosition]
    [D2DShaderProfile(D2D1ShaderProfile.PixelShader50)]
    [AutoConstructor]
    private readonly partial struct Shader
        : ID2D1PixelShader
    {
        // The MIT License
        // Copyright © 2020 Inigo Quilez
        // https://www.youtube.com/c/InigoQuilez
        // https://iquilezles.org/
        // Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
        // associated documentation files (the "Software"), to deal in the Software without restriction,
        // including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
        // and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
        // subject to the following conditions: The above copyright notice and this permission notice shall be
        // included in all copies or substantial portions of the Software. THE SOFTWARE IS PROVIDED "AS IS",
        // WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
        // MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS
        // OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF
        // CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
        // OTHER DEALINGS IN THE SOFTWARE.

        // A simple way to prevent aliasing of cosine functions (the color
        // palette in this case is made of 8 layers) by attenuating them
        // when their oscillations become smaller than a pixel. Left is
        // direct use of cos(x), right is band-limited cos(x).
        //
        // Box-filtering of cos(x):
        //
        // (1/w)∫cos(t)dt with t ∈ (x-½w, x+½w)
        // = [sin(x+½w) - sin(x-½w)]/w
        // = cos(x)·sin(½w)/(½w)
        //
        // Can approximate smoothstep(2π,0,w) ≈ sin(w/2)/(w/2),
        // which you can also see as attenuating cos(x) when it 
        // oscilates more than once per pixel. More info:
        //
        // https://iquilezles.org/articles/bandlimiting
        //
        // Related Shader:
        //   https://www.shadertoy.com/view/WtScDt
        //   https://www.shadertoy.com/view/wtXfRH
        //   https://www.shadertoy.com/view/3tScWd

        // iResolution is the width/height of the render target
        private readonly float3 iResolution;

        // iTime is elapsed time, although here in PDN we don't have that for effects so we let the user specify it
        private readonly float iTime;

        // box-filted cos(x)
        float3 fcos(float3 x)
        {
            float3 w = Hlsl.Fwidth(x);
        #if true
            return Hlsl.Cos(x) * Hlsl.Sin(0.5f * w) / (0.5f * w); // exact
        #else
            return cos(x) * smoothstep(6.2832, 0.0, w); // approx
        #endif
        }

        // Note: The ShaderToy code uses an instance variable, `mode`. But, CS.D2D1 shaders
        // must be readonly structs, therefore stateless, so I converted the field to be
        // passed around as a parameter.
        //
        // pick raw cosine, or band-limited cosine
        float3 mcos(float3 x, bool mode) 
        { 
            return mode ? Hlsl.Cos(x) : fcos(x); 
        }

        // color palette, made of 8 cos functions
        // (see https://iquilezles.org/articles/palettes)
        float3 getColor(float t, bool mode)
        {
            float3 col = new float3(0.6f, 0.5f, 0.4f);
            col += 0.14f * mcos(6.2832f * t * 1.0f + new float3(0.0f, 0.5f, 0.6f), mode);
            col += 0.13f * mcos(6.2832f * t * 3.1f + new float3(0.5f, 0.6f, 1.0f), mode);
            col += 0.12f * mcos(6.2832f * t * 5.1f + new float3(0.1f, 0.7f, 1.1f), mode);
            col += 0.11f * mcos(6.2832f * t * 9.1f + new float3(0.1f, 0.5f, 1.2f), mode);
            col += 0.10f * mcos(6.2832f * t * 17.1f + new float3(0.0f, 0.3f, 0.9f), mode);
            col += 0.09f * mcos(6.2832f * t * 31.1f + new float3(0.1f, 0.5f, 1.3f), mode);
            col += 0.08f * mcos(6.2832f * t * 65.1f + new float3(0.1f, 0.5f, 1.3f), mode);
            col += 0.07f * mcos(6.2832f * t * 131.1f + new float3(0.3f, 0.2f, 0.8f), mode);
            return col;
        }

        void mainImage(out float4 fragColor, float2 fragCoord)
        {
            // coordiantes
            float2 q = (2.0f * fragCoord - this.iResolution.XY) / this.iResolution.Y;

            // separation
            //float th = 0; // (iMouse.z > 0.001) ? (2.0 * iMouse.x - iResolution.x) / iResolution.y : 1.8 * sin(iTime);
            bool mode = false; // (q.X < th);

            // deformation
            float2 p = 2.0f * q / Hlsl.Dot(q, q);

            // animation
            p.XY += 0.05f * this.iTime;

            // texture
            float3 col = Hlsl.Min(getColor(p.X, mode), getColor(p.Y, mode));

            // vignetting
            col *= 1.5f - 0.2f * Hlsl.Length(q);

            // Note: Remove the separator line between the aliased and antialiased version
            // separation
            //col *= Hlsl.SmoothStep(0.005f, 0.010f, Hlsl.Abs(q.X - th));

            // Note: Remove the palette at the bottom of the image. We just want the trippy part :)
            // palette
            //if (q.Y < -0.9f) col = getColor(fragCoord.X / iResolution.X, mode);

            fragColor = new float4(col, 1.0f);
        }

        // Note: This is the Direct2D pixel shader method. The `mainImage` above is ShaderToy/GLSL's equivalent
        //       This Execute() implementation can be used to help port any other ShaderToy shaders.
        public float4 Execute()
        {
            float2 fragCoord = D2D.GetScenePosition().XY;
            mainImage(out float4 fragColor, fragCoord);
            return fragColor;
        }
    }
}

// This work is licensed under a Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported License.

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
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// This is an example of a complicated shader ported from GLSL over at ShaderToy
// This sample also shows how to choose at runtime between running a shader in either IEEE strict or relaxed mode.
// Originally written by "gaz": https://www.shadertoy.com/view/tdyBR1

namespace PaintDotNet.Effects.Samples.Gpu;

internal sealed partial class NightCircuitShaderEffect
    : PropertyBasedGpuImageEffect
{
    public NightCircuitShaderEffect()
        : base(
            "Night Circuit Shader (GPU Sample)",
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
        Quality,
        IeeeStrict,
        Link
    }

    protected override PropertyCollection OnCreatePropertyCollection()
    {
        List<Property> properties = new List<Property>();

        properties.Add(new Int32Property(PropertyNames.Time, 0, 0, 3000));
        properties.Add(new Int32Property(PropertyNames.Quality, 2, 1, 4));
        properties.Add(new BooleanProperty(PropertyNames.IeeeStrict, false));
        properties.Add(new UriProperty(PropertyNames.Link, new Uri("https://www.shadertoy.com/view/tdyBR1")));

        return new PropertyCollection(properties);
    }

    protected override ControlInfo OnCreateConfigUI(PropertyCollection props)
    {
        ControlInfo configUI = CreateDefaultConfigUI(props);

        configUI.SetPropertyControlType(PropertyNames.Link, PropertyControlType.LinkLabel);
        configUI.SetPropertyControlValue(PropertyNames.Link, ControlInfoPropertyNames.DisplayName, "Ported from ShaderToy");

        return configUI;
    }

    protected override InspectTokenAction OnInspectTokenChanges(PropertyBasedEffectConfigToken oldToken, PropertyBasedEffectConfigToken newToken)
    {
        if (oldToken.GetProperty<Int32Property>(PropertyNames.Quality).Value != newToken.GetProperty<Int32Property>(PropertyNames.Quality).Value ||
            oldToken.GetProperty<BooleanProperty>(PropertyNames.IeeeStrict).Value != newToken.GetProperty<BooleanProperty>(PropertyNames.IeeeStrict).Value)
        {
            return InspectTokenAction.RecreateOutput;
        }
        else
        {
            return InspectTokenAction.UpdateOutput;
        }
    }

    private Guid ieeeRelaxedEffectID;
    private Guid ieeeStrictEffectID;
    private Vector2Float[]? subPixelOffsets;
    private HlslBinaryOperatorEffect[]? subPxScenePosEffects;
    private IDeviceEffect[]? pixelShaderEffects;

    protected override void OnSetDeviceContext(IDeviceContext deviceContext)
    {
        deviceContext.Factory.RegisterEffectFromBlob(D2D1PixelShaderEffect.GetRegistrationBlob<IeeeRelaxedShader>(out this.ieeeRelaxedEffectID));
        deviceContext.Factory.RegisterEffectFromBlob(D2D1PixelShaderEffect.GetRegistrationBlob<IeeeStrictShader>(out this.ieeeStrictEffectID));
        base.OnSetDeviceContext(deviceContext);
    }

    protected override void OnInvalidateDeviceResources()
    {
        if (this.subPixelOffsets != null)
        {
            for (int i = 0; i < this.subPixelOffsets.Length; ++i)
            {
                this.subPxScenePosEffects![i]?.Dispose();
                this.pixelShaderEffects![i]?.Dispose();
            }

            this.subPixelOffsets = null;
            this.subPxScenePosEffects = null;
            this.pixelShaderEffects = null;
        }

        base.OnInvalidateDeviceResources();
    }

    protected override IDeviceImage OnCreateOutput(IDeviceContext deviceContext)
    {
        // Multisample Antialiasing (MSAA) is implemented by rendering the shader multiple times at various subpixel offsets
        // Each effect output is summed together using the CompositeEffect, then divided at the end with the MultiplyConstEffect

        bool ieeeStrict = this.Token.GetProperty<BooleanProperty>(PropertyNames.IeeeStrict).Value;

        int quality = this.Token.GetProperty<Int32Property>(PropertyNames.Quality).Value;
        this.subPixelOffsets = EffectHelpers.GetRgssOffsets(quality);

        // 'using' is fine and good here, because the IDeviceEffect.SetInput() method (see below when
        // this is used as the input for MultiplyConstEffect) will acquire its own 'ref' that keeps the
        // underlying object alive (this is exactly like COM -- IDeviceEffect wraps ID2D1Effect after
        // all, which is a COM object.)
        // It is not required to use 'using', it can just be a little better for performance so that
        // the .NET garbage collector has less objects to finalize.
        using CompositeEffect compositeEffect = new CompositeEffect(deviceContext);
        compositeEffect.InputCount = this.subPixelOffsets.Length;
        compositeEffect.Properties.Mode.SetValue(CompositeMode.Plus);

        this.subPxScenePosEffects = new HlslBinaryOperatorEffect[this.subPixelOffsets.Length];
        this.pixelShaderEffects = new IDeviceEffect[this.subPixelOffsets.Length];
        for (int i = 0; i < this.pixelShaderEffects.Length; ++i)
        {
            // The ScenePositionEffect is created once per pixel shader effect, despite being identical
            // for each, in order to permit Direct2D to link the shaders together.
            // An effect cannot be linked if its output is consumed by more than one effect, in which
            // case it would first be rendered into a temporary buffer (texture). Sometimes that is fine,
            // especially for an expensive shader; however, the ScenePositionEffect is super simple and
            // it is better to have it linked even if it's somewhat redundant.
            // 
            // More info on Direct2D effect shader linking: https://docs.microsoft.com/en-us/windows/win32/direct2d/effect-shader-linking

            using ScenePositionEffect scenePosEffect = new ScenePositionEffect(deviceContext);

            this.subPxScenePosEffects[i] = new HlslBinaryOperatorEffect(
                deviceContext,
                scenePosEffect,
                HlslBinaryOperator.Add,
                new FloodEffect(deviceContext, new Vector4Float(this.subPixelOffsets[i], 0, 0)));

            this.pixelShaderEffects[i] = deviceContext.CreateEffect(ieeeStrict ? this.ieeeStrictEffectID : this.ieeeRelaxedEffectID);
            this.pixelShaderEffects[i]!.SetInput(0, this.subPxScenePosEffects[i]);

            compositeEffect.SetInput(i, this.pixelShaderEffects[i]);
        }

        // After accumulating the samples, take the average by dividing by the number of samples
        // Use multiplication of (1/n) instead of division because division is relatively slow
        HlslBinaryOperatorEffect divConstEffect = new HlslBinaryOperatorEffect(deviceContext);
        divConstEffect.Properties.Parameter1.SetValue(HlslEffectParameter.Input);
        divConstEffect.Properties.Input1.Set(compositeEffect);
        divConstEffect.Properties.Operator.SetValue(HlslBinaryOperator.Multiply);
        divConstEffect.Properties.Parameter2.SetValue(HlslEffectParameter.Value);
        divConstEffect.Properties.Value2.SetValue(new Vector4Float((float)(1.0 / this.subPixelOffsets.Length)));

        return divConstEffect;
    }

    protected override void OnUpdateOutput(IDeviceContext deviceContext)
    {
        int time = this.Token.GetProperty<Int32Property>(PropertyNames.Time).Value;
        bool ieeeStrict = this.Token.GetProperty<BooleanProperty>(PropertyNames.IeeeStrict).Value;

        for (int i = 0; i < this.subPixelOffsets!.Length; ++i)
        {
            float iTime = time / 30.0f;
            float2 iResolution = new float2(this.Environment.CanvasSize.Width, this.Environment.CanvasSize.Height);

            this.pixelShaderEffects![i].SetValue(
                D2D1PixelShaderEffectProperty.ConstantBuffer,
                ieeeStrict
                    ? D2D1PixelShader.GetConstantBuffer(new IeeeStrictShader(iTime, iResolution))
                    : D2D1PixelShader.GetConstantBuffer(new IeeeRelaxedShader(iTime, iResolution)));
        }

        base.OnUpdateOutput(deviceContext);
    }

    [D2DInputCount(1)]
    [D2DInputSimple(0)]
    [D2DInputDescription(0, D2D1Filter.MinMagMipPoint)]
    [D2DRequiresScenePosition]
    [D2DShaderProfile(D2D1ShaderProfile.PixelShader50)]
    [D2DCompileOptions(D2D1CompileOptions.Default | D2D1CompileOptions.EnableLinking)]
    [AutoConstructor]
    private partial struct IeeeRelaxedShader
        : ID2D1PixelShader
    {
        // TODO: bug in ComputeSharp means I have to have a copy of this const over here, not just in ShaderImpl. https://github.com/Sergio0694/ComputeSharp/issues/298
        //#define TAU atan(1.)*8.
        private static readonly float TAU = Hlsl.Atan(1.0f) * 8.0f;

        // Standard input constants for ShaderToy shaders
        private float iTime;
        private readonly float2 iResolution;

        // This Execute() method adapts ShaderToy's mainImage() to work with ComputeSharp.D2D1's expected method signature
        public float4 Execute()
        {
            float2 scenePos = D2D.GetInput(0).XY;
            ShaderImpl.mainImage(out float4 fragColor, scenePos, this.iTime, this.iResolution);
            return fragColor;
        }
    }

    [D2DInputCount(1)]
    [D2DInputSimple(0)]
    [D2DInputDescription(0, D2D1Filter.MinMagMipPoint)]
    [D2DRequiresScenePosition]
    [D2DShaderProfile(D2D1ShaderProfile.PixelShader50)]
    [D2DCompileOptions(D2D1CompileOptions.Default | D2D1CompileOptions.EnableLinking | D2D1CompileOptions.IeeeStrictness)]
    [AutoConstructor]
    private partial struct IeeeStrictShader
        : ID2D1PixelShader
    {
        // TODO: bug in ComputeSharp means I have to have a copy of this const over here, not just in ShaderImpl. https://github.com/Sergio0694/ComputeSharp/issues/298
        //#define TAU atan(1.)*8.
        private static readonly float TAU = Hlsl.Atan(1.0f) * 8.0f;

        // Standard input constants for ShaderToy shaders
        private float iTime;
        private readonly float2 iResolution;

        // This Execute() method adapts ShaderToy's mainImage() to work with ComputeSharp.D2D1's expected method signature
        public float4 Execute()
        {
            float2 scenePos = D2D.GetInput(0).XY;
            ShaderImpl.mainImage(out float4 fragColor, scenePos, this.iTime, this.iResolution);
            return fragColor;
        }
    }

    private static class ShaderImpl
    {
        //#define TAU atan(1.)*8.
        private static readonly float TAU = Hlsl.Atan(1.0f) * 8.0f;

        static void lookAt(ref float3 rd, float3 ro, float3 ta, float3 up)
        {
            float3 w = Hlsl.Normalize(ta - ro);
            float3 u = Hlsl.Normalize(Hlsl.Cross(w, up));
            rd = rd.X * u + rd.Y * Hlsl.Cross(u, w) + rd.Z * w;
        }

        private static void pointAt(ref float3 p, float3 dir, float3 up)
        {
            float3 u = Hlsl.Normalize(Hlsl.Cross(dir, up));
            p = new float3(Hlsl.Dot(p, u), Hlsl.Dot(p, Hlsl.Cross(u, dir)), Hlsl.Dot(p, dir));
        }

        private static void rot(ref float3 p, float3 a, float t)
        {
            a = Hlsl.Normalize(a);
            float3 u = Hlsl.Cross(a, p), v = Hlsl.Cross(a, u);
            p = u * Hlsl.Sin(t) + v * Hlsl.Cos(t) + a * Hlsl.Dot(a, p);
        }

        private static void rot(ref float2 p, float t)
        {
            p = p * Hlsl.Cos(t) + new float2(-p.Y, p.X) * Hlsl.Sin(t);
        }

        // https://www.shadertoy.com/view/WdfcWr
        private static void pSFold(ref float2 p, float n)
        {
            float h = Hlsl.Floor(Hlsl.Log2(n)), a = TAU * Hlsl.Exp2(h) / n;
            for (float i = 0; i < h + 2; i++)
            {
                float2 v = new float2(-Hlsl.Cos(a), Hlsl.Sin(a));
                float g = Hlsl.Dot(p, v);
                p -= (g - Hlsl.Sqrt(g * g + 2e-3f)) * v;
                a *= 0.5f;
            }
        }

        private const float seed = 2576;

        // The #define is translated into a method overload for each type that it's needed for
        //#define hash(p)fract(sin(p*12345.5))
        private static float hash(float p)
        {
            return Hlsl.Frac(Hlsl.Sin(p * 12345.5f));
        }

        private static float2 hash(float2 p)
        {
            return Hlsl.Frac(Hlsl.Sin(p * 12345.5f));
        }

        private static float3 hash(float3 p)
        {
            return Hlsl.Frac(Hlsl.Sin(p * 12345.5f));
        }

        private static float3 randVec(float s)
        {
            float2 n = hash(new float2(s, s + 215.3f));
            return new float3(Hlsl.Cos(n.Y) * Hlsl.Cos(n.X), Hlsl.Sin(n.Y), Hlsl.Cos(n.Y) * Hlsl.Sin(n.X));
        }

        private static float3 randCurve(float t, float n)
        {
            float3 p = default;
            for (int i = 0; i < 3; i++)
            {
                p += randVec(n += 365.0f) * Hlsl.Sin((t *= 1.3f) + Hlsl.Sin(t * 0.6f) * 0.5f);
            }
            return p;
        }

        private static float3 orbit(float t, float n, float iTime)
        {
            float3 p = randCurve(-t * 1.5f + iTime, seed) * 5;
            float3 off = randVec(n) * (t + 0.05f) * 0.6f;
            float time = iTime + hash(n) * 5.0f;
            return p + off * Hlsl.Sin(time + 0.5f * Hlsl.Sin(0.5f * time));
        }

        // rewrote 20/12/01
        private static void sFold45(ref float2 p)
        {
            float2 v = Hlsl.Normalize(new float2(1, -1));
            float g = Hlsl.Dot(p, v);
            p -= (g - Hlsl.Sqrt(g * g + 5e-5f)) * v;
        }

        private static float stella(float3 p, float s)
        {
            p = Hlsl.Sqrt(p * p + 5e-5f); // https://iquilezles.org/articles/functions
            sFold45(ref p.XZ);
            sFold45(ref p.YZ);
            return Hlsl.Dot(p, Hlsl.Normalize(new float3(1, 1, -1))) - s;
        }

        /*
        float stella(float3 p, float s)
        {
            p=abs(p);
            if(p.x<p.z)p.xz=p.zx;
            if(p.y<p.z)p.yz=p.zy;
            return dot(p,normalize(new float3(1,1,-1)))-s;
        }
        */

        private static float stellas(float3 p, float iTime)
        {
            p.Y -= -iTime;
            float c = 2.0f;
            float3 e = Hlsl.Floor(p / c);
            e = Hlsl.Sin(11.0f * (2.5f * e + 3.0f * e.YZX + 1.345f));
            p -= e * 0.5f;
            p = Hlsl.Fmod(p, c) - c * 0.5f;
            rot(ref p, hash(e + 166.887f) - 0.5f, iTime * 1.5f);
            return Hlsl.Min(0.7f, stella(p, 0.08f));
        }

        private static float structure(float3 p, ref float g1, ref float g2, float iTime)
        {
            float d = 1e3f, d0;
            for (int i = 0; i < 12; i++)
            {
                float3 q = p;
                float3 w = Hlsl.Normalize(new float3(Hlsl.Sqrt(5.0f) * 0.5f + 0.5f, 1, 0));
                w.XY *= new float2(i >> 1 & 1, i & 1) * 2.0f - 1.0f;

                //w = new float3[] { w, w.YZX, w.ZXY }[i % 3];
                uint imod3 = (uint)i % 3;
                w = (imod3 == 0) ? w :
                    (imod3 == 1) ? w.YZX :
                    w.ZXY;

                pointAt(ref q, w, -Hlsl.Sign(w.X + w.Y + w.Z) * Hlsl.Sign(w) * w.ZXY);

                d0 = Hlsl.Length(q - new float3(0, 0, Hlsl.Clamp(q.Z, 2.0f, 8.0f))) - 0.4f + q.Z * 0.05f;
                d = Hlsl.Min(d, d0);
                g2 += 0.1f / (0.1f + d0 * d0); // Distance glow by balkhan

                float c = 0.8f;
                float e = Hlsl.Floor(q.Z / c - c * 0.5f);
                q.Z -= c * Hlsl.Clamp(Hlsl.Round(q.Z / c), 3.0f, 9.0f);

                q.Z -= Hlsl.Clamp(q.Z, -0.05f, 0.05f);
                pSFold(ref q.XY, 5.0f);
                q.Y -= 1.4f - e * 0.2f + Hlsl.Sin(iTime * 10.0f + e + (float)i) * 0.05f;
                q.X -= Hlsl.Clamp(q.X, -2.0f, 2.0f);
                q.Y -= Hlsl.Clamp(q.Y, 0.0f, 0.2f);

                d0 = Hlsl.Length(q) * 0.7f - 0.05f;
                d = Hlsl.Min(d, d0);
                if (e == 2.0f + Hlsl.Floor(Hlsl.Fmod(iTime * 5.0f, 7.0f)))
                    g1 += 0.1f / (0.1f + d0 * d0);
            }
            return d;
        }

        private static float rabbit(float3 p, ref float g3, float iTime)
        {
            p -= randCurve(iTime, seed) * 5.0f;
            rot(ref p, new float3(1, 1, 1), iTime);
            float d = stella(p, 0.2f);
            g3 += 0.1f / (0.1f + d * d);
            return d;
        }

        private static float map(float3 p, ref float g1, ref float g2, ref float g3, float iTime)
        {
            return Hlsl.Min(Hlsl.Min(stellas(p, iTime), structure(p, ref g1, ref g2, iTime)), rabbit(p, ref g3, iTime));
        }

        private static float3 calcNormal(float3 p, ref float g1, ref float g2, ref float g3, float iTime)
        {
            float3 n = default;
            for (int i = 0; i < 4; i++)
            {
                float3 e = 0.001f * (new float3(9 >> i & 1, i >> 1 & 1, i & 1) * 2.0f - 1.0f);
                n += e * map(p + e, ref g1, ref g2, ref g3, iTime);
            }
            return Hlsl.Normalize(n);
        }

        private static float3 doColor(float3 p, float iTime)
        {
            if (stellas(p, iTime) < 0.001f) return new float3(0.7f, 0.7f, 1);
            return new float3(1, 1, 1);
        }

        private static float3 hue(float h)
        {
            return Hlsl.Cos((new float3(0, 2, -2) / 3.0f + h) * TAU) * 0.5f + 0.5f;
        }

        private static float3 cLine(float3 ro, float3 rd, float3 a, float3 b)
        {
            float3 ab = Hlsl.Normalize(b - a), ao = a - ro;
            float d0 = Hlsl.Dot(rd, ab), d1 = Hlsl.Dot(rd, ao), d2 = Hlsl.Dot(ab, ao);
            float t = (d0 * d1 - d2) / (1.0f - d0 * d0) / Hlsl.Length(b - a);
            t = Hlsl.Clamp(t, 0.0f, 1.0f);
            float3 p = a + (b - a) * t - ro;
            return new float3(Hlsl.Length(Hlsl.Cross(p, rd)), Hlsl.Dot(p, rd), t);
        }

        private static float int7_10_12_15(int index)
        {
            return index == 0 ? 7
                : index == 1 ? 10
                : index == 2 ? 12
                : 15;
        }

        public static void mainImage(out float4 fragColor, float2 fragCoord, float iTime, float2 iResolution)
        {
            float g1 = 0;
            float g2 = 0;
            float g3 = 0;

            float2 p = (2.0f * fragCoord - iResolution.XY) / iResolution.Y;
            float3 col = new float3(0.0f, 0.0f, 0.05f);

            // ComputeSharp.D2D1 does not currently support 'new int[] { ... }', so it has been translated to a method call (see above)
            //float3 ro = new float3(1, 0, new int[] { 7, 10, 12, 15 }[(int)(Hlsl.Abs(4.0f * Hlsl.Sin(iTime * 0.3f + 3.0f * Hlsl.Sin(iTime * 0.2f)))) & 3]);
            float3 ro = new float3(1, 0, int7_10_12_15((int)(Hlsl.Abs(4.0f * Hlsl.Sin(iTime * 0.3f + 3.0f * Hlsl.Sin(iTime * 0.2f)))) & 3));

            rot(ref ro, new float3(1, 1, 1), iTime * 0.2f);
            float3 ta = new float3(2, 1, 2);
            float3 rd = Hlsl.Normalize(new float3(p, 2));
            lookAt(ref rd, ro, ta, new float3(0, 1, 0));
            float z = 0.0f, i, d = 0, ITR = 50.0f;
            for (i = 0.0f; i < ITR; i++)
            {
                z += d = map(ro + rd * z, ref g1, ref g2, ref g3, iTime);
                if (d < 0.001f || z > 30.0f) break;
            }
            if (d < .001f)
            {
                float3 p2 = ro + rd * z;
                float3 nor = calcNormal(p2, ref g1, ref g2, ref g3, iTime);
                float3 li = Hlsl.Normalize(new float3(1, 1, -1));
                col = doColor(p2, iTime);
                col *= Hlsl.Pow(Hlsl.Abs(1.9f - i / ITR), 2.0f);
                col *= Hlsl.Clamp(Hlsl.Dot(nor, li), 0.3f, 1.0f);
                col *= Hlsl.Max(0.5f + 0.5f * nor.Y, 0.2f);
                col += new float3(0.8f, 0.1f, 0.0f) * Hlsl.Pow(Hlsl.Abs(Hlsl.Clamp(Hlsl.Dot(Hlsl.Reflect(Hlsl.Normalize(p2 - ro), nor), Hlsl.Normalize(new float3(-1, -1, -1))), 0.0f, 1.0f)), 30.0f);
                col += new float3(0.1f, 0.2f, 0.5f) * Hlsl.Pow(Hlsl.Abs(Hlsl.Clamp(Hlsl.Dot(Hlsl.Reflect(Hlsl.Normalize(p2 - ro), nor), Hlsl.Normalize(new float3(1, 1, -1))), 0.0f, 1.0f)), 30.0f);
                col = Hlsl.Lerp(new float3(0, 0, 0), col, Hlsl.Exp(-z * z * 0.00001f));
            }
            col += new float3(0.9f, 0.1f, 0.0f) * g1 * 0.05f;
            col += new float3(0.0f, 0.3f, 0.7f) * g2 * 0.08f;
            col += new float3(0.5f, 0.3f, 0.1f) * g3 * 0.15f;

            // https://www.shadertoy.com/view/wtXSzX
            float3 de;
            ITR = 40.0f;
            for (i = 0.0f; i < 1.0f; i += 1.0f / 7.0f)
            {
                de = new float3(1e9f, 1e9f, 1e9f);
                float off = hash(i * 234.6f + 256.0f);
                for (float j = 0.0f; j < 1.0f; j += 1.0f / ITR)
                {
                    float t = j + off * 0.5f;
                    float3 c = cLine(ro, rd, orbit(t, off, iTime), orbit(t + 1.0f / ITR, off, iTime));
                    if (de.X * de.X * de.Y > c.X * c.X * c.Y)
                    {
                        de = c;
                        de.Z = j + c.Z / ITR;
                    }
                }
                float s = Hlsl.Pow(Hlsl.Max(0.0f, 0.6f - de.Z), 2.0f) * 0.1f;
                if (de.Y > 0.0f && z > de.Y)
                    col += Hlsl.Lerp(new float3(1, 1, 1), hue(i), 0.8f) * (1.0f - de.Z * 0.9f) * Hlsl.SmoothStep(s + 0.17f, s, de.X) * 0.7f;
            }
            float col0 = 0.8f + 0.3f * Hlsl.Sin(iTime * 0.5f + 3.0f * Hlsl.Sin(iTime * 0.3f));
            col = Hlsl.Pow(Hlsl.Abs(col), new float3(col0, col0, col0));

            fragColor = new float4(col, 1);
        }
    }
}

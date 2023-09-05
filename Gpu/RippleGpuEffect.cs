// This advanced sample shows how to implement and use a custom HLSL pixel shader that computes
// a sample map. SampleMapRenderer is used to render the sample map multiple times at various
// subpixel offsets to achieve high quality output.
// 
// The shader is implemented using ComputeSharp.D2D1, and wrapped in a PixelShaderEffect
// (also supplied by ComputeSharp.D2D1).
//
// Ported from Windows SDK sample: https://github.com/microsoft/Windows-universal-samples/tree/main/Samples/D2DCustomEffects/cpp/PixelShader

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

internal sealed partial class RippleGpuEffect
    : PropertyBasedGpuImageEffect
{
    private Guid sampleMapEffectID;
    private SampleMapRenderer? sampleMapRenderer;

    public RippleGpuEffect()
        : base(
            "Ripple",
            "PDN v5 Samples (GPU)",
            GpuImageEffectOptions.Create() with
            {
                IsConfigurable = true
            })
    {
    }

    private enum PropertyNames
    {
        Size,
        Frequency,
        Phase,
        Amplitude,
        Spread,
        Center,
        Quality
    }

    protected unsafe override PropertyCollection OnCreatePropertyCollection()
    {
        List<Property> properties = new List<Property>();

        properties.Add(new DoubleProperty(PropertyNames.Size, 0.5, 0.0001, 1.0));
        properties.Add(new DoubleProperty(PropertyNames.Frequency, 100, 0, 1000));
        properties.Add(new DoubleProperty(PropertyNames.Phase, 0, -100, +100));
        properties.Add(new DoubleProperty(PropertyNames.Amplitude, 100, 0.0001, 1000.0));
        properties.Add(new DoubleProperty(PropertyNames.Spread, 1, 0.0001, 100));
        properties.Add(new DoubleVectorProperty(PropertyNames.Center, new Vector2Double(0, 0), new Vector2Double(-1.0, -1.0), new Vector2Double(+1.0, +1.0)));
        properties.Add(new Int32Property(PropertyNames.Quality, 3, 1, 8));

        return new PropertyCollection(properties);
    }

    protected override ControlInfo OnCreateConfigUI(PropertyCollection props)
    {
        ControlInfo controlInfo = CreateDefaultConfigUI(props);

        controlInfo.SetPropertyControlValue(PropertyNames.Frequency, ControlInfoPropertyNames.UseExponentialScale, true);
        controlInfo.SetPropertyControlValue(PropertyNames.Amplitude, ControlInfoPropertyNames.UseExponentialScale, true);
        controlInfo.SetPropertyControlValue(PropertyNames.Spread, ControlInfoPropertyNames.UseExponentialScale, true);

        controlInfo.SetPropertyControlValue(
            PropertyNames.Center,
            ControlInfoPropertyNames.StaticImageUnderlay,
            this.Environment.GetSourceBitmapBgra32());

        return controlInfo;
    }

    protected override void OnSetDeviceContext(IDeviceContext deviceContext)
    {
        // Register a D2D1PixelShaderEffect for the shader. The PixelShaderEffect must be registered once per shader.
        deviceContext.Factory.RegisterEffectFromBlob(
            D2D1PixelShaderEffect.GetRegistrationBlob<SampleMapShader>(out this.sampleMapEffectID));

        base.OnSetDeviceContext(deviceContext);
    }

    protected override IDeviceImage OnCreateOutput(IDeviceContext deviceContext)
    {
        double width = this.Environment.Document.Size.Width;
        double height = this.Environment.Document.Size.Height;

        double size = this.Token.GetProperty<DoubleProperty>(PropertyNames.Size).Value;
        double sizePx = size * (Math.Max(width, height) / 2.0);

        double frequency = this.Token.GetProperty<DoubleProperty>(PropertyNames.Frequency).Value;
        double phase = this.Token.GetProperty<DoubleProperty>(PropertyNames.Phase).Value;
        double amplitude = this.Token.GetProperty<DoubleProperty>(PropertyNames.Amplitude).Value;
        double spread = this.Token.GetProperty<DoubleProperty>(PropertyNames.Spread).Value;

        Vector2Double center = this.Token.GetProperty<DoubleVectorProperty>(PropertyNames.Center).Value;
        Point2Double centerPoint = new Point2Double(
            (width + (center.X * width)) / 2.0,
            (height + (center.Y * height)) / 2.0);

        int quality = this.Token.GetProperty<Int32Property>(PropertyNames.Quality).Value;

        // To implement multisampling, the ripple effect is run multiple times at various sampling offsets,
        // which are then blended together by the SampleMapRenderer to form the final high-quality output.
        // The # of samples is equal to the square of the quality value, so a quality value of [1,2,3,4,...,8]
        // will use [1,4,9,16,...,64] samples.

        this.sampleMapRenderer = new SampleMapRenderer(deviceContext);
        this.sampleMapRenderer.Properties.Input.Set(this.Environment.SourceImage);
        this.sampleMapRenderer.Properties.EdgeMode.SetValue(SampleMapEdgeMode.Clamp);

        Vector2Float[] sampleOffsets = EffectHelpers.GetRgssOffsets(quality);
        this.sampleMapRenderer.Properties.SampleMaps.SetCount(sampleOffsets.Length);

        for (int i = 0; i < sampleOffsets.Length; ++i)
        {
            ScenePositionEffect scenePosSampleMap = new ScenePositionEffect(deviceContext);

            HlslBinaryOperatorEffect scenePosRgssSampleMap = new HlslBinaryOperatorEffect(deviceContext);
            scenePosRgssSampleMap.Properties.Parameter1.SetValue(HlslEffectParameter.Input);
            scenePosRgssSampleMap.Properties.Input1.Set(scenePosSampleMap);
            scenePosRgssSampleMap.Properties.Operator.SetValue(HlslBinaryOperator.Add);
            scenePosRgssSampleMap.Properties.Parameter2.SetValue(HlslEffectParameter.Value);
            scenePosRgssSampleMap.Properties.Value2.SetValue(new Vector4Float(sampleOffsets[i], 0, 0));

            IDeviceEffect rippleSampleMap = deviceContext.CreateEffect(this.sampleMapEffectID);
            rippleSampleMap.SetInput(0, scenePosRgssSampleMap);
            rippleSampleMap.SetValue(
                D2D1PixelShaderEffectProperty.ConstantBuffer,
                D2D1PixelShader.GetConstantBuffer(new SampleMapShader(
                    (float)sizePx,
                    (float)frequency,
                    (float)phase,
                    (float)amplitude,
                    (float)spread,
                    new float2(
                        (float)centerPoint.X,
                        (float)centerPoint.Y))));

            this.sampleMapRenderer.Properties.SampleMaps[i].Set(rippleSampleMap);
        }

        return this.sampleMapRenderer;
    }

    [D2DInputCount(1)]
    [D2DInputSimple(0)]
    [D2DInputDescription(0, D2D1Filter.MinLinearMagMipPoint)]
    [D2DShaderProfile(D2D1ShaderProfile.PixelShader50)]
    [AutoConstructor]
    private readonly partial struct SampleMapShader
        : ID2D1PixelShader
    {
        public readonly float size;
        public readonly float frequency;
        public readonly float phase;
        public readonly float amplitude;
        public readonly float spread;
        public readonly float2 center;

        public float4 Execute()
        {
            // Sample map shaders read the scene position from the input, instead of via D2D.GetScenePos()
            float2 scenePos = D2D.GetInput(0).XY; 

            float2 toPixel = scenePos - this.center;

            // Scale distance such that the ripple's displacement decays to 0 at the requested size (in pixels)
            float distance = Hlsl.Length(toPixel * (1.0f / this.size));
            float2 direction = Hlsl.Normalize(toPixel);

            float2 wave = default;
            Hlsl.SinCos(this.frequency * distance + this.phase, out wave.X, out wave.Y);

            // Clamp the falloff value so that the ripple does not extend beyond the limit.
            // Spread controls how quickly the ripple decays, up to the limit.
            float falloff = Hlsl.Saturate(1.0f - distance);
            falloff = Hlsl.Pow(falloff, 1.0f / this.spread);

            // Calculate new mapping coordinates based on the frequency, center, and amplitude.
            float2 inputOffset = (wave.X * falloff * this.amplitude) * direction;

            // Not currently using this value -- planning to make this possible in a future build
            //float lighting = Hlsl.Lerp(1.0f, 1.0f + wave.X * falloff * 0.2f, Hlsl.Saturate(this.amplitude / 20.0f));
            //sample.RGB *= lighting;

            // The return value from a sample map shader is in the format (X,Y,*,A)
            // X and Y are the pixel to sample from, which is then multiplied by A (alpha)
            // The third component of the return value is currently discarded
            float2 samplePos = scenePos + inputOffset;
            return new float4(samplePos, 1, 1);
        }
    }
}

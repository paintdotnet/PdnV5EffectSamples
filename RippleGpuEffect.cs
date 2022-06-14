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
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;
using PaintDotNet.Rendering;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace PaintDotNet.Effects.Gpu.Samples;

// Disambiguate between System.Drawing.IDeviceContext and PaintDotNet.Direct2D1.IDeviceContext
using IDeviceContext = PaintDotNet.Direct2D1.IDeviceContext;

internal sealed partial class RippleGpuEffect
    : PropertyBasedGpuImageEffect
{
    public RippleGpuEffect()
        : base(
            "Ripple (GPU Sample)",
            null, // no icon
            "GPU Samples",
            new GpuImageEffectOptions()
            {
                Flags = EffectFlags.Configurable
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

    protected override PropertyCollection OnCreatePropertyCollection()
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

        return controlInfo;
    }

    protected override void OnSetRenderInfo(PropertyBasedEffectConfigToken newToken)
    {
        double width = this.SourceSize.Width;
        double height = this.SourceSize.Height;

        double size = newToken.GetProperty<DoubleProperty>(PropertyNames.Size).Value;
        this.sizePx = size * (Math.Max(width, height) / 2.0);

        this.frequency = newToken.GetProperty<DoubleProperty>(PropertyNames.Frequency).Value;
        this.phase = newToken.GetProperty<DoubleProperty>(PropertyNames.Phase).Value;
        this.amplitude = newToken.GetProperty<DoubleProperty>(PropertyNames.Amplitude).Value;
        this.spread = newToken.GetProperty<DoubleProperty>(PropertyNames.Spread).Value;

        Vector2Double center = newToken.GetProperty<DoubleVectorProperty>(PropertyNames.Center).Value;
        this.centerPoint = new Point2Double(
            (width + (center.X * width)) / 2.0,
            (height + (center.Y * height)) / 2.0);

        this.quality = newToken.GetProperty<Int32Property>(PropertyNames.Quality).Value;

        base.OnSetRenderInfo(newToken);
    }

    private Guid sampleMapEffectID;
    private double sizePx;
    private double frequency;
    private double phase;
    private double amplitude;
    private double spread;
    private int quality;
    private Point2Double centerPoint;
    private ISampleMapRenderer? sampleMapRenderer;

    protected override void OnSetDeviceContext(IDeviceContext deviceContext)
    {
        // Register a D2D1PixelShaderEffect for the shader. The PixelShaderEffect must be registered once per shader.
        deviceContext.Factory.RegisterEffectFromBlob(
            D2D1PixelShaderEffect.GetRegistrationBlob<SampleMapShader>(out this.sampleMapEffectID));

        base.OnSetDeviceContext(deviceContext);
    }

    protected override IDeviceImage OnCreateOutput(IDeviceContext deviceContext)
    {
        // To implement multisampling, the ripple effect is run multiple times at various sampling offsets,
        // which are then blended together by the SampleMapRenderer to form the final high-quality output.
        // The # of samples is equal to the square of the quality value, so a quality value of [1,2,3,4,...,8]
        // will use [1,4,9,16,...,64] samples.

        this.sampleMapRenderer = SampleMapRenderer.Create(deviceContext, this.SourceSize);
        this.sampleMapRenderer.SetInput(this.SourceImage);
        this.sampleMapRenderer.EdgeMode = SampleMapEdgeMode.Clamp;

        Vector2Float[] sampleOffsets = EffectHelpers.GetRgssOffsets(this.quality);
        this.sampleMapRenderer.SampleMapCount = sampleOffsets.Length;

        for (int i = 0; i < sampleOffsets.Length; ++i)
        {
            ScenePositionEffect scenePosSampleMap = new ScenePositionEffect(deviceContext);

            ArithmeticConstEffect scenePosRgssSampleMap = new ArithmeticConstEffect(deviceContext);
            scenePosRgssSampleMap.Properties.Input.Set(scenePosSampleMap);
            scenePosRgssSampleMap.Properties.Operator.SetValue(ArithmeticOperator.Add);
            scenePosRgssSampleMap.Properties.Value.SetValue(new Vector4Float(sampleOffsets[i], 0, 0));

            IDeviceEffect rippleSampleMap = deviceContext.CreateEffect(this.sampleMapEffectID);
            rippleSampleMap.SetInput(0, scenePosRgssSampleMap);
            rippleSampleMap.SetValue(
                D2D1PixelShaderEffectProperty.ConstantBuffer,
                PropertyType.Blob,
                D2D1PixelShader.GetConstantBuffer(new SampleMapShader(
                    (float)this.sizePx,
                    (float)this.frequency,
                    (float)this.phase,
                    (float)this.amplitude,
                    (float)this.spread,
                    new float2(
                        (float)this.centerPoint.X,
                        (float)this.centerPoint.Y))));

            this.sampleMapRenderer.SetSampleMap(i, rippleSampleMap);
        }

        return this.sampleMapRenderer.GetOutput();
    }

    [D2DInputCount(1)]
    [D2DInputSimple(0)]
    [D2DInputDescription(0, D2D1Filter.MinLinearMagMipPoint)]
    [D2DEmbeddedBytecode(D2D1ShaderProfile.PixelShader50)]
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

            // The return value from a sample map shader is in the format (X,Y,A,*)
            // X and Y are the pixel to sample from, which is then multiplied by A (alpha)
            // The fourth component of the return value is currently discarded, but in a later build it will be possible
            // to pass that value into a post-processor (e.g. to apply the lighting value that's commented out above)
            float2 samplePos = scenePos + inputOffset;
            return new float4(samplePos, 1, 0);
        }
    }
}

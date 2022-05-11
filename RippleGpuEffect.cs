// This advanced sample shows how to implement and use a custom HLSL pixel shader.
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
        properties.Add(new Int32Property(PropertyNames.Quality, 2, 1, 3));

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

    private Guid effectID;
    private double sizePx;
    private double frequency;
    private double phase;
    private double amplitude;
    private double spread;
    private int quality;
    private Point2Double centerPoint;

    protected override void OnSetDeviceContext(IDeviceContext deviceContext)
    {
        // Register a PixelShaderEffect for this shader. The PixelShaderEffect must be registered once per shader.
        deviceContext.Factory.RegisterEffectFromBlob(D2D1PixelShaderEffect.GetRegistrationBlob(
            () => new ShaderTransformMapper(),
            out this.effectID));

        base.OnSetDeviceContext(deviceContext);
    }

    protected override IDeviceImage OnCreateOutput(IDeviceContext deviceContext)
    {
        // Produce a higher quality output by rendering at a higher resolution and then downsampling
        // Note that this is a very bruteforce way of improving rendering quality, and can be extremely
        // slow at the highest setting on large images.
        int scale;
        ScaleInterpolationMode scaleUpInterpolation;
        ScaleInterpolationMode scaleDownInterpolation;
        switch (this.quality)
        {
            case 1:
                scale = 1;
                scaleUpInterpolation = ScaleInterpolationMode.NearestNeighbor;
                scaleDownInterpolation = ScaleInterpolationMode.NearestNeighbor;
                break;

            case 2:
                scale = 2;
                scaleUpInterpolation = ScaleInterpolationMode.Linear;
                scaleDownInterpolation = ScaleInterpolationMode.Linear;
                break;

            case 3:
                scale = 4;
                scaleUpInterpolation = ScaleInterpolationMode.HighQualityCubic;
                scaleDownInterpolation = ScaleInterpolationMode.HighQualityCubic;
                break;

            default:
                throw new ArgumentException();
        }

        IDeviceImage sourceImage;
        if (scale == 1)
        {
            sourceImage = this.SourceImage;
        }
        else
        {
            ScaleEffect scaleUpEffect = new ScaleEffect(deviceContext);
            scaleUpEffect.Properties.Input.Set(this.SourceImage);
            scaleUpEffect.Properties.Scale.SetValue(new Vector2Float(scale, scale));
            scaleUpEffect.Properties.InterpolationMode.SetValue(scaleUpInterpolation);
            scaleUpEffect.Properties.BorderMode.SetValue(BorderMode.Soft);
            sourceImage = scaleUpEffect;
        }

        IDeviceEffect rippleEffect = deviceContext.CreateEffect(this.effectID);
        rippleEffect.SetInput(0, sourceImage);
        rippleEffect.SetValue(
            0, // TODO: there should be a PixelShaderEffectProperties.ConstantBuffer or something
            PropertyType.Blob,
            D2D1InteropServices.GetPixelShaderConstantBuffer(new Shader(
                (float)this.sizePx * scale,
                (float)this.frequency,
                (float)this.phase,
                (float)(this.amplitude * scale),
                (float)this.spread,
                new float2(
                    (float)this.centerPoint.X * scale,
                    (float)this.centerPoint.Y * scale))));

        if (scale == 1)
        {
            return rippleEffect;
        }
        else
        {
            ScaleEffect scaleDownEffect = new ScaleEffect(deviceContext);
            scaleDownEffect.Properties.Input.Set(rippleEffect);
            scaleDownEffect.Properties.Scale.SetValue(new Vector2Float(1.0f / scale, 1.0f / scale));
            scaleDownEffect.Properties.InterpolationMode.SetValue(scaleDownInterpolation);
            scaleDownEffect.Properties.BorderMode.SetValue(BorderMode.Soft);

            return scaleDownEffect;
        }
    }

    [D2DInputCount(1)]
    [D2DInputComplex(0)]
    [D2DRequiresScenePosition]
    [D2DEmbeddedBytecode(D2D1ShaderProfile.PixelShader50)]
    [AutoConstructor]
    private readonly partial struct Shader
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
            float2 toPixel = D2D.GetScenePosition().XY - this.center;

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
            float lighting = Hlsl.Lerp(1.0f, 1.0f + wave.X * falloff * 0.2f, Hlsl.Saturate(this.amplitude / 20.0f));

            // Resample the image based on the new coordinates.
            float4 color = D2D.SampleInputAtOffset(0, inputOffset);
            color.RGB *= lighting;

            return color;
        }
    }

    private sealed class ShaderTransformMapper
        : ID2D1TransformMapper<Shader>
    {
        private RectInt32 inputRect;

        public void MapInputsToOutput(
            in Shader shader, 
            ReadOnlySpan<Rectangle> inputs, 
            ReadOnlySpan<Rectangle> opaqueInputs, 
            out Rectangle output, 
            out Rectangle opaqueOutput)
        {
            output = inputs[0];

            // Store the inputRect so we can use it later in MapInvalidRect
            this.inputRect = inputs[0];

            // Indicate that entire output might contain transparency.
            opaqueOutput = RectInt32.Zero;
        }

        public void MapInvalidOutput(
            in Shader shader, 
            int inputIndex, 
            Rectangle invalidInput, 
            out Rectangle invalidOutput)
        {
            // Indicate that the entire output may be invalid.
            invalidOutput = this.inputRect;
        }

        public void MapOutputToInputs(
            in Shader shader, 
            in Rectangle output, 
            Span<Rectangle> inputs)
        {
            int expansion = (int)Round(shader.amplitude);

            // Expand the rect out by the amplitude of the ripple animation.
            inputs[0] = RectInt32.FromEdges(
                SafeAdd(output.Left, -expansion),
                SafeAdd(output.Top, -expansion),
                SafeAdd(output.Right, expansion),
                SafeAdd(output.Bottom, expansion));
        }

        private static float Round(float v)
        {
            return MathF.Floor(v + 0.5f);
        }

        private static int SafeAdd(int baseValue, int valueToAdd)
        {
            return (int)Math.Clamp((long)baseValue + valueToAdd, int.MinValue, int.MaxValue);
        }
    }
}

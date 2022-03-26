// Ported from Windows SDK sample: https://github.com/microsoft/Windows-universal-samples/tree/main/Samples/D2DCustomEffects/cpp/PixelShader

using PaintDotNet.Direct2D1;
using PaintDotNet.Direct2D1.Effects;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;
using PaintDotNet.Rendering;
using System;
using System.Collections.Generic;

namespace PaintDotNet.Effects.Gpu.Samples;

// This advanced sample shows how to implement and use a custom HLSL pixel shader.
// This class provides the Paint.NET-side implementation for an effect.
// RippleEffect is the Direct2D custom effect that is used as the IDeviceImage "output" for the GpuImageEffect.
// It contains a "Props" class that declares the properties that are set by the consumer (in this case,
// RippleGpuEffect), and an "Impl" class that is used internally by Direct2D. The Impl class creates the
// transform nodes (in this case, only RippleTransform) and initializes the transform graph.
// RippleTransform is the transform node inside the effect transform graph, which loads the pixel shader and
// marshals the property values into it.
// Note that it's possible for the effect "Impl" class to also implement IDrawTransform, which permits you to
// merge the RippleTransform and RippleEffect.Impl classes. Structuring things this way makes it clearer how
// to expand beyond having just 1 transform node in the graph. It also makes it possible to reuse the
// transform in other effects. You can also convert an effect to a transform node using
// IDeviceEffectContext.CreateTransformNodeFromEffect(), so it's not necessary to do things this way --
// effects can be stitched together within other effects, in other words.
//
// The Direct2D code is based on the Windows SDK sample: https://github.com/microsoft/Windows-universal-samples/tree/main/Samples/D2DCustomEffects
internal class RippleGpuEffect
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
        properties.Add(new DoubleVectorProperty(PropertyNames.Center, Pair.Create(0.0, 0.0), Pair.Create(-1.0, -1.0), Pair.Create(+1.0, +1.0)));
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

    protected override void OnSetRenderInfo(PropertyBasedEffectConfigToken newToken, RenderArgs dstArgs, RenderArgs srcArgs)
    {
        double width = this.SrcArgs.Width;
        double height = this.SrcArgs.Height;

        double size = newToken.GetProperty<DoubleProperty>(PropertyNames.Size).Value;
        this.sizePx = size * (Math.Max(width, height) / 2.0);

        this.frequency = newToken.GetProperty<DoubleProperty>(PropertyNames.Frequency).Value;
        this.phase = newToken.GetProperty<DoubleProperty>(PropertyNames.Phase).Value;
        this.amplitude = newToken.GetProperty<DoubleProperty>(PropertyNames.Amplitude).Value;
        this.spread = newToken.GetProperty<DoubleProperty>(PropertyNames.Spread).Value;

        Pair<double, double> center = newToken.GetProperty<DoubleVectorProperty>(PropertyNames.Center).Value;
        double centerX = center.First;
        double centerY = center.Second;
        this.centerPoint = new Point2Double(
            (width + (centerX * width)) / 2.0,
            (height + (centerY * height)) / 2.0);

        this.quality = newToken.GetProperty<Int32Property>(PropertyNames.Quality).Value;

        base.OnSetRenderInfo(newToken, dstArgs, srcArgs);
    }

    private double sizePx;
    private double frequency;
    private double phase;
    private double amplitude;
    private double spread;
    private int quality;
    private Point2Double centerPoint;

    protected override IDeviceImage OnCreateOutput(IDeviceContext deviceContext)
    {
        // Produce a higher quality output by rendering at a higher resolution and then downsampling
        // Note that this is a very bruteforce way of improving rendering quality, and be be extremely
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

        RippleEffect rippleEffect = new RippleEffect(deviceContext);
        rippleEffect.Properties.Input.Set(sourceImage);
        rippleEffect.Properties.Size.SetValue((float)this.sizePx * scale);
        rippleEffect.Properties.Frequency.SetValue((float)this.frequency);
        rippleEffect.Properties.Phase.SetValue((float)this.phase);
        rippleEffect.Properties.Amplitude.SetValue((float)(this.amplitude * scale));
        rippleEffect.Properties.Spread.SetValue((float)this.spread);
        rippleEffect.Properties.Center.SetValue(
            new Point2Float(
                (float)(this.centerPoint.X * scale),
                (float)(this.centerPoint.Y * scale)));

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
}

using PaintDotNet.Direct2D1;
using PaintDotNet.Direct2D1.Effects;
using PaintDotNet.Effects.Gpu;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;
using System;
using System.Collections.Generic;

namespace PaintDotNet.Effects.Samples.Gpu;

// This sample uses both the Gaussian Blur and PDN's Bokeh blur effects and composes them together.
// Using the CrossFade slider you can have all Gaussian, a mixture, or all Bokeh.
// This isn't necessarily a useful effect, but illustrates some simple effect chaining and blending.

internal sealed class GaussianBokehGpuEffect
    : PropertyBasedGpuImageEffect
{
    public GaussianBokehGpuEffect()
        : base(
            "Gaussian Bokeh (GPU Sample)",
            "PDN v5 Samples (GPU)",
            new GpuImageEffectOptions()
            {
                IsConfigurable = true
            })
    {
    }

    private enum PropertyName
    {
        Radius,
        CrossFade,
        BokehGamma,
        BokehQuality
    }

    protected override PropertyCollection OnCreatePropertyCollection()
    {
        List<Property> properties = new List<Property>();
        properties.Add(new DoubleProperty(PropertyName.Radius, 10, 0, 250));
        properties.Add(new Int32Property(PropertyName.BokehQuality, 3, 1, 6));
        properties.Add(new DoubleProperty(PropertyName.BokehGamma, 3, 0.01, 10.0));
        properties.Add(new DoubleProperty(PropertyName.CrossFade, 0.5, 0.0, 1.0));
        return new PropertyCollection(properties);
    }

    protected override InspectTokenAction OnInspectTokenChanges(PropertyBasedEffectConfigToken oldToken, PropertyBasedEffectConfigToken newToken)
    {
        return InspectTokenAction.UpdateOutput;
    }

    private GaussianBlurEffect? gaussianBlurEffect;
    private PdnBokehEffect? bokehEffect;
    private CrossFadeEffect? crossFadeEffect;

    protected override IDeviceImage OnCreateOutput(IDeviceContext deviceContext)
    {
        this.gaussianBlurEffect = new GaussianBlurEffect(deviceContext);
        this.gaussianBlurEffect.Properties.Input.Set(this.Environment.SourceImage);
        this.gaussianBlurEffect.Properties.BorderMode.SetValue(BorderMode.Hard);
        this.gaussianBlurEffect.Properties.Optimization.SetValue(GaussianBlurOptimization.Quality);

        this.bokehEffect = new PdnBokehEffect(deviceContext);
        this.bokehEffect.Properties.Input.Set(this.Environment.SourceImage);
        this.bokehEffect.Properties.EdgeMode.SetValue(PdnBokehEdgeMode.Clamp);

        this.crossFadeEffect = new CrossFadeEffect(deviceContext);
        this.crossFadeEffect.Properties.Destination.Set(this.bokehEffect);
        this.crossFadeEffect.Properties.Source.Set(this.gaussianBlurEffect);

        return this.crossFadeEffect;
    }

    protected override void OnUpdateOutput(IDeviceContext deviceContext)
    {
        double radius = this.Token.GetProperty<DoubleProperty>(PropertyName.Radius).Value;
        int bokehQuality = this.Token.GetProperty<Int32Property>(PropertyName.BokehQuality).Value;
        double bokehGamma = this.Token.GetProperty<DoubleProperty>(PropertyName.BokehGamma).Value;
        double crossFade = this.Token.GetProperty<DoubleProperty>(PropertyName.CrossFade).Value;

        this.gaussianBlurEffect!.Properties.StandardDeviation.SetValue((float)StandardDeviation.FromRadius(radius));
        this.bokehEffect!.Properties.Radius.SetValue((float)radius);
        this.bokehEffect!.Properties.GammaExponent.SetValue((float)bokehGamma);
        this.bokehEffect!.Properties.Quality.SetValue(bokehQuality);
        this.crossFadeEffect!.Properties.Weight.SetValue((float)crossFade);

        base.OnUpdateOutput(deviceContext);
    }
}

using PaintDotNet.Direct2D1;
using PaintDotNet.Direct2D1.Effects;
using PaintDotNet.Effects.Gpu;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;
using System;
using System.Collections.Generic;

namespace PaintDotNet.Effects.Samples.Gpu;

// This sample uses both the Gaussian Blur and PDN's Bokeh blur effects and composes them together.
// Using the CrossFade slider you can have all Gaussian (0), a mixture, or all Bokeh (1).
// This isn't necessarily a useful effect, but illustrates some simple effect chaining and blending,
// as well as using the ConvertGammaEffect for gamma-correct rendering.

internal sealed class GaussianBokehGpuEffect
    : PropertyBasedGpuImageEffect
{
    public GaussianBokehGpuEffect()
        : base(
            "Gaussian Bokeh",
            "PDN v5 Samples (GPU)",
            GpuImageEffectOptions.Create() with
            {
                IsConfigurable = true
            })
    {
    }

    private enum PropertyNames
    {
        Radius,
        GaussianBlurQuality,
        BokehQuality,
        CrossFade,
    }

    protected override PropertyCollection OnCreatePropertyCollection()
    {
        List<Property> properties = new List<Property>();
        properties.Add(new DoubleProperty(PropertyNames.Radius, 10, 0, 250));
        properties.Add(new Int32Property(PropertyNames.GaussianBlurQuality, 3, 1, 4));
        properties.Add(new Int32Property(PropertyNames.BokehQuality, 3, 1, 10));
        properties.Add(new DoubleProperty(PropertyNames.CrossFade, 0.5, 0.0, 1.0));
        return new PropertyCollection(properties);
    }

    protected override ControlInfo OnCreateConfigUI(PropertyCollection props)
    {
        ControlInfo configUI = CreateDefaultConfigUI(props);
        configUI.SetPropertyControlValue(PropertyNames.CrossFade, ControlInfoPropertyNames.DisplayName, "Cross-fade: Gaussian Blur (0) <--> Bokeh (1)");
        return configUI;
    }

    protected override IDeviceImage OnCreateOutput(IDeviceContext deviceContext)
    {
        double radius = this.Token.GetProperty<DoubleProperty>(PropertyNames.Radius)!.Value;
        int gaussianBlurQuality = this.Token.GetProperty<Int32Property>(PropertyNames.GaussianBlurQuality)!.Value;
        int bokehQuality = this.Token.GetProperty<Int32Property>(PropertyNames.BokehQuality)!.Value;
        double crossFade = this.Token.GetProperty<DoubleProperty>(PropertyNames.CrossFade)!.Value;

        // Using GaussianBlurEffect "2" allows us to use GaussianBlurOptimization2.HighQuality
        GaussianBlurEffect2 gaussianBlurEffect = new GaussianBlurEffect2(deviceContext);
        gaussianBlurEffect.Properties.Input.Set(this.Environment.SourceImage);
        gaussianBlurEffect.Properties.BorderMode.SetValue(BorderMode.Hard);
        // The property has a range of 1-4, but the enum values are 0-3, so we must subtract 1
        gaussianBlurEffect.Properties.Optimization.SetValue((GaussianBlurOptimization2)(gaussianBlurQuality - 1));
        gaussianBlurEffect!.Properties.StandardDeviation.SetValue((float)StandardDeviation.FromRadius(radius));

        BokehBlurEffect bokehEffect = new BokehBlurEffect(deviceContext);
        bokehEffect.Properties.Input.Set(this.Environment.SourceImage);
        bokehEffect.Properties.Radius.SetValue((float)radius);
        bokehEffect.Properties.Quality.SetValue(bokehQuality);
        bokehEffect.Properties.EdgeMode.SetValue(BokehBlurEdgeMode.Mirror);

        CrossFadeEffect crossFadeEffect = new CrossFadeEffect(deviceContext);
        crossFadeEffect.Properties.Destination.Set(gaussianBlurEffect);
        crossFadeEffect.Properties.Source.Set(bokehEffect);
        crossFadeEffect.Properties.SourceWeight.SetValue((float)crossFade);

        return crossFadeEffect;
    }
}

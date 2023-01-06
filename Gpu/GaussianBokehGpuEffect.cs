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
            new GpuImageEffectOptions()
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
        double radius = this.Token.GetProperty<DoubleProperty>(PropertyNames.Radius).Value;
        int gaussianBlurQuality = this.Token.GetProperty<Int32Property>(PropertyNames.GaussianBlurQuality).Value;
        int bokehQuality = this.Token.GetProperty<Int32Property>(PropertyNames.BokehQuality).Value;
        double crossFade = this.Token.GetProperty<DoubleProperty>(PropertyNames.CrossFade).Value;

        // The UI in Paint.NET for Gaussian Blur and Bokeh has a Gamma dropdown and Gamma Exponent slider.
        // Those are implemented with the ConvertGammaEffect, which we use here but only for sRGB/Linear
        // conversion, which ensures we do the blurring in a gamma-correct manner. All input images that
        // come into effects are sRGB, except when the image has a color profile (which is currently
        // ignored). Effect output is also treated as being sRGB, so we must convert back to it from
        // linear at the end.
        ConvertGammaEffect inputAsLinearEffect = new ConvertGammaEffect(deviceContext);
        inputAsLinearEffect.Properties.Input.Set(this.Environment.SourceImage);
        inputAsLinearEffect.Properties.Mode.SetValue(ConvertGammaMode.SrgbToLinear);

        // Using GaussianBlurEffect "2" allows us to use GaussianBlurOptimization2.HighQuality
        GaussianBlurEffect2 gaussianBlurEffect = new GaussianBlurEffect2(deviceContext);
        gaussianBlurEffect.Properties.Input.Set(inputAsLinearEffect);
        gaussianBlurEffect.Properties.BorderMode.SetValue(BorderMode.Hard);
        // The property has a range of 1-4, but the enum values are 0-3, so we must subtract 1
        gaussianBlurEffect.Properties.Optimization.SetValue((GaussianBlurOptimization2)(gaussianBlurQuality - 1));
        gaussianBlurEffect!.Properties.StandardDeviation.SetValue((float)StandardDeviation.FromRadius(radius));

        PdnBokehEffect bokehEffect = new PdnBokehEffect(deviceContext);
        bokehEffect.Properties.Input.Set(inputAsLinearEffect);
        bokehEffect.Properties.Radius.SetValue((float)radius);
        bokehEffect.Properties.Quality.SetValue(bokehQuality);

        CrossFadeEffect crossFadeEffect = new CrossFadeEffect(deviceContext);
        crossFadeEffect.Properties.Destination.Set(gaussianBlurEffect);
        crossFadeEffect.Properties.Source.Set(bokehEffect);
        crossFadeEffect.Properties.SourceWeight.SetValue((float)crossFade);

        ConvertGammaEffect outputAsSrgbEffect = new ConvertGammaEffect(deviceContext);
        outputAsSrgbEffect.Properties.Input.Set(crossFadeEffect);
        outputAsSrgbEffect.Properties.Mode.SetValue(ConvertGammaMode.LinearToSrgb);

        return outputAsSrgbEffect;
    }
}

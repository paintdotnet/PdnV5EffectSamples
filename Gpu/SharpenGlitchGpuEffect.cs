using PaintDotNet.Direct2D1;
using PaintDotNet.Direct2D1.Effects;
using PaintDotNet.Effects.Gpu;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;
using System;
using System.Collections.Generic;

namespace PaintDotNet.Effects.Samples.Gpu;

// This effect is just for fun, showing how to chain together multiple Sharpen effects
// to produce a "glitch" effect. Paint.NET's built-in Effects->Photo->Sharpen effect
// is just a wrapper around Direct2D's SharpenEffect.

internal sealed class SharpenGlitchGpuEffect
    : PropertyBasedGpuImageEffect
{
    public SharpenGlitchGpuEffect()
        : base(
            "Sharpen Glitch",
            "PDN v5 Samples (GPU)",
            GpuImageEffectOptions.Create() with
            {
                IsConfigurable = true
            })
    {
    }

    protected override void OnInitializeRenderInfo(IGpuImageEffectRenderInfo renderInfo)
    {
        // This should probably be done in linear space, but I'm wary of changing the output.
        // Switching to linear seems to reduce the apparent strength of the sharpening.
        renderInfo.ColorContext = GpuEffectColorContext.WorkingSpace;

        base.OnInitializeRenderInfo(renderInfo);
    }

    private enum PropertyNames
    {
        Amount,
        Threshold,
        Iterations
    }

    protected override PropertyCollection OnCreatePropertyCollection()
    {
        List<Property> props = new List<Property>();

        props.Add(new DoubleProperty(PropertyNames.Amount, 2, 0, 10));
        props.Add(new DoubleProperty(PropertyNames.Threshold, 0, 0, 1));
        props.Add(new Int32Property(PropertyNames.Iterations, 5, 1, 50));

        return new PropertyCollection(props);
    }

    protected override ControlInfo OnCreateConfigUI(PropertyCollection props)
    {
        ControlInfo configUI = CreateDefaultConfigUI(props);

        configUI.SetPropertyControlValue(PropertyNames.Amount, ControlInfoPropertyNames.DisplayName, "Amount");
        configUI.SetPropertyControlValue(PropertyNames.Amount, ControlInfoPropertyNames.ShowHeaderLine, false);

        configUI.SetPropertyControlValue(PropertyNames.Threshold, ControlInfoPropertyNames.DisplayName, "Threshold");
        configUI.SetPropertyControlValue(PropertyNames.Threshold, ControlInfoPropertyNames.ShowHeaderLine, false);
        configUI.SetPropertyControlValue(PropertyNames.Threshold, ControlInfoPropertyNames.UseExponentialScale, true);

        configUI.SetPropertyControlValue(PropertyNames.Iterations, ControlInfoPropertyNames.DisplayName, "Iterations");
        configUI.SetPropertyControlValue(PropertyNames.Iterations, ControlInfoPropertyNames.ShowHeaderLine, false);

        return configUI;
    }

    protected override IDeviceImage OnCreateOutput(IDeviceContext deviceContext)
    {
        double amount = this.Token.GetProperty<DoubleProperty>(PropertyNames.Amount)!.Value;
        double threshold = this.Token.GetProperty<DoubleProperty>(PropertyNames.Threshold)!.Value;
        int iterations = this.Token.GetProperty<Int32Property>(PropertyNames.Iterations)!.Value;

        SharpenEffect output = new SharpenEffect(deviceContext);
        output.Properties.Input.Set(this.Environment.SourceImage);
        output.Properties.Sharpness.SetValue((float)amount);
        output.Properties.Threshold.SetValue((float)threshold);

        for (int i = 1; i <= iterations; ++i)
        {
            SharpenEffect sharpen = new SharpenEffect(deviceContext);
            sharpen.Properties.Input.Set(output);
            sharpen.Properties.Sharpness.SetValue((float)amount);
            sharpen.Properties.Threshold.SetValue((float)threshold);

            output = sharpen;
        }

        return output;
    }
}

#nullable enable

using PaintDotNet.Direct2D1;
using PaintDotNet.Direct2D1.Effects;
using PaintDotNet.Effects.Gpu;
using PaintDotNet.PropertySystem;
using System;
using System.Collections.Generic;

namespace PaintDotNet.Effects.Samples.Gpu;

// This sample shows how to combine GaussianBlurEffect and InputSwizzleEffect to create a blur the only affects the requested channels

internal sealed class ChannelBlurEffect
    : PropertyBasedGpuImageEffect
{
    public ChannelBlurEffect()
        : base(
            "Channel Blur",
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
        Red,
        Green,
        Blue,
        Alpha
    }

    protected override PropertyCollection OnCreatePropertyCollection()
    {
        List<Property> properties = new List<Property>();
        properties.Add(new DoubleProperty(PropertyNames.Radius, 3, 0, 750));
        properties.Add(new BooleanProperty(PropertyNames.Red, true));
        properties.Add(new BooleanProperty(PropertyNames.Green, true));
        properties.Add(new BooleanProperty(PropertyNames.Blue, true));
        properties.Add(new BooleanProperty(PropertyNames.Alpha, true));
        return new PropertyCollection(properties);
    }

    private GaussianBlurEffect? blurEffect;
    private InputSwizzleEffect? swizzleEffect;

    protected override IDeviceImage OnCreateOutput(IDeviceContext deviceContext)
    {
        this.blurEffect = new GaussianBlurEffect(deviceContext);
        this.blurEffect.Properties.Input.Set(this.Environment.SourceImage);
        this.blurEffect.Properties.BorderMode.SetValue(BorderMode.Hard);
        this.blurEffect.Properties.Optimization.SetValue(GaussianBlurOptimization.Quality);

        this.swizzleEffect = new InputSwizzleEffect(deviceContext);
        this.swizzleEffect.InputCount = 2;
        this.swizzleEffect.SetInput(0, this.Environment.SourceImage);
        this.swizzleEffect.SetInput(1, this.blurEffect);

        return this.swizzleEffect;
    }

    protected override InspectTokenAction OnInspectTokenChanges(PropertyBasedEffectConfigToken oldToken, PropertyBasedEffectConfigToken newToken)
    {
        return InspectTokenAction.UpdateOutput;
    }

    protected override void OnUpdateOutput(IDeviceContext deviceContext)
    {
        double radius = this.Token.GetProperty<DoubleProperty>(PropertyNames.Radius).Value;
        bool red = this.Token.GetProperty<BooleanProperty>(PropertyNames.Red).Value;
        bool green = this.Token.GetProperty<BooleanProperty>(PropertyNames.Green).Value;
        bool blue = this.Token.GetProperty<BooleanProperty>(PropertyNames.Blue).Value;
        bool alpha = this.Token.GetProperty<BooleanProperty>(PropertyNames.Alpha).Value;

        this.blurEffect!.Properties.StandardDeviation.SetValue((float)StandardDeviation.FromRadius(radius));

        this.swizzleEffect!.Properties.RedInputIndex.SetValue(red ? 1 : 0);
        this.swizzleEffect!.Properties.GreenInputIndex.SetValue(green ? 1 : 0);
        this.swizzleEffect!.Properties.BlueInputIndex.SetValue(blue ? 1 : 0);
        this.swizzleEffect!.Properties.AlphaInputIndex.SetValue(alpha ? 1 : 0);

        base.OnUpdateOutput(deviceContext);
    }
}

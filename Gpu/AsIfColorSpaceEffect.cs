#nullable enable

using PaintDotNet.Collections;
using PaintDotNet.Direct2D1;
using PaintDotNet.Direct2D1.Effects;
using PaintDotNet.Effects.Gpu;
using PaintDotNet.Imaging;
using PaintDotNet.PropertySystem;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PaintDotNet.Effects.Samples.Gpu;

// This sample shows how to use the ColorManagementEffect and the KnownColorSpace API
// The image is "assumed" to be in the chosen color space and then transformed back to its actual color space.
// For sRGB images, which is most, this mostly increases saturation. In this case it emulates the effect of
// using a wide-gamut monitor without any color management, which many find appealing (or, at least, have
// gotten used to).

[EffectCategory(EffectCategory.Adjustment)]
internal sealed class AsIfColorSpaceEffect
    : PropertyBasedGpuImageEffect
{
    public AsIfColorSpaceEffect()
        : base(
            "As-if Color Space",
            "PDN v5 Samples (GPU)",
            GpuImageEffectOptions.Create() with
            {
                IsConfigurable = true
            })
    {
    }

    private enum PropertyNames
    {
        ColorSpace,
        Strength
    }

    protected override PropertyCollection OnCreatePropertyCollection()
    {
        List<Property> properties = new List<Property>();

        object[] colorSpaces = Enum.GetValues<KnownColorSpace>().Where(cs => cs != KnownColorSpace.ScRgb).Cast<object>().ToArray();
        properties.Add(new StaticListChoiceProperty(PropertyNames.ColorSpace, colorSpaces, colorSpaces.IndexOf(KnownColorSpace.DisplayP3)));

        properties.Add(new DoubleProperty(PropertyNames.Strength, 1, 0, 1));
        return new PropertyCollection(properties);
    }

    protected override void OnInitializeRenderInfo(IGpuImageEffectRenderInfo renderInfo)
    {
        // We work in linear gamma because blending works best that way, so this is for the benefit of CrossFadeEffect
        renderInfo.ColorContext = GpuEffectColorContext.WorkingSpaceLinear;
        base.OnInitializeRenderInfo(renderInfo);
    }

    private KnownColorSpace? chosenColorSpace;
    private IDeviceColorContext? chosenColorContext;
    private ColorManagementEffect? colorManagementEffect;
    private CrossFadeEffect? crossFadeEffect;

    protected override void OnInvalidateDeviceResources()
    {
        this.chosenColorSpace = null;
        DisposableUtil.Free(ref this.chosenColorContext);
        DisposableUtil.Free(ref this.colorManagementEffect);
        DisposableUtil.Free(ref this.crossFadeEffect);
        base.OnInvalidateDeviceResources();
    }

    protected override IDeviceImage OnCreateOutput(IDeviceContext deviceContext)
    {
        this.colorManagementEffect = new ColorManagementEffect(deviceContext);
        this.colorManagementEffect.Properties.Source.Set(this.Environment.SourceImage);
        this.colorManagementEffect.Properties.DestinationColorContext.SetValue(this.Environment.Document.ColorContext);

        this.crossFadeEffect = new CrossFadeEffect(deviceContext);
        this.crossFadeEffect.Properties.Destination.Set(this.Environment.SourceImage);
        this.crossFadeEffect.Properties.Source.Set(this.colorManagementEffect);

        return this.crossFadeEffect;
    }

    protected override InspectTokenAction OnInspectTokenChanges(PropertyBasedEffectConfigToken oldToken, PropertyBasedEffectConfigToken newToken)
    {
        return InspectTokenAction.UpdateOutput;
    }

    protected override void OnUpdateOutput(IDeviceContext deviceContext)
    {
        KnownColorSpace colorSpace = (KnownColorSpace)this.Token.GetProperty<StaticListChoiceProperty>(PropertyNames.ColorSpace)!.Value;
        double strength = this.Token.GetProperty<DoubleProperty>(PropertyNames.Strength)!.Value;

        if (this.chosenColorContext is null || colorSpace != this.chosenColorSpace)
        {
            DisposableUtil.Free(ref this.chosenColorContext);
            using IDeviceColorContext chosenColorContext = deviceContext.CreateColorContext(colorSpace);
            this.chosenColorContext = deviceContext.CreateLinearizedColorContextOrScRgb(chosenColorContext);
            this.chosenColorSpace = colorSpace;
            this.colorManagementEffect!.Properties.SourceColorContext.SetValue(this.chosenColorContext);
        }

        this.crossFadeEffect!.Properties.SourceWeight.SetValue((float)strength);

        base.OnUpdateOutput(deviceContext);
    }
}

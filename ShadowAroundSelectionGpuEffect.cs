using PaintDotNet;
using PaintDotNet.Direct2D1;
using PaintDotNet.Direct2D1.Effects;
using PaintDotNet.Effects;
using PaintDotNet.Effects.Gpu;
using PaintDotNet.Imaging;
using PaintDotNet.PropertySystem;
using PaintDotNet.Rendering;
using System;
using System.Collections.Generic;

namespace PaintDotNet.Effects.Gpu.Samples;

// A simple effect that renders a drop shadow inside the active selection using the Direct2D ShadowEffect.
// This does not require there to be any transparency. This sample shows how to retrieve the active selection
// as a Direct2D IGeometry instance.
internal sealed class ShadowAroundSelectionGpuEffect
    : PropertyBasedGpuImageEffect
{
    public ShadowAroundSelectionGpuEffect()
        : base(
            "Shadow Around Selection (GPU Sample)",
            null, // no icon
            "GPU Samples",
            new GpuImageEffectOptions()
            {
                Flags = EffectFlags.Configurable | EffectFlags.DisableSelectionClipping
            })
    {
    }

    private enum PropertyNames
    {
        InsideSelection,
        OutsideSelection,
        BlurRadius
    }

    protected override PropertyCollection OnCreatePropertyCollection()
    {
        List<Property> properties = new List<Property>();

        properties.Add(new BooleanProperty(PropertyNames.InsideSelection, true));
        properties.Add(new BooleanProperty(PropertyNames.OutsideSelection, true));
        properties.Add(new Int32Property(PropertyNames.BlurRadius, 10, 0, 200));

        return new PropertyCollection(properties);
    }

    protected override void OnSetRenderInfo(PropertyBasedEffectConfigToken newToken)
    {
        this.insideSelection = newToken.GetProperty<BooleanProperty>(PropertyNames.InsideSelection).Value;
        this.outsideSelection = newToken.GetProperty<BooleanProperty>(PropertyNames.OutsideSelection).Value;
        this.blurRadius = newToken.GetProperty<Int32Property>(PropertyNames.BlurRadius).Value;
        base.OnSetRenderInfo(newToken);
    }

    private bool insideSelection;
    private bool outsideSelection;
    private int blurRadius;

    private IDeviceImage? selectionMaskImage;

    protected override void OnInvalidateDeviceResources()
    {
        this.selectionMaskImage?.Dispose();
        this.selectionMaskImage = null;

        base.OnInvalidateDeviceResources();
    }

    protected override IDeviceImage OnCreateOutput(IDeviceContext deviceContext)
    {
        if (!this.insideSelection && !this.outsideSelection)
        {
            return this.SourceImage;
        }

        // The output image will be composted from a few different images. The base is the source image (the
        // currently selected layer from the PDN image), and then the shadow is blended on top
        CompositeEffect outputImage = new CompositeEffect(deviceContext);
        outputImage.Properties.Destination.Set(this.SourceImage);

        // Retrieve the selection geometry and create a command list that draws a stroke outline of it
        // We can use the command list as an IDeviceImage that we plug into an effect (in this case, 
        // ShadowEffect)
        // The geometry object is cached by the effect infrastructure, so it is only (potentially) expensive
        // the first time it's retreived.
        IGeometry selectionGeometry = this.EnvironmentParameters.Selection.GetGeometry(deviceContext.Factory);

        ICommandList selectionOutline = deviceContext.CreateCommandList();
        using (selectionOutline.UseBeginDraw(deviceContext))
        {
            // Note that we issue drawing commands to the deviceContext. The command list becomes
            // the device context's current "target" that receives the drawing commands.

            ISolidColorBrush brush = deviceContext.CreateSolidColorBrush(Colors.Black);
            deviceContext.DrawGeometry(selectionGeometry, brush, this.blurRadius / 2.0f);
        }

        // Create a shadow effect that will blur the image of the stroked geometry outline
        ShadowEffect shadowEffect = new ShadowEffect(deviceContext);
        shadowEffect.Properties.Input.Set(selectionOutline);
        shadowEffect.Properties.BlurStandardDeviation.SetValue(StandardDeviation.FromRadius(this.blurRadius));
        shadowEffect.Properties.Optimization.SetValue(ShadowOptimization.Quality);

        if (this.insideSelection && this.outsideSelection)
        {
            outputImage.Properties.Sources.Add(shadowEffect);
            outputImage.Properties.Mode.SetValue(CompositeMode.SourceOver);
        }
        else
        {
            // Only create this if needed, otherwise don't consume the CPU time and memory for the bitmap and image.
            // This bitmap doesn't change, and can be a bit expensive (CPU and memory), so it's important to re-use
            // it across renderings (that is, when the user changes values in the effect's UI). The mask bitmap is
            // cached by Paint.NET, so it's only expensive the first time it's used to create a device image.
            if (this.selectionMaskImage == null)
            {
                IBitmapSource<ColorAlpha8> selectionMaskBitmap = this.EnvironmentParameters.Selection.GetMaskBitmap();
                this.selectionMaskImage = deviceContext.CreateImageFromBitmap(selectionMaskBitmap);
            }

            CompositeEffect maskedShadow = new CompositeEffect(deviceContext);
            maskedShadow.Properties.Destination.Set(this.selectionMaskImage);
            maskedShadow.Properties.Sources.Add(shadowEffect);
            maskedShadow.Properties.Mode.SetValue(this.insideSelection ? CompositeMode.SourceIn : CompositeMode.SourceOut);

            outputImage.Properties.Sources.Add(maskedShadow);
        }

        return outputImage;
    }
}

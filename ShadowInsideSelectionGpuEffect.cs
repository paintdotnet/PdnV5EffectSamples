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
internal sealed class ShadowInsideSelectionGpuEffect
    : PropertyBasedGpuImageEffect
{
    public ShadowInsideSelectionGpuEffect()
        : base(
            "Shadow Inside Selection (GPU Sample)",
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
        BlurRadius
    }

    protected override PropertyCollection OnCreatePropertyCollection()
    {
        List<Property> properties = new List<Property>();

        properties.Add(new Int32Property(PropertyNames.BlurRadius, 10, 0, 200));

        return new PropertyCollection(properties);
    }

    protected override void OnSetRenderInfo(PropertyBasedEffectConfigToken newToken, RenderArgs dstArgs, RenderArgs srcArgs)
    {
        this.blurRadius = newToken.GetProperty<Int32Property>(PropertyNames.BlurRadius).Value;
        base.OnSetRenderInfo(newToken, dstArgs, srcArgs);
    }

    private int blurRadius;

    protected override IDeviceImage OnCreateOutput(IDeviceContext deviceContext)
    {
        IGeometry selectionGeometry = this.EnvironmentParameters.GetSelectionAsGeometry(deviceContext.Factory);

        // Create a command list that fills with black and punches a transparent "hole" where the active selection is
        // Because images (which a command list is) have infinite extent, this works even at the edge of the image;
        // the black fill extends to infinity.
        using IDeviceContext deviceContextCL = deviceContext.Device.CreateDeviceContext();
        ICommandList commandList = deviceContextCL.CreateCommandList();
        deviceContextCL.SetTarget(commandList);
        using (deviceContextCL.UseBeginDraw())
        {
            deviceContextCL.Clear(Colors.Black);
            deviceContextCL.PrimitiveBlend = PrimitiveBlend.Copy;
            ISolidColorBrush fillBrush = deviceContextCL.CreateSolidColorBrush(ColorRgba128Float.TransparentBlack);
            deviceContextCL.FillGeometry(selectionGeometry, fillBrush);
        }
        commandList.Close();

        // Set up a simple transform graph.
        // The commandList is plugged into ShadowEffect, which will rendered the shadow.
        // Then, CompositeEffect, which is used as the output, is used to blend the ShadowEffect on top of the SourceImage.

        ShadowEffect shadowEffect = new ShadowEffect(deviceContext);
        shadowEffect.Properties.Input.Set(commandList);
        shadowEffect.Properties.Optimization.SetValue(ShadowOptimization.Quality);
        shadowEffect.Properties.BlurStandardDeviation.SetValue(StandardDeviation.FromRadius(this.blurRadius));
        
        CompositeEffect compositeEffect = new CompositeEffect(deviceContext);
        compositeEffect.Properties.Destination.Set(this.SourceImage);
        compositeEffect.Properties.Source[0].Set(shadowEffect);
        compositeEffect.Properties.Mode.SetValue(CompositeMode.SourceOver);

        return compositeEffect;
    }
}

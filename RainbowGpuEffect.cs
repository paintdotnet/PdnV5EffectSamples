using PaintDotNet;
using PaintDotNet.Direct2D1;
using PaintDotNet.Effects;
using PaintDotNet.Effects.Gpu;
using PaintDotNet.Imaging;
using PaintDotNet.PropertySystem;
using PaintDotNet.Rendering;
using System;
using System.Collections.Generic;

namespace PaintDotNet.Effects.Gpu.Samples;

// This is a very simple effect that just draws a rainbow using a linear gradient brush
// There are no configuration parameters so we just specify the base EffectConfigToken
internal sealed class RainbowGpuEffect
    : PropertyBasedGpuDrawingEffect
{
    public RainbowGpuEffect()
        : base(
            "Rainbow (GPU Sample)",
            null, // no icon
            "GPU Samples",
            new GpuDrawingEffectOptions()
            {
                Flags = EffectFlags.Configurable
            })
    {
    }

    private enum PropertyNames
    {
        HueOffset
    }

    protected override PropertyCollection OnCreatePropertyCollection()
    {
        List<Property> properties = new List<Property>();

        properties.Add(new Int32Property(PropertyNames.HueOffset, 0, 0, 360));

        return new PropertyCollection(properties);
    }

    protected override void OnSetRenderInfo(PropertyBasedEffectConfigToken newToken, RenderArgs dstArgs, RenderArgs srcArgs)
    {
        this.hueOffset = newToken.GetProperty<Int32Property>(PropertyNames.HueOffset).Value;
        base.OnSetRenderInfo(newToken, dstArgs, srcArgs);
    }

    private int hueOffset;

    protected override void OnDraw(IDeviceContext dc)
    {
        SizeInt32 size = this.EnvironmentParameters.SourceSurface.Size;

        // D2D1_GRADIENT_STOP: https://docs.microsoft.com/en-us/windows/win32/api/d2d1/ns-d2d1-d2d1_gradient_stop
        GradientStopFloat[] gradientStops = new GradientStopFloat[361];
        for (int i = 0; i < gradientStops.Length; ++i)
        {
            int hue = ((i + 360 - this.hueOffset) % 360);

            // ColorHsv96Float is a Paint.NET primitive. It can be converted to an ColorRgb96Float
            // with the ToRgb() method, and then cast to ColorRgba128Float.
            ColorHsv96Float hsv = new ColorHsv96Float(hue, 100, 100);
            gradientStops[hue] = new GradientStopFloat((float)i / 360.0f, (ColorRgba128Float)hsv.ToRgb());
        }

        // ID2D1GradientStopCollection: https://docs.microsoft.com/en-us/windows/win32/api/d2d1/nn-d2d1-id2d1gradientstopcollection
        // ID2D1RenderTarget::CreateGradientStopCollection(): https://docs.microsoft.com/en-us/windows/win32/api/d2d1/nf-d2d1-id2d1rendertarget-creategradientstopcollection(constd2d1_gradient_stop_uint32_d2d1_gamma_d2d1_extend_mode_id2d1gradientstopcollection)
        IGradientStopCollection gradientStopCollection = dc.CreateGradientStopCollection(gradientStops);

        // ID2D1LinearGradientBrush: https://docs.microsoft.com/en-us/windows/win32/api/d2d1/nn-d2d1-id2d1lineargradientbrush
        // ID2D1RenderTarget::CreateLinearGradientBrush(): https://docs.microsoft.com/en-us/windows/win32/api/d2d1/nf-d2d1-id2d1rendertarget-createlineargradientbrush(constd2d1_linear_gradient_brush_properties__constd2d1_brush_properties__id2d1gradientstopcollection_id2d1lineargradientbrush)
        ILinearGradientBrush gradientBrush = dc.CreateLinearGradientBrush(
            new LinearGradientBrushProperties(
                new Point2Float(0, 0),
                new Point2Float(size.Width, size.Height)),
            null,
            gradientStopCollection);

        // ID2D1RenderTarget::FillRectangle(): https://docs.microsoft.com/en-us/windows/win32/api/d2d1/nf-d2d1-id2d1rendertarget-fillrectangle(constd2d1_rect_f_id2d1brush)
        dc.FillRectangle(
            new RectFloat(0, 0, size.Width, size.Height),
            gradientBrush);
    }

    protected override void OnInvalidateDeviceResources()
    {
        // In this sample we're not worrying about calling Dispose() on the resources (IGradientStopCollection
        // and ILinearGradientBrush) created and used in OnDraw(), for the sake of simplicity and brevity. The
        // garbage collector in .NET will clean these up. This is fine for simple effects that don't create a
        // lot of resources. However, for effects that do a lot of work, or use large bitmaps, or effects/images
        // with caching enabled, it can be important to store resources in fields and then implement this method
        // to call Dispose() on each one.
        // My general advice on this: unless you're already very comfortable with the IDisposable pattern, just
        // get your effect working first without worrying about this.

        base.OnInvalidateDeviceResources();
    }
}
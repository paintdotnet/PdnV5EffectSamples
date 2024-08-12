using PaintDotNet.Imaging;
using PaintDotNet.PropertySystem;
using PaintDotNet.Rendering;
using System;
using System.Collections.Generic;

namespace PaintDotNet.Effects.Samples.Bitmap;

// This is similar to SquareBlurGpuEffect, but implemented with CPU rendering via BitmapEffect.
// This shows how to request the source image in Prgba128Float format and to produce output that
// is also in Prgba128Float format. This accomplishes two things: 1) it converts the source image
// to premultiplied alpha, which greatly simplifies the calculations we do here, and 2) it converts
// the image to linear gamma, which greatly improves the quality of the result. The effect's
// output will be automatically converted back to the format used by the image for storage (e.g.
// currently always Bgra32, but will be expanded later).
//
// BitmapEffects that work with floating-point formats will automatically use linear gamma. The
// input bitmaps will be linearized, and the output is assumed to be linearized. This covers
// Rgba128Float, Prgba128Float, Rgba64Half, and Prgba64Half.
//
// BitmapEffects that work with integer formats will have neither of these conversions done, and
// will operate much like you'd expect for "classic" effect plugins. This covers Bgra32, Pbgra32,
// Rgba64, Prgba64, etc.
//
// Note that BGRA ordering is the convention for 32-bit pixel formats, while RGBA ordering is
// the convention for everything else. You can use RGBA ordering if you want to, just specify the
// appropriate pixel format.
//
// Note that this sample is not optimized for performance. Not only does it not use SIMD, but the
// blur algorithm is not implemented as a separated kernel (that is, separate rendering passes for
// horizontal and vertical blurring).

internal sealed class SquareBlurBitmapEffect
    : PropertyBasedBitmapEffect
{
    private IBitmapSource<ColorPrgba128Float>? sourceBitmap;
    private int radius;

    public SquareBlurBitmapEffect()
        : base(
            "Square Blur",
            "PDN v5 Samples",
            BitmapEffectOptions.Create() with
            {
                IsConfigurable = true
            })
    {
    }

    private enum PropertyNames
    {
        Radius
    }

    protected override PropertyCollection OnCreatePropertyCollection()
    {
        List<Property> properties = new List<Property>();
        properties.Add(new Int32Property(PropertyNames.Radius, 6, 0, 50));
        return new PropertyCollection(properties);
    }

    protected override void OnInitializeRenderInfo(IBitmapEffectRenderInfo renderInfo)
    {
        renderInfo.OutputPixelFormat = PixelFormats.Prgba128Float;
        this.sourceBitmap = this.Environment.GetSourceBitmap<ColorPrgba128Float>();
        base.OnInitializeRenderInfo(renderInfo);
    }

    protected override void OnSetToken(PropertyBasedEffectConfigToken? newToken)
    {
        this.radius = newToken!.GetProperty<Int32Property>(PropertyNames.Radius)!.Value;
        base.OnSetToken(newToken);
    }

    protected override void OnRender(IBitmapEffectOutput output)
    {
        using IBitmapLock<ColorPrgba128Float> outputLock = output.Lock<ColorPrgba128Float>();
        RegionPtr<ColorPrgba128Float> outputRegion = outputLock.AsRegionPtr();

        int radius = this.radius;
        if (radius == 0)
        {
            this.sourceBitmap!.CopyPixels(outputLock, output.Bounds.Location);
            return;
        }

        int sampleCount = (1 + 2 * radius) * (1 + 2 * radius);

        // Retrieve the region we need from the source bitmap. We clip the bitmap with the Clamp extend mode
        // so that we can access pixels "outside" the source without having a lot of messy bounds checking code
        RectInt32 sourceRect = RectInt32.Inflate(output.Bounds, this.radius, this.radius);
        using IBitmap<ColorPrgba128Float> sourceTile = this.sourceBitmap!
            .CreateClipper(sourceRect, BitmapExtendMode.Clamp)
            .ToBitmap();

        if (this.IsCancelRequested)
        {
            return;
        }

        using IBitmapLock<ColorPrgba128Float> sourceTileLock = sourceTile.Lock(BitmapLockOptions.Read);

        // Get an offset view of the source so that x,y values are aligned with outputRegion
        var sourceRegion = sourceTileLock
            .AsRegionPtr()
            .OffsetView(this.radius, this.radius);

        for (int outputDY = 0; outputDY < outputRegion.Height; ++outputDY)
        {
            if (this.IsCancelRequested)
            {
                return;
            }

            for (int outputDX = 0; outputDX < outputRegion.Width; ++outputDX)
            {
                Vector4Float samples = default;

                for (int sourceY = outputDY - radius; sourceY <= outputDY + radius; ++sourceY)
                {
                    for (int sourceX = outputDX - radius; sourceX <= outputDX + radius; ++sourceX)
                    {
                        ColorPrgba128Float sample = sourceRegion[sourceX, sourceY];
                        samples += (Vector4Float)sample;
                    }
                }

                ColorPrgba128Float outputColor = (ColorPrgba128Float)(samples / sampleCount);
                outputRegion[outputDX, outputDY] = outputColor;
            }
        }
    }
}

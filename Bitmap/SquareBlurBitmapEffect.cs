using PaintDotNet.Imaging;
using PaintDotNet.PropertySystem;
using PaintDotNet.Rendering;
using System;
using System.Collections.Generic;

namespace PaintDotNet.Effects.Samples.Bitmap;

// This is similar to SquareBlurGpuEffect, but implemented with CPU rendering via BitmapEffect
// Note that this sample is not optimized for performance.

internal sealed class SquareBlurBitmapEffect
    : PropertyBasedBitmapEffect
{
    private IBitmapSource<ColorBgra32>? sourceBitmap;
    private int radius;

    public SquareBlurBitmapEffect()
        : base(
            "Square Blur (Sample)",
            "PDN v5 Samples",
            new BitmapEffectOptions()
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
        this.sourceBitmap = this.Environment.GetSourceBitmapBgra32();
        base.OnInitializeRenderInfo(renderInfo);
    }

    protected override void OnSetToken(PropertyBasedEffectConfigToken? newToken)
    {
        this.radius = newToken!.GetProperty<Int32Property>(PropertyNames.Radius).Value;
        base.OnSetToken(newToken);
    }

    protected override void OnRender(IBitmapEffectOutput output)
    {
        using IBitmapLock<ColorBgra32> outputLock = output.LockBgra32();
        RegionPtr<ColorBgra32> outputRegion = outputLock.AsRegionPtr();

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
        using IBitmap<ColorBgra32> sourceTile = this.sourceBitmap!
            .CreateClipper(sourceRect, BitmapExtendMode.Clamp)
            .ToBitmap();

        if (this.IsCancelRequested)
        {
            return;
        }

        using IBitmapLock<ColorBgra32> sourceTileLock = sourceTile.Lock(BitmapLockOptions.Read);

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
                        ColorBgra32 sample = sourceRegion[sourceX, sourceY];
                        samples += (Vector4Float)(((ColorRgba128Float)sample).ToPremultiplied());
                    }
                }

                ColorPrgba128Float outputPrgba128F = ((ColorPrgba128Float)(samples / sampleCount));
                ColorRgba128Float outputRgba128F = (ColorRgba128Float)outputPrgba128F;
                ColorBgra32 outputBgra32 = ColorBgra32.Round(outputRgba128F);
                outputRegion[outputDX, outputDY] = outputBgra32;
            }
        }
    }
}

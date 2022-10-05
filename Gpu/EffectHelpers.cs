using PaintDotNet.Rendering;
using System;

namespace PaintDotNet.Effects.Samples.Gpu;

internal class EffectHelpers
{
    // These methods return offsets for rotated grid supersampling, as used in the old 3dfx Voodoo graphics cards

    public static int GetRgssOffsetsCount(int quality)
    {
        return quality * quality;
    }

    public static Vector2Float GetRgssOffset(int quality, int index)
    {
        if (quality == 1 && index == 0)
        {
            return default;
        }

        int count = quality * quality;

        float y = (index + 1.0f) / (count + 1.0f);
        float x = y * quality;
        x -= (int)x;

        return new Vector2Float(x - 0.5f, y - 0.5f);
    }

    public static Vector2Float[] GetRgssOffsets(int quality)
    {
        int sampleCount = quality * quality;
        Vector2Float[] offsets = new Vector2Float[sampleCount];
        GetRgssOffsets(offsets, quality);
        return offsets;
    }

    public static void GetRgssOffsets(Span<Vector2Float> offsets, int quality)
    {
        if (offsets.Length < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(offsets), $"{nameof(offsets)} must not be empty");
        }

        if (offsets.Length != quality * quality)
        {
            throw new ArgumentOutOfRangeException($"{nameof(offsets)}.{nameof(offsets.Length)} must equal ({nameof(quality)} * {nameof(quality)})");
        }

        if (offsets.Length == 1)
        {
            offsets[0] = default;
        }
        else
        {
            for (int i = 0; i < offsets.Length; ++i)
            {
                offsets[i] = GetRgssOffset(quality, i);
            }
        }
    }
}

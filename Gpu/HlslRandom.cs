using ComputeSharp;
using System;

namespace PaintDotNet.Effects.Samples.Gpu;

internal static class HlslRandom
{
    public static uint PcgInitializedSeed(uint instanceSeed, float2 scenePos)
    {
        return CombineHashCodes(
            instanceSeed,
            CombineHashCodes(
                PcgHash(Hlsl.AsUInt(scenePos.X)),
                PcgHash(Hlsl.AsUInt(scenePos.Y))));
    }

    // https://www.reedbeta.com/blog/hash-functions-for-gpu-rendering/
    public static uint PcgHash(uint input)
    {
        uint state = input * 747796405u + 2891336453u;
        uint word = ((state >> (int)((state >> 28) + 4)) ^ state) * 277803737u;
        return (word >> 22) ^ word;
    }

    public static float PcgNextFloat(ref uint seed)
    {
        seed = PcgHash(seed);
        return (seed >> 8) * 5.96046448E-08f; // derived from how .NET's built-in RNG is implemented
    }

    private static uint CombineHashCodes(uint hash1, uint hash2)
    {
        uint result = hash1;
        result = ((result << 5) + result) ^ hash2;
        return result;
    }
}

using ComputeSharp;
using ComputeSharp.D2D1;
using ComputeSharp.D2D1.Interop;
using PaintDotNet.Direct2D1;
using PaintDotNet.Direct2D1.Effects;
using PaintDotNet.Rendering;
using System;
using System.Runtime.CompilerServices;

namespace PaintDotNet.Effects.Gpu.Samples;

internal sealed partial class RippleTransform
    : DrawTransform
{
    [D2DInputCount(1)]
    [D2DInputComplex(0)]
    [D2DRequiresScenePosition]
    [D2DEmbeddedBytecode(D2D1ShaderProfile.PixelShader40)]
    [AutoConstructor]
    private readonly partial struct Shader
        : ID2D1PixelShader
    {
        public struct Constants
        {
            public float size;
            public float frequency;
            public float phase;
            public float amplitude;
            public float spread;
            public float2 center;
        }

        private readonly Constants constants;

        public float4 Execute()
        {
            float2 toPixel = D2D.GetScenePosition().XY - this.constants.center;

            // Scale distance such that the ripple's displacement decays to 0 at the requested size (in pixels)
            float distance = Hlsl.Length(toPixel * (1.0f / this.constants.size));
            float2 direction = Hlsl.Normalize(toPixel);

            float2 wave = default;
            Hlsl.SinCos(this.constants.frequency * distance + this.constants.phase, out wave.X, out wave.Y);

            // Clamp the falloff value so that the ripple does not extend beyond the limit.
            // Spread controls how quickly the ripple decays, up to the limit.
            float falloff = Hlsl.Saturate(1.0f - distance);
            falloff = Hlsl.Pow(falloff, 1.0f / this.constants.spread);

            // Calculate new mapping coordinates based on the frequency, center, and amplitude.
            float2 inputOffset = (wave.X * falloff * this.constants.amplitude) * direction;
            float lighting = Hlsl.Lerp(1.0f, 1.0f + wave.X * falloff * 0.2f, Hlsl.Saturate(this.constants.amplitude / 20.0f));

            // Resample the image based on the new coordinates.
            float4 color = D2D.SampleInputAtOffset(0, inputOffset);
            color.RGB *= lighting;

            return color;
        }
    }

    private Shader.Constants constants;
    private RectInt32 inputRect;

    public RippleTransform(IDeviceEffectContext effectContext)
        : base(effectContext)
    {
        effectContext.LoadPixelShader(
            typeof(Shader).GUID,
            D2D1InteropServices.LoadShaderBytecode<Shader>());
    }

    public override int InputCount => 1;

    public float Size
    {
        get => this.constants.size;

        set
        {
            this.constants.size = value;
            UpdateConstants();
        }
    }

    public float Frequency
    {
        get => this.constants.frequency;

        set
        {
            this.constants.frequency = Math.Clamp(value, 0.0f, 1000.0f);
            UpdateConstants();
        }
    }

    public float Phase
    {
        get => this.constants.phase;

        set
        {
            this.constants.phase = Math.Clamp(value, -100.0f, +100.0f);
            UpdateConstants();
        }
    }

    public float Amplitude
    {
        get => this.constants.amplitude;

        set
        {
            this.constants.amplitude = Math.Clamp(value, 0.0001f, 1000.0f);
            UpdateConstants();
        }
    }

    public float Spread
    {
        get => this.constants.spread;

        set
        {
            this.constants.spread = value;
            UpdateConstants();
        }
    }

    public Point2Float Center
    {
        get => Unsafe.As<float2, Point2Float>(ref this.constants.center);

        set
        {
            this.constants.center = Unsafe.As<Point2Float, float2>(ref value);
            UpdateConstants();
        }
    }

    protected override void OnSetDrawInfo()
    {
        this.DrawInfo.SetPixelShader(typeof(Shader).GUID, PixelOptions.None);

        // If this method is not used, nearest neighbor ("point") sampling is used. We want
        // higher quality sampling to produce a nicer looking output.
        this.DrawInfo.SetInputDescription(0, Filter.MinMagMipLinear, 0);

        base.OnSetDrawInfo();
    }

    private void UpdateConstants()
    {
        this.DrawInfo.SetPixelShaderConstantBuffer(
            D2D1InteropServices.GetPixelShaderConstantBufferForD2D1DrawInfo(new Shader(this.constants)));
    }

    public override void MapInputRectsToOutputRect(
        ReadOnlySpan<RectInt32> inputRects,
        ReadOnlySpan<RectInt32> inputOpaqueSubRects,
        out RectInt32 outputRect,
        out RectInt32 outputOpaqueSubRect)
    {
        // This effect has exactly one input, so if there is more than one input rect,
        // something is wrong.
        if (inputRects.Length != 1)
        {
            throw new ArgumentException();
        }

        outputRect = inputRects[0];

        // Store the inputRect so we can use it later in MapInvalidRect
        this.inputRect = inputRects[0];

        // Indicate that entire output might contain transparency.
        outputOpaqueSubRect = RectInt32.Zero;
    }
    
    // Calculates the mapping between the output and input rects. In this case,
    // we want to request an expanded region to account for pixels that the ripple
    // may need outside of the bounds of the destination.
    public override void MapOutputRectToInputRects(RectInt32 outputRect, Span<RectInt32> inputRects)
    {
        // This effect has exactly one input, so if there is more than one input rect,
        // something is wrong.
        if (inputRects.Length != 1)
        {
            throw new ArgumentException();
        }

        int expansion = (int)Round(this.constants.amplitude);

        // Expand the rect out by the amplitude of the ripple animation.
        inputRects[0] = RectInt32.FromEdges(
            SafeAdd(outputRect.Left, -expansion),
            SafeAdd(outputRect.Top, -expansion),
            SafeAdd(outputRect.Right, expansion),
            SafeAdd(outputRect.Bottom, expansion));
    }

    public override void MapInvalidRect(int inputIndex, RectInt32 invalidInputRect, out RectInt32 invalidOutputRect)
    {
        // Indicate that the entire output may be invalid.
        invalidOutputRect = this.inputRect;
    }

    private static float Round(float v)
    {
        return MathF.Floor(v + 0.5f);
    }

    private static int SafeAdd(int baseValue, int valueToAdd)
    {
        return (int)Math.Clamp((long)baseValue + valueToAdd, int.MinValue, int.MaxValue);
    }
}

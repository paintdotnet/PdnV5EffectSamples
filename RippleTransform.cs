using PaintDotNet.Direct2D1;
using PaintDotNet.Direct2D1.Effects;
using PaintDotNet.Rendering;
using System;

namespace PaintDotNet.Effects.Gpu.Samples;

// Implements a Direct2D draw transform that executes the ripple shader
internal sealed class RippleTransform
    : DrawTransform
{
    // This derives from a special class that handles loading an embedded resource with the same name as
    // this class, with the appropriate file extension appended. See doc comments for the base classes
    // for more info.
    // The RippleTransform.Shader.ps_4_0.cso file is the resource, which is compiled during the .csproj's
    // pre-build event using buildD2DHLSL.cmd. The output files are the .fxlib and .cso files. The .fxlib
    // is not needed. The HLSL is the file you'd actually edit to modify the shader.
    // You can derive from the PaintDotNet.Direct2D1.Shader class if you want to load your shader in a 
    // different manner.
    internal sealed class Shader
        : AutoResourcePixelShader_4_0
    {
    }

    private readonly RippleTransform.Shader shader = new Shader();

    private struct Constants
    {
        public float size;
        public float frequency;
        public float phase;
        public float amplitude;
        public float spread;
        public Point2Float center;
    }

    private Constants constants;
    private RectInt32 inputRect;

    public RippleTransform(IDeviceEffectContext effectContext)
        : base(effectContext)
    {
        effectContext.LoadPixelShader(this.shader);
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
        get => this.constants.center;

        set
        {
            this.constants.center = value;
            UpdateConstants();
        }
    }

    protected override void OnSetDrawInfo()
    {
        this.DrawInfo.SetPixelShader(this.shader);

        // If this method is not used, nearest neighbor ("point") sampling is used. We want
        // higher quality sampling to produce a nicer looking output.
        this.DrawInfo.SetInputDescription(0, Filter.MinMagMipLinear, 0);

        base.OnSetDrawInfo();
    }

    private void UpdateConstants()
    {
        this.DrawInfo.SetPixelShaderConstantBuffer(this.constants);
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

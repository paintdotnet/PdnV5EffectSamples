using ComputeSharp;
using ComputeSharp.D2D1;
using ComputeSharp.D2D1.Interop;
using PaintDotNet.Direct2D1;
using PaintDotNet.Effects.Gpu;
using PaintDotNet.PropertySystem;
using PaintDotNet.Rendering;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace PaintDotNet.Effects.Samples.Gpu;

// This sample shows how to use a pixel shader with a "complex" input.
// Complex inputs allow reading from pixels that are not at the same position as the 
// output pixel (the "scene position").
// This effect could be implemented using ConvolveMatrixEffect, but we're going to do
// it ourselves for the purpose of illustration.

internal sealed partial class SquareBlurGpuEffect
    : PropertyBasedGpuImageEffect
{
    public SquareBlurGpuEffect()
        : base(
            "Square Blur (GPU Sample)",
            "PDN v5 Samples (GPU)",
            new GpuImageEffectOptions()
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

    protected override IDeviceImage OnCreateOutput(Direct2D1.IDeviceContext deviceContext)
    {
        int radius = this.Token.GetProperty<Int32Property>(PropertyNames.Radius).Value;
        if (radius == 0)
        {
            return this.Environment.SourceImage;
        }

        deviceContext.Factory.RegisterEffectFromBlob(
            D2D1PixelShaderEffect.GetRegistrationBlob<SquareBlurShader, SquareBlurTransformMapper>(out Guid effectID));

        IDeviceEffect shaderEffect = deviceContext.CreateEffect(effectID);

        shaderEffect.SetInput(0, this.Environment.SourceImage);

        shaderEffect.SetValue(
            D2D1PixelShaderEffectProperty.ConstantBuffer,
            new SquareBlurShader(
                radius,
                (float)(1.0 / (radius * radius))));

        return shaderEffect;
    }

    [D2DInputCount(1)]
    [D2DInputComplex(0)]
    [D2DInputDescription(0, D2D1Filter.MinMagMipPoint)]
    [D2DRequiresScenePosition]
    [AutoConstructor]
    private readonly partial struct SquareBlurShader
        : ID2D1PixelShader
    {
        // 'public' so that the transform mapper can read it
        public readonly int radius;

        // When dividing by a value that is computed from another constants (in this case, radius squared),
        // it's a good idea to precompute its reciprocal so that multiplication can be used instead of
        // the more expensive division.
        private readonly float oneOverRadiusSquared;

        public float4 Execute()
        {
            float2 scenePos = D2D.GetScenePosition().XY;

            float4 samples = float4.Zero;
            for (int y = -this.radius; y <= +this.radius; ++y)
            {
                for (int x = -this.radius; x <= +this.radius; ++x)
                {
                    float2 offset = new float2(x, y);
                    float2 samplePos = scenePos + offset;
                    float4 color = D2D.SampleInputAtPosition(0, samplePos);
                    samples += color;
                }
            }

            return samples * this.oneOverRadiusSquared;
        }
    }

    private sealed class SquareBlurTransformMapper
        : ID2D1TransformMapper<SquareBlurShader>
    {
        private SquareBlurShader shader;

        // This method is called once each time the shader is rendered. We can store the shader and
        // access it later during the other methods.
        public void MapInputsToOutput(
            in SquareBlurShader shader, 
            ReadOnlySpan<Rectangle> inputs, 
            ReadOnlySpan<Rectangle> opaqueInputs, 
            out Rectangle output, 
            out Rectangle opaqueOutput)
        {
            // Store this so we can access it later. 
            this.shader = shader;

            // We want to produce an image that is larger than the input by 'radius' pixels.
            // In Paint.NET, the output area is always equal to the total input area of the SourceImage,
            // so we could also just say `output = inputs[0];`
            MapInvalidOutput(0, inputs[0], out output);

            // We don't know/care about our "opaque" output region. Specifying this is optional and
            // just lets Direct2D do some optimizations in certain cases that are not a concern for us.
            // 'default' will produce a Rectangle at x=0,y=0,width=0,height=0, which Direct2D
            // interprets as there being no information about the opaque region.
            opaqueOutput = default;
        }

        // This method is very important and defines the readable input area for a given output area.
        // This method is called _at least_ once each time the shader is rendered. We cannot modify our
        // state in this method. It's very important that this method is "functional" (as in functional
        // programming) in nature.
        // A shader can still try to read from outside the specified area, and no error/exception will
        // occur, but but the result is undefined and will either be zero (transparent black), or some
        // random color.
        public void MapOutputToInputs(in Rectangle output, Span<Rectangle> inputs)
        {
            // We need to read pixels from an area of +radius pixels around the output area
            inputs[0] = InflateHelper(output, this.shader.radius, this.shader.radius);
        }
        
        // This method is called _at least_ once each time the shader is rendered. We cannot modify our
        // state in this method. It's very important that this method is "functional" (as in functional
        // programming) in nature.
        public void MapInvalidOutput(int inputIndex, Rectangle invalidInput, out Rectangle invalidOutput)
        {
            // NOTE: If the input image is "infinite", which will be the case when supplying (for example)
            // a Flood effect as the input, or an image with a Border[Effect] applied to it, then this
            // will overflow. We can use RectInt64 to work around that. (TODO: show how!)
            invalidOutput = InflateHelper(invalidInput, shader.radius, shader.radius);
        }

        // This is a helper method that avoid overflow in the case where the rectangle is "logically infinite"
        // It's actually easy to have an "infinite" rect. Using FloodEffect will produce an image with an
        // infinite output rect, as will using BorderEffect. You can use CropEffect as another way to
        // constrain those output rects.
        private static Rectangle InflateHelper(Rectangle rect, int dx, int dy)
        {
            // First, do calculations at 64-bit to avoid overflow
            long left = rect.Left - dx;
            long top = rect.Top - dy;
            long right = rect.Right + dx;
            long bottom = rect.Bottom + dy;

            // Create a 64-bit rectangle
            RectInt64 result64 = RectInt64.FromEdges(left, top, right, bottom);

            // Clamp (intersect) the rectangle to the 32-bit "logically infinite" area, then cast do a 32-bit rectangle
            RectInt32 result = (RectInt32)RectInt64.Intersect(result64, RectInt32.LogicallyInfinite);

            return result;
        }
    }
}

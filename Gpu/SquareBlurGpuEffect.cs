﻿using ComputeSharp;
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
            "Square Blur",
            "PDN v5.1 Samples (GPU)",
            GpuImageEffectOptions.Create() with
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
        int radius = this.Token.GetProperty<Int32Property>(PropertyNames.Radius)!.Value;
        if (radius == 0)
        {
            return this.Environment.SourceImage;
        }

        deviceContext.Factory.RegisterEffectFromBlob(
            D2D1PixelShaderEffect.GetRegistrationBlob<SquareBlurShader>(out Guid effectID));

        IDeviceEffect shaderEffect = deviceContext.CreateEffect(effectID);

        shaderEffect.SetInput(0, this.Environment.SourceImage);

        // Note that it's very important to use D2D1PixelShader.GetConstantBuffer() when setting the ConstantBuffer.
        // SetValue() can be called with the Shader directly, which will use the overload that takes an `in T where T : unmanaged`,
        // but there's no guarantee that the layout of the pixel shader's constant buffer will match the layout of the C# struct.
        // Direct3D/HLSL and C# do not have the same rules for packing and alignment.
        shaderEffect.SetValue(
            D2D1PixelShaderEffectProperty.ConstantBuffer,
            D2D1PixelShader.GetConstantBuffer(
                new SquareBlurShader(
                    radius,
                    (float)(1.0 / (radius * radius)))));

        return shaderEffect;
    }

    [D2DInputCount(1)]
    [D2DInputComplex(0)]
    [D2DInputDescription(0, D2D1Filter.MinMagMipPoint)]
    [D2DRequiresScenePosition]
    [D2DShaderProfile(D2D1ShaderProfile.PixelShader50)]
    [D2DGeneratedPixelShaderDescriptor]
    [AutoConstructor]
    internal readonly partial struct SquareBlurShader
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
        : D2D1DrawTransformMapper<SquareBlurShader>
    {
        private SquareBlurShader shader;

        // This method is called once each time the shader is rendered. We can store the shader and
        // access it later during the other methods.
        public override void MapInputsToOutput(
            D2D1DrawInfoUpdateContext<SquareBlurShader> drawInfoUpdateContext, 
            ReadOnlySpan<Rectangle> inputs, 
            ReadOnlySpan<Rectangle> opaqueInputs, 
            out Rectangle output, 
            out Rectangle opaqueOutput)
        {
            SquareBlurShader shader = drawInfoUpdateContext.GetConstantBuffer();

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
        // occur, but the result is undefined and will either be zero (transparent black), or some
        // random color.
        public override void MapOutputToInputs(in Rectangle output, Span<Rectangle> inputs)
        {
            // We need to read pixels from an area of +radius pixels around the output area
            inputs[0] = InflateHelper(output, this.shader.radius, this.shader.radius);
        }
        
        // This method is called _at least_ once each time the shader is rendered. We cannot modify our
        // state in this method. It's very important that this method is "functional" (as in functional
        // programming) in nature.
        public override void MapInvalidOutput(int inputIndex, in Rectangle invalidInput, out Rectangle invalidOutput)
        {
            // NOTE: If the input image is "infinite", which will be the case when supplying (for example)
            // a Flood effect as the input, or an image with a Border[Effect] applied to it, then this
            // will overflow. We use a helper method that employs RectInt64 to resolve that.
            invalidOutput = InflateHelper(invalidInput, shader.radius, shader.radius);
        }

        // This is a helper method that avoids overflow in the case where the rectangle is "logically infinite"
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

            // Return result with implicit cast to System.Drawing.Rectangle
            return result;
        }
    }
}

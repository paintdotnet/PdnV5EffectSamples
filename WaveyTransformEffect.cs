using ComputeSharp;
using ComputeSharp.D2D1;
using ComputeSharp.D2D1.Interop;
using PaintDotNet;
using PaintDotNet.Direct2D1;
using PaintDotNet.Direct2D1.Effects;
using PaintDotNet.Effects;
using PaintDotNet.Effects.Gpu;
using PaintDotNet.Imaging;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;
using PaintDotNet.Rendering;
using System;
using System.Collections.Generic;

// This sample illustrates how to use a shader that outputs a "sample map" to use with SampleMapRenderer.
// Sample maps are often used to implement "distortion" effects.
// Using SampleMapRenderer is recommended, versus rolling your own, as it is complicated to get it working
// correctly with large images due to Direct2D's effects rendering architecture.

namespace PaintDotNet.Effects.Gpu.Samples;

using IDeviceContext = PaintDotNet.Direct2D1.IDeviceContext;

internal sealed partial class WaveyTransformEffect
    : PropertyBasedGpuImageEffect
{
    public WaveyTransformEffect()
        : base(
            "Wavey Transform (GPU Sample)",
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
        Scale
    }

    protected override PropertyCollection OnCreatePropertyCollection()
    {
        List<Property> properties = new List<Property>();
        properties.Add(new DoubleProperty(PropertyNames.Scale, 100.0, 1, 200.0));
        return new PropertyCollection(properties);
    }

    private SampleMapShader shader;
    private Guid shaderEffectID;
    private IDeviceEffect? sampleMapEffect;
    private SampleMapRenderer? sampleMapRenderer;

    protected override void OnSetRenderInfo(PropertyBasedEffectConfigToken newToken)
    {
        double scale = newToken.GetProperty<DoubleProperty>(PropertyNames.Scale).Value;
        SizeInt32 sourceImageSize = this.SourceSize;
        this.shader = new SampleMapShader(
            new uint2((uint)sourceImageSize.Width, (uint)sourceImageSize.Height),
            (float)scale);
       
        base.OnSetRenderInfo(newToken);
    }

    protected override InspectTokenAction OnInspectTokenChanges(PropertyBasedEffectConfigToken oldToken, PropertyBasedEffectConfigToken newToken)
    {
        return InspectTokenAction.UpdateOutput;
    }

    protected override void OnSetDeviceContext(IDeviceContext deviceContext)
    {
        deviceContext.Factory.RegisterEffectFromBlob(
            D2D1PixelShaderEffect.GetRegistrationBlob<SampleMapShader>(out this.shaderEffectID));

        base.OnSetDeviceContext(deviceContext);
    }

    protected override IDeviceImage OnCreateOutput(IDeviceContext deviceContext)
    {
        this.sampleMapEffect = deviceContext.CreateEffect(this.shaderEffectID);

        this.sampleMapRenderer = new SampleMapRenderer(deviceContext, this.SourceSize);
        this.sampleMapRenderer.SetInput(this.SourceImage);
        this.sampleMapRenderer.SampleMapCount = 1;
        this.sampleMapRenderer.SetSampleMap(0, this.sampleMapEffect);
        this.sampleMapRenderer.EdgeMode = SampleMapEdgeMode.Mirror;

        return this.sampleMapRenderer.GetOutput();
    }

    protected override void OnUpdateOutput(IDeviceContext deviceContext)
    {
        this.sampleMapEffect!.SetValue(
            0, 
            PropertyType.Blob, 
            D2D1PixelShader.GetConstantBuffer(this.shader));

        base.OnUpdateOutput(deviceContext);
    }

    protected override void OnInvalidateDeviceResources()
    {
        this.sampleMapEffect?.Dispose();
        this.sampleMapEffect = null;

        this.sampleMapRenderer?.Dispose();
        this.sampleMapRenderer = null;

        base.OnInvalidateDeviceResources();
    }

    [D2DInputCount(0)]
    [D2DRequiresScenePosition]
    [D2DEmbeddedBytecode(D2D1ShaderProfile.PixelShader50)]
    [AutoConstructor]
    private readonly partial struct SampleMapShader
        : ID2D1PixelShader
    {
        private readonly uint2 imageSize;
        private readonly float scale;

        public float4 Execute()
        {
            // Gets the current (x,y) 
            float2 scenePos = D2D.GetScenePosition().XY;

            float2 imageSizeF = this.imageSize;
            float2 uv = ((scenePos / imageSizeF) - new float2(0.5f, 0.5f)) * 2.0f; // [-1, +1], with [0,0] at the center

            Hlsl.SinCos(uv.X * this.scale, out float sinU, out float cosU);
            Hlsl.SinCos(uv.Y * this.scale, out float sinV, out float cosV);

            float2 dxdy = new float2(
                (sinU + cosV) * this.scale, 
                (cosU + sinV) * this.scale);

            // The return value from a sample map tells the SampleMapRenderer where to read from the input image.
            // The format is (X, Y, A, 0), where (X, Y) is the sampling position, which is then multiplied by A.
            // Usually you should just set A to 1, but you can also use it to modulate the alpha/transparency of
            // the sampled pixel.
            // The sampled pixel will then be drawn at the current scene position.
            return new float4(scenePos + dxdy, 1, 1);
        }
    }
}

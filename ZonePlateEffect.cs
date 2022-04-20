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
using System.Drawing;

// https://github.com/saucecontrol/PhotoSauce/blob/v0.13.0/src/MagicScaler/Magic/ZonePlatePixelSource.cs
// https://en.wikipedia.org/wiki/Zone_plate

namespace PaintDotNet.Effects.Gpu.Samples;

using IDeviceContext = PaintDotNet.Direct2D1.IDeviceContext;

internal sealed partial class ZonePlateEffect
    : PropertyBasedGpuImageEffect
{
    public ZonePlateEffect()
        : base(
            "Zone Plate (GPU Sample)",
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
        ZonePlateScale,
        DisplacementScale
    }

    protected override PropertyCollection OnCreatePropertyCollection()
    {
        List<Property> properties = new List<Property>();
        properties.Add(new DoubleProperty(PropertyNames.ZonePlateScale, 1.0, 0.0, 2.0));
        properties.Add(new DoubleProperty(PropertyNames.DisplacementScale, 25, 0, 1000));
        return new PropertyCollection(properties);
    }

    private Shader shader;
    private Guid shaderEffectID;
    private IDeviceEffect? shaderEffect;

    protected override void OnSetRenderInfo(PropertyBasedEffectConfigToken newToken, RenderArgs dstArgs, RenderArgs srcArgs)
    {
        double zonePlateScale = newToken.GetProperty<DoubleProperty>(PropertyNames.ZonePlateScale).Value;
        SizeInt32 sourceImageSize = this.SourceImageSize;
        double diameter = (Math.Min(sourceImageSize.Width, sourceImageSize.Height) & ~1) * zonePlateScale;
        this.shader = new Shader(sourceImageSize.Width, sourceImageSize.Height, (float)diameter);
       
        base.OnSetRenderInfo(newToken, dstArgs, srcArgs);
    }

    protected override InspectTokenAction OnInspectTokenChanges(PropertyBasedEffectConfigToken oldToken, PropertyBasedEffectConfigToken newToken)
    {
        return InspectTokenAction.UpdateOutput;
    }

    protected override void OnSetDeviceContext(IDeviceContext deviceContext)
    {
        deviceContext.Factory.RegisterEffectFromBlob(
            D2D1InteropServices.GetPixelShaderEffectRegistrationBlob<Shader, ShaderTransformMapper>(out this.shaderEffectID));

        base.OnSetDeviceContext(deviceContext);
    }

    protected override IDeviceImage OnCreateOutput(IDeviceContext deviceContext)
    {
        this.shaderEffect = deviceContext.CreateEffect(this.shaderEffectID);
        return this.shaderEffect;
    }

    protected override void OnUpdateOutput(IDeviceContext deviceContext)
    {
        this.shaderEffect!.SetValue(
            0, 
            PropertyType.Blob, 
            D2D1InteropServices.GetPixelShaderConstantBufferForD2D1DrawInfo(this.shader));

        base.OnUpdateOutput(deviceContext);
    }

    protected override void OnInvalidateDeviceResources()
    {
        this.shaderEffect?.Dispose();
        this.shaderEffect = null;

        base.OnInvalidateDeviceResources();
    }

    [D2DInputCount(0)]
    [D2DRequiresScenePosition]
    [D2DEmbeddedBytecode(D2D1ShaderProfile.PixelShader50)]
    [AutoConstructor]
    private readonly partial struct Shader
        : ID2D1PixelShader
    {
        private readonly int width;
        private readonly int height;
        private readonly float diameter;

        public float4 Execute()
        {
            float2 scenePos = D2D.GetScenePosition().XY;

            float xo = scenePos.X - (this.width >> 1);
            float yo = scenePos.Y - (this.height >> 1);

            float rm = 0.5f * this.diameter;
            float km = 0.7f / this.diameter * MathF.PI;
            float w = rm / 10.0f;

            float yd = yo * yo;
            float xd = xo * xo;
            float d = xd + yd;
            float v = 1.0f + (1.0f + Hlsl.Tanh((rm - Hlsl.Sqrt(d)) / w)) * Hlsl.Sin(km * d) * 0.5f;

            return new Float4(v, v, v, 1.0f);
        }
    }

    // TODO: BUG in CS.D2D1. This "default" mapper shouldn't be necessary for an output-only pixel shader with no clip rect.
    private sealed class ShaderTransformMapper
        : ID2D1TransformMapper<Shader>
    {
        public void MapInputsToOutput(in Shader shader, ReadOnlySpan<Rectangle> inputs, ReadOnlySpan<Rectangle> opaqueInputs, out Rectangle output, out Rectangle opaqueOutput)
        {
            output = default;
            output.ToD2D1Infinite();

            opaqueOutput = default;
        }

        public void MapInvalidOutput(in Shader shader, int inputIndex, Rectangle invalidInput, out Rectangle invalidOutput)
        {
            throw new NotImplementedException();
        }

        public void MapOutputToInputs(in Shader shader, in Rectangle output, Span<Rectangle> inputs)
        {
            throw new NotImplementedException();
        }
    }
}

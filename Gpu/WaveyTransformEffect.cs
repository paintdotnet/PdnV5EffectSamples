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

namespace PaintDotNet.Effects.Samples.Gpu;

internal sealed partial class WaveyTransformEffect
    : PropertyBasedGpuImageEffect
{
    public WaveyTransformEffect()
        : base(
            "Wavey Transform (GPU Sample)",
            "PDN v5 Samples (GPU)",
            new GpuImageEffectOptions()
            {
                IsConfigurable = true
            })
    {
    }

    private enum PropertyNames
    {
        Scale,
        Quality
    }

    protected override PropertyCollection OnCreatePropertyCollection()
    {
        List<Property> properties = new List<Property>();
        properties.Add(new DoubleProperty(PropertyNames.Scale, 100.0, 1, 200.0));
        properties.Add(new Int32Property(PropertyNames.Quality, 2, 1, 8));
        return new PropertyCollection(properties);
    }

    // To implement multisample antialiasing, we must render using sub-pixel offsets.
    // EffectHelpers.GetRgssOffset() provides us with those offsets. The sample count
    // is the square of the Quality property, which is calculated by EffectHelpers.GetRgssOffsetsCount(quality).
    //
    // The sample map shader will use the values from its input instead of D2D.GetScenePosition().
    // This allows us to program the sample map shader to calculate its values for any location.
    // (in this case, the "real" scene position + the sub-pixel offset).
    //
    // Multiple ScenePositionEffects are used in order to permit Direct2D to use shader linking,
    // which essentially inlines the shader into the effect that consumes it. Otherwise, if we
    // use only 1 ScenePositionEffect, it would be rendered once into an intermediate texture
    // which would then be shared by the other effects that use it as an input. This would then
    // use a lot more memory bandwidth in this case because ScenePositionEffect is trivial.

    // The effect graph looks like this when quality=1:
    //
    // [scenePositionEffects]  [subPxOffset]                              [SourceImage]
    //               |              |                                          |
    //               v              v                                          v
    //       [HlslBinaryOperatorEffect(Add)] --> [sampleMapEffect] --> [SampleMapRenderer] --> (output)

    private ScenePositionEffect[]? scenePositionEffects;
    private HlslBinaryOperatorEffect[]? subPxScenePositionEffects;
    private Guid sampleMapEffectID;
    private IDeviceEffect[]? sampleMapEffects;
    private SampleMapRenderer? sampleMapRenderer;

    private static int GetQualityFromToken(PropertyBasedEffectConfigToken token)
    {
        return token.GetProperty<Int32Property>(PropertyNames.Quality).Value;
    }

    protected override void OnSetDeviceContext(IDeviceContext deviceContext)
    {
        // We only need to register the shader when the device context is initialized.
        // The device context can be destroyed and recreated, such as when certain system events occur.
        // It's also okay to register the effect in OnCreateOutput(), as registration is idempotent,
        // but doing it here is better for performance.
        deviceContext.Factory.RegisterEffectFromBlob(
            D2D1PixelShaderEffect.GetRegistrationBlob<SampleMapShader>(out this.sampleMapEffectID));

        base.OnSetDeviceContext(deviceContext);
    }

    protected override IDeviceImage OnCreateOutput(IDeviceContext deviceContext)
    {
        int quality = GetQualityFromToken(this.Token);
        int sampleCount = EffectHelpers.GetRgssOffsetsCount(quality);

        this.scenePositionEffects = new ScenePositionEffect[sampleCount];
        this.subPxScenePositionEffects = new HlslBinaryOperatorEffect[sampleCount];
        this.sampleMapEffects = new IDeviceEffect[sampleCount];

        for (int i = 0; i < sampleCount; ++i)
        {
            this.scenePositionEffects[i] = new ScenePositionEffect(deviceContext);

            this.subPxScenePositionEffects[i] = new HlslBinaryOperatorEffect(deviceContext);
            this.subPxScenePositionEffects[i].Properties.Parameter1.SetValue(HlslEffectParameter.Input);
            this.subPxScenePositionEffects[i].Properties.Input1.Set(this.scenePositionEffects[i]);
            this.subPxScenePositionEffects[i].Properties.Operator.SetValue(HlslBinaryOperator.Add);
            this.subPxScenePositionEffects[i].Properties.Parameter2.SetValue(HlslEffectParameter.Value);
            // We will set the Value2 property in OnUpdateOutput()

            this.sampleMapEffects[i] = deviceContext.CreateEffect(this.sampleMapEffectID);
            this.sampleMapEffects[i].SetInput(0, this.subPxScenePositionEffects[i]);
            // We will update the shader constants in OnUpdateOutput()
        }

        this.sampleMapRenderer = new SampleMapRenderer(deviceContext);

        // Using an update transaction is optional, but helps performance since updating properties on the
        // SampleMapRenderer can be expensive. Using a transaction allows it to defer its internal updates
        // until the end of the transaction.
        this.sampleMapRenderer.Properties.Input.Set(this.Environment.SourceImage);
        this.sampleMapRenderer.Properties.SampleMaps.SetCount(sampleCount);

        for (int i = 0; i < sampleCount; ++i)
        {
            this.sampleMapRenderer.Properties.SampleMaps[i].Set(this.sampleMapEffects[i]);
        }

        this.sampleMapRenderer.Properties.EdgeMode.SetValue(SampleMapEdgeMode.Mirror);

        return this.sampleMapRenderer;
    }

    protected override InspectTokenAction OnInspectTokenChanges(PropertyBasedEffectConfigToken oldToken, PropertyBasedEffectConfigToken newToken)
    {
        // If the quality changes, we need to recreate the effect graph because the number of
        // inputs to the SampleMapRenderer has changed.
        // It's possible to do a differential update, e.g. destroy/create things as necessary
        // for the transition, but doing it this way is much easier.
        // Not doing the differential update has a small performance cost but it only happens
        // when the user changes the Quality slider, so it's not very impactful.
        int oldQuality = GetQualityFromToken(oldToken);
        int newQuality = GetQualityFromToken(newToken);
        return (oldQuality == newQuality) ? InspectTokenAction.UpdateOutput : InspectTokenAction.RecreateOutput;
    }

    protected override void OnUpdateOutput(IDeviceContext deviceContext)
    {
        double scale = this.Token.GetProperty<DoubleProperty>(PropertyNames.Scale).Value;
        int quality = GetQualityFromToken(this.Token);
        int sampleCount = EffectHelpers.GetRgssOffsetsCount(quality);

        SampleMapShader sampleMapShader = new SampleMapShader(
            new float2(
                (float)(1.0 / this.Environment.Document.Size.Width),
                (float)(1.0 / this.Environment.Document.Size.Height)),
            (float)scale);

        ReadOnlyMemory<byte> sampleMapConstantBuffer = D2D1PixelShader.GetConstantBuffer(sampleMapShader);

        for (int i = 0; i < sampleCount; ++i)
        {
            Vector2Float subPxOffset = EffectHelpers.GetRgssOffset(quality, i);
            this.subPxScenePositionEffects![i].Properties.Value2.SetValue(new Vector4Float(subPxOffset, 0, 0));
            this.sampleMapEffects![i]!.SetValue(D2D1PixelShaderEffectProperty.ConstantBuffer, sampleMapConstantBuffer);
        }

        base.OnUpdateOutput(deviceContext);
    }

    // NOTE: It's very important to declare your input as "simple" when you only need to read the current position from it.
    // Otherwise you will likely get an IntermediateTooLargeException, as Direct2D assumes you need the ability to read
    // from any pixel. Direct2D is only willing to use intermediate buffers as large as 4096x4096, so your effect would
    // only work on images up to that size.
    [D2DInputCount(1)]
    [D2DInputSimple(0)]
    [D2DInputDescription(0, D2D1Filter.MinMagMipPoint)]
    [D2DShaderProfile(D2D1ShaderProfile.PixelShader50)]
    [AutoConstructor]
    private readonly partial struct SampleMapShader
        : ID2D1PixelShader
    {
        private readonly float2 invImageSize;
        private readonly float scale;

        public float4 Execute()
        {
            float2 scenePos = D2D.GetInput(0).XY;

            float2 uv = ((scenePos * this.invImageSize) - new float2(0.5f, 0.5f)) * 2.0f; // [-1, +1], with [0,0] at the center

            Hlsl.SinCos(uv.X * this.scale, out float sinU, out float cosU);
            Hlsl.SinCos(uv.Y * this.scale, out float sinV, out float cosV);

            float2 dxdy = new float2(
                (sinU + cosV) * this.scale, 
                (cosU + sinV) * this.scale);

            // The return value from a sample map tells the SampleMapRenderer where to read from the input image.
            // The format is (X, Y, A, 1), where (X, Y) is the sampling position, which is then multiplied by A.
            // Usually you should just set A to 1, but you can also use it to modulate the alpha/transparency of
            // the sampled pixel.
            // The sampled pixel will then be drawn at the current scene position.
            return new float4(scenePos + dxdy, 1, 1);
        }
    }
}

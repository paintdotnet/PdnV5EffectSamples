using ComputeSharp;
using ComputeSharp.D2D1;
using ComputeSharp.D2D1.Interop;
using PaintDotNet.Direct2D1;
using PaintDotNet.Direct2D1.Effects;
using PaintDotNet.Effects.Gpu;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;
using System;
using System.Collections.Generic;

// This sample shows an effective way of using random numbers in a pixel shader.
// The random number generator is also used in Paint.NET, and is based on this blog post by Nathan Reed:
//     https://www.reedbeta.com/blog/hash-functions-for-gpu-rendering/

namespace PaintDotNet.Effects.Samples.Gpu;

internal sealed partial class RandomNoiseEffect
    : PropertyBasedGpuImageEffect
{
    public RandomNoiseEffect()
        : base(
            "Random Noise",
            "PDN v5.1 Samples (GPU)",
            GpuImageEffectOptions.Create() with
            {
                IsConfigurable = true
            })
    {
    }

    private enum PropertyNames
    {
        ColorMode,
        Blending,
        BlendMode,
        Seed
    }

    private enum ColorMode
    {
        RGB = 0,
        Grayscale = 1
    }

    protected override PropertyCollection OnCreatePropertyCollection()
    {
        List<Property> properties = new List<Property>();
        properties.Add(StaticListChoiceProperty.CreateForEnum(PropertyNames.ColorMode, ColorMode.RGB));
        properties.Add(new BooleanProperty(PropertyNames.Blending, false));
        properties.Add(StaticListChoiceProperty.CreateForEnum(PropertyNames.BlendMode, BlendMode.Multiply));
        properties.Add(new Int32Property(PropertyNames.Seed, 0, 0, 255));

        List<PropertyCollectionRule> rules = new List<PropertyCollectionRule>();
        rules.Add(new ReadOnlyBoundToBooleanRule(PropertyNames.BlendMode, PropertyNames.Blending, true));

        return new PropertyCollection(properties, rules);
    }

    protected override ControlInfo OnCreateConfigUI(PropertyCollection props)
    {
        ControlInfo configUI = CreateDefaultConfigUI(props);

        configUI.SetPropertyControlType(PropertyNames.ColorMode, PropertyControlType.RadioButton);

        // The value from this isn't actually used, not directly.
        // Clicking the increment button forces the property collection to change, which means
        // OnUpdateOutput() will be called and we can generate a new random seed
        configUI.SetPropertyControlType(PropertyNames.Seed, PropertyControlType.IncrementButton);

        return configUI;
    }

    protected override InspectTokenAction OnInspectTokenChanges(
        PropertyBasedEffectConfigToken oldToken, 
        PropertyBasedEffectConfigToken newToken)
    {
        return InspectTokenAction.UpdateOutput;
    }

    private Guid shaderEffectID;
    private IDeviceEffect? shaderEffect;
    private GrayscaleEffect? grayscaleEffect;
    private InputSelectorEffect? coloredShaderEffect;
    private BlendEffect? blendEffect;
    private InputSelectorEffect? outputEffect;

    protected override void OnInvalidateDeviceResources()
    {
        this.shaderEffect?.Dispose();
        this.shaderEffect = null;

        this.grayscaleEffect?.Dispose();
        this.grayscaleEffect = null;

        this.coloredShaderEffect?.Dispose();
        this.coloredShaderEffect = null;

        this.blendEffect?.Dispose();
        this.blendEffect= null;

        this.outputEffect?.Dispose();
        this.outputEffect = null;

        base.OnInvalidateDeviceResources();
    }

    protected override void OnSetDeviceContext(IDeviceContext deviceContext)
    {
        deviceContext.Factory.RegisterEffectFromBlob(
            D2D1PixelShaderEffect.GetRegistrationBlob<Shader>(out this.shaderEffectID));

        base.OnSetDeviceContext(deviceContext);
    }

    protected override IDeviceImage OnCreateOutput(IDeviceContext deviceContext)
    {
        this.shaderEffect = deviceContext.CreateEffect(this.shaderEffectID);

        this.grayscaleEffect = new GrayscaleEffect(deviceContext);
        this.grayscaleEffect.Properties.Input.Set(this.shaderEffect);

        this.coloredShaderEffect = new InputSelectorEffect(deviceContext);
        this.coloredShaderEffect.InputCount = 2;
        this.coloredShaderEffect.SetInput((int)ColorMode.RGB, this.shaderEffect);
        this.coloredShaderEffect.SetInput((int)ColorMode.Grayscale, this.grayscaleEffect);

        this.blendEffect = new BlendEffect(deviceContext);
        this.blendEffect.Properties.Destination.Set(this.Environment.SourceImage);
        this.blendEffect.Properties.Source.Set(this.coloredShaderEffect);

        this.outputEffect = new InputSelectorEffect(deviceContext);
        this.outputEffect.Properties.Inputs.Add(this.coloredShaderEffect);
        this.outputEffect.Properties.Inputs.Add(this.blendEffect);

        return this.outputEffect;
    }

    protected override void OnUpdateOutput(IDeviceContext deviceContext)
    {
        uint instanceSeed = (uint)Random.Shared.NextInt64(0, (long)uint.MaxValue + 1);
        Shader shader = new Shader(instanceSeed);
        this.shaderEffect!.SetValue(
            D2D1PixelShaderEffectProperty.ConstantBuffer,
            D2D1PixelShader.GetConstantBuffer(shader));

        ColorMode colorMode = (ColorMode)this.Token.GetProperty(PropertyNames.ColorMode)!.Value!;
        this.coloredShaderEffect!.Properties.Index.SetValue((int)colorMode);

        BlendMode blendMode = (BlendMode)this.Token.GetProperty(PropertyNames.BlendMode)!.Value!;
        this.blendEffect!.Properties.Mode.SetValue(blendMode);

        bool blending = this.Token.GetProperty<BooleanProperty>(PropertyNames.Blending)!.Value;
        this.outputEffect!.Properties.Index.SetValue(blending ? 1 : 0);

        base.OnUpdateOutput(deviceContext);
    }

    [D2DInputCount(0)]
    [D2DRequiresScenePosition]
    [D2DShaderProfile(D2D1ShaderProfile.PixelShader50)]
    [D2DGeneratedPixelShaderDescriptor]
    [AutoConstructor]
    internal readonly partial struct Shader
        : ID2D1PixelShader
    {
        private readonly uint instanceSeed;

        public float4 Execute()
        {
            float2 scenePos = D2D.GetScenePosition().XY;

            uint seed = HlslRandom.PcgInitializedSeed(this.instanceSeed, scenePos);

            return new float4(
                HlslRandom.PcgNextFloat(ref seed),
                HlslRandom.PcgNextFloat(ref seed),
                HlslRandom.PcgNextFloat(ref seed),
                1.0f);
        }
    }
}

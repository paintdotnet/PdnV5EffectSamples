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

namespace PaintDotNet.Effects.Samples.Gpu;

// This is the same as HueRotateEffect, except that it does implement OnSetDeviceContext(),
// OnInvalidateDeviceResources(), OnInspectTokenChanges(), and OnUpdateOutput(). Thus, this sample
// shows how to graduate from the simple "happy path" to a higher-performance version of the same
// code. This particular effect does not gain much performance, but should still run a smidge
// faster on slower CPUs due to not needing to recreate objects every time the user moves the Angle
// slider in the UI. This also reduces pressure on the .NET garbage collector, since it won't
// need to finalize those objects.

internal sealed partial class HueRotateEffectAdvanced
    : PropertyBasedGpuImageEffect
{
    public HueRotateEffectAdvanced()
        : base(
            "Hue Rotate (GPU Sample, Advanced Implementation)",
            "PDN v5.1 Samples (GPU)",
            GpuImageEffectOptions.Create() with
            {
                IsConfigurable = true
            })
    {
    }

    private enum PropertyNames
    {
        Angle
    }

    protected override PropertyCollection OnCreatePropertyCollection()
    {
        List<Property> properties = new List<Property>();
        properties.Add(new DoubleProperty(PropertyNames.Angle, 0.0, -180.0, +180.0));
        return new PropertyCollection(properties);
    }

    protected override ControlInfo OnCreateConfigUI(PropertyCollection props)
    {
        ControlInfo configUI = CreateDefaultConfigUI(props);
        configUI.SetPropertyControlType(PropertyNames.Angle, PropertyControlType.AngleChooser);
        return configUI;
    }

    private Guid shaderEffectID;
    private IDeviceEffect? shaderEffect;

    protected override void OnSetDeviceContext(IDeviceContext deviceContext)
    {
        deviceContext.Factory.RegisterEffectFromBlob(D2D1PixelShaderEffect.GetRegistrationBlob<Shader>(out this.shaderEffectID));
        base.OnSetDeviceContext(deviceContext);
    }

    protected override IDeviceImage OnCreateOutput(IDeviceContext deviceContext)
    {
        // In the "advanced" implemention of OnCreateOutput(), we will create all the Direct2D effects and link
        // them together to form the effect graph. We will set the properties on the effects that not change
        // based on the token properties (rom the UI).

        // 1. Convert SourceImage from premultiplied RGBA to HSVA
        // Direct2D's RGB-to-Hue effect: https://docs.microsoft.com/en-us/windows/win32/direct2d/rgb-to-hue-effect
        using RgbToHueEffect rgbToHueEffect = new RgbToHueEffect(deviceContext);
        rgbToHueEffect.Properties.Input.Set(this.Environment.SourceImage);
        rgbToHueEffect.Properties.OutputColorSpace.SetValue(RgbToHueOutputColorSpace.HueSaturationValue);

        // 2. Use our own pixel shader to modify the hue (which is stored in the R channel)
        this.shaderEffect = deviceContext.CreateEffect(this.shaderEffectID);
        this.shaderEffect.SetInput(0, rgbToHueEffect);

        // 3. Convert from HSVA back to premultiplied RGBA.
        // Direct2D's Hue-to-RGB effect: https://docs.microsoft.com/en-us/windows/win32/direct2d/hue-to-rgb-effect
        HueToRgbEffect hueToRgbEffect = new HueToRgbEffect(deviceContext);
        hueToRgbEffect.Properties.Input.Set(shaderEffect);
        hueToRgbEffect.Properties.InputColorSpace.SetValue(HueToRgbInputColorSpace.HueSaturationValue);

        return hueToRgbEffect;
    }

    protected override void OnInvalidateDeviceResources()
    {
        this.shaderEffect?.Dispose();
        this.shaderEffect = null;

        base.OnInvalidateDeviceResources();
    }

    protected override InspectTokenAction OnInspectTokenChanges(PropertyBasedEffectConfigToken oldToken, PropertyBasedEffectConfigToken newToken)
    {
        // For this effect we never need to rebuild the effect graph. We just need to copy the Token's properties
        // (only 1 in this case) over to the shader's constant buffer, via the D2D1PixelShaderEffect that hosts it.
        // That is done in OnUpdateOutput().
        return InspectTokenAction.UpdateOutput;
    }

    protected override void OnUpdateOutput(IDeviceContext deviceContext)
    {
        // In the "advanced" implementation of OnUpdateOutput(), we will read the property values from the token,
        // and apply them to the effects in the effect graph. We cannot change the effect graph -- if we need to
        // do that, we need to return InspectTokenAction.RecreateOutput  from OnInspectTokenChanges().

        double angle = this.Token.GetProperty<DoubleProperty>(PropertyNames.Angle)!.Value;
        this.shaderEffect!.SetValue(
            D2D1PixelShaderEffectProperty.ConstantBuffer,
            D2D1PixelShader.GetConstantBuffer(new Shader((float)angle)));

        base.OnUpdateOutput(deviceContext);
    }

    [D2DInputCount(1)]
    [D2DInputSimple(0)]
    [D2DInputDescription(0, D2D1Filter.MinMagMipPoint)] // This specifies the filter (sampling) for the input. Since we only read from the input at the same place we write to, point (nearest neighbor) is the right choice (and is fastest).
    [D2DShaderProfile(D2D1ShaderProfile.PixelShader50)] // This causes the shader to be compiled at compile time instead of runtime
    [D2DGeneratedPixelShaderDescriptor]
    [AutoConstructor]
    internal readonly partial struct Shader
        : ID2D1PixelShader
    {
        private readonly float angle;

        public float4 Execute()
        {
            float4 hsva = D2D.GetInput(0);

            // From the documentation for Direct2D's RGB-to-Hue effect: https://docs.microsoft.com/en-us/windows/win32/direct2d/rgb-to-hue-effect
            // "This effect normalizes the output data (hue, saturation value for HSV or hue, saturation, lightness for HSL) to the range [0, 1]."
            // The hue, stored in the red channel, is [0, 1] instead of [0, 360], so we must do some math.
            float hue = hsva.R * 360.0f;
            float newHue = (hue + this.angle) % 360.0f;
            float newR = newHue / 360.0f;

            return new float4(newR, hsva.GBA);
        }
    }
}

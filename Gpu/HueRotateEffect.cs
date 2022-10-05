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

// This is a simple pixel shader-based effect that illustrates a "minimal" / "happy path" implementation,
// and how to use SourceImage (the active layer's bitmap) in an effect graph.
// This does not implement OnSetDeviceContext(), OnInvalidateDeviceResources(), OnInspectTokenChanges(),
// or OnUpdateOutput(). Those methods are important for performance in some cases, but are not really
// necessary when getting an effect up and running for the first time, or for simple effect graphs.
// Also, only the minimum necessary attributes on the shader are specified.

internal sealed partial class HueRotateEffect
    : PropertyBasedGpuImageEffect
{
    public HueRotateEffect()
        : base(
            "Hue Rotate (GPU Sample)",
            "PDN v5 Samples (GPU)",
            new GpuImageEffectOptions()
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

    protected override IDeviceImage OnCreateOutput(IDeviceContext deviceContext)
    {
        double angle = this.Token.GetProperty<DoubleProperty>(PropertyNames.Angle).Value;

        // 1. Convert SourceImage from premultiplied RGBA to HSVA
        // Direct2D's RGB-to-Hue effect: https://docs.microsoft.com/en-us/windows/win32/direct2d/rgb-to-hue-effect
        RgbToHueEffect rgbToHueEffect = new RgbToHueEffect(deviceContext);
        rgbToHueEffect.Properties.Input.Set(this.Environment.SourceImage);
        rgbToHueEffect.Properties.OutputColorSpace.SetValue(RgbToHueOutputColorSpace.HueSaturationValue);

        // 2. Use our own pixel shader to modify the hue (which is stored in the R channel)
        deviceContext.Factory.RegisterEffectFromBlob(D2D1PixelShaderEffect.GetRegistrationBlob<Shader>(out Guid shaderEffectID));
        IDeviceEffect shaderEffect = deviceContext.CreateEffect(shaderEffectID);
        shaderEffect.SetInput(0, rgbToHueEffect);
        shaderEffect.SetValue(
            D2D1PixelShaderEffectProperty.ConstantBuffer,
            PropertyType.Blob,
            D2D1PixelShader.GetConstantBuffer(new Shader((float)angle)));

        // 3. Convert from HSVA back to premultiplied RGBA.
        // Direct2D's Hue-to-RGB effect: https://docs.microsoft.com/en-us/windows/win32/direct2d/hue-to-rgb-effect
        HueToRgbEffect hueToRgbEffect = new HueToRgbEffect(deviceContext);
        hueToRgbEffect.Properties.Input.Set(shaderEffect);
        hueToRgbEffect.Properties.InputColorSpace.SetValue(HueToRgbInputColorSpace.HueSaturationValue);

        return hueToRgbEffect;
    }

    [D2DInputCount(1)]
    [D2DInputSimple(0)]
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

using PaintDotNet.Direct2D1;
using PaintDotNet.Direct2D1.Effects;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;
using PaintDotNet.Effects.Gpu;
using PaintDotNet.Imaging;
using PaintDotNet.Rendering;
using System;
using System.Collections.Generic;

namespace PaintDotNet.Effects.Samples.Gpu;

// This sample effect was migrated from the CodeLab script used to make this plugin: https://forums.getpaint.net/topic/123583-iris-blur-gpu/
// See that link for more information and discussion.

internal sealed class IrisBlurEffectPlugin 
    : PropertyBasedGpuImageEffect
{
    public IrisBlurEffectPlugin()
        : base(
            "Iris Blur",
            "PDN v5.1 Samples (GPU)", 
            GpuImageEffectOptions.Create() with 
            { 
                IsConfigurable = true 
            })
    {
    }

    public enum PropertyNames
    {
        Center,
        BlurType,
        Fovea,
        BlurRadius,
        BlurRadiusRampLength,
        BlurRadiusRampExponent,
        Gamma,
        Reversed,
        Quality
    }

    protected override PropertyCollection OnCreatePropertyCollection()
    {
        List<Property> props = new List<Property>();

        props.Add(new DoubleVectorProperty(PropertyNames.Center, new Vector2Double(0.0, 0.0), new Vector2Double(-1.0, -1.0), new Vector2Double(+1.0, +1.0)));
        props.Add(StaticListChoiceProperty.CreateForEnum<BlurTypeEnum>(PropertyNames.BlurType, 0, false));
        props.Add(new DoubleProperty(PropertyNames.Fovea, 0.1, 0, 1));
        props.Add(new DoubleProperty(PropertyNames.BlurRadius, 50, 0, 300));
        props.Add(new DoubleProperty(PropertyNames.BlurRadiusRampLength, 1, 0.01, 3));
        props.Add(new DoubleProperty(PropertyNames.BlurRadiusRampExponent, 1.5, 0.25, 4));
        props.Add(new DoubleProperty(PropertyNames.Gamma, 2.2, 0.01, 6));
        props.Add(new BooleanProperty(PropertyNames.Reversed, false));
        props.Add(new Int32Property(PropertyNames.Quality, 4, 1, 8));

        return new PropertyCollection(props);
    }

    protected override ControlInfo OnCreateConfigUI(PropertyCollection props)
    {
        RectInt32 selection = Environment.Selection.RenderBounds;
        IBitmapSource<ColorBgra32> panImage = Environment.GetSourceBitmapBgra32().CreateClipper(selection);

        ControlInfo configUI = CreateDefaultConfigUI(props);

        configUI.SetPropertyControlValue(PropertyNames.Center, ControlInfoPropertyNames.DisplayName, "Center");
        configUI.SetPropertyControlValue(PropertyNames.Center, ControlInfoPropertyNames.SliderSmallChangeX, 0.05);
        configUI.SetPropertyControlValue(PropertyNames.Center, ControlInfoPropertyNames.SliderLargeChangeX, 0.25);
        configUI.SetPropertyControlValue(PropertyNames.Center, ControlInfoPropertyNames.UpDownIncrementX, 0.01);
        configUI.SetPropertyControlValue(PropertyNames.Center, ControlInfoPropertyNames.SliderSmallChangeY, 0.05);
        configUI.SetPropertyControlValue(PropertyNames.Center, ControlInfoPropertyNames.SliderLargeChangeY, 0.25);
        configUI.SetPropertyControlValue(PropertyNames.Center, ControlInfoPropertyNames.UpDownIncrementY, 0.01);
        configUI.SetPropertyControlValue(PropertyNames.Center, ControlInfoPropertyNames.DecimalPlaces, 2);
        configUI.SetPropertyControlValue(PropertyNames.Center, ControlInfoPropertyNames.StaticImageUnderlay, panImage);
        configUI.SetPropertyControlValue(PropertyNames.Center, ControlInfoPropertyNames.ShowHeaderLine, false);
        configUI.SetPropertyControlValue(PropertyNames.BlurType, ControlInfoPropertyNames.DisplayName, "Blur Type");
        PropertyControlInfo blurTypeControl = configUI.FindControlForPropertyName(PropertyNames.BlurType)!;
        blurTypeControl.SetValueDisplayName(BlurTypeEnum.Gaussian, "Gaussian");
        blurTypeControl.SetValueDisplayName(BlurTypeEnum.Bokeh, "Bokeh");
        configUI.SetPropertyControlValue(PropertyNames.BlurType, ControlInfoPropertyNames.ShowHeaderLine, false);
        configUI.SetPropertyControlValue(PropertyNames.Fovea, ControlInfoPropertyNames.DisplayName, "Fovea");
        configUI.SetPropertyControlValue(PropertyNames.Fovea, ControlInfoPropertyNames.SliderLargeChange, 0.25);
        configUI.SetPropertyControlValue(PropertyNames.Fovea, ControlInfoPropertyNames.SliderSmallChange, 0.05);
        configUI.SetPropertyControlValue(PropertyNames.Fovea, ControlInfoPropertyNames.UpDownIncrement, 0.01);
        configUI.SetPropertyControlValue(PropertyNames.Fovea, ControlInfoPropertyNames.DecimalPlaces, 2);
        configUI.SetPropertyControlValue(PropertyNames.Fovea, ControlInfoPropertyNames.ShowHeaderLine, false);
        configUI.SetPropertyControlValue(PropertyNames.BlurRadius, ControlInfoPropertyNames.DisplayName, "Blur Radius");
        configUI.SetPropertyControlValue(PropertyNames.BlurRadius, ControlInfoPropertyNames.SliderLargeChange, 0.25);
        configUI.SetPropertyControlValue(PropertyNames.BlurRadius, ControlInfoPropertyNames.SliderSmallChange, 0.05);
        configUI.SetPropertyControlValue(PropertyNames.BlurRadius, ControlInfoPropertyNames.UpDownIncrement, 0.01);
        configUI.SetPropertyControlValue(PropertyNames.BlurRadius, ControlInfoPropertyNames.DecimalPlaces, 2);
        configUI.SetPropertyControlValue(PropertyNames.BlurRadius, ControlInfoPropertyNames.ShowHeaderLine, false);
        configUI.SetPropertyControlValue(PropertyNames.BlurRadiusRampLength, ControlInfoPropertyNames.DisplayName, "Radius Ramp Length");
        configUI.SetPropertyControlValue(PropertyNames.BlurRadiusRampLength, ControlInfoPropertyNames.SliderLargeChange, 0.25);
        configUI.SetPropertyControlValue(PropertyNames.BlurRadiusRampLength, ControlInfoPropertyNames.SliderSmallChange, 0.05);
        configUI.SetPropertyControlValue(PropertyNames.BlurRadiusRampLength, ControlInfoPropertyNames.UpDownIncrement, 0.01);
        configUI.SetPropertyControlValue(PropertyNames.BlurRadiusRampLength, ControlInfoPropertyNames.DecimalPlaces, 2);
        configUI.SetPropertyControlValue(PropertyNames.BlurRadiusRampLength, ControlInfoPropertyNames.ShowHeaderLine, false);
        configUI.SetPropertyControlValue(PropertyNames.BlurRadiusRampExponent, ControlInfoPropertyNames.DisplayName, "Radius Ramp Exponent");
        configUI.SetPropertyControlValue(PropertyNames.BlurRadiusRampExponent, ControlInfoPropertyNames.SliderLargeChange, 0.25);
        configUI.SetPropertyControlValue(PropertyNames.BlurRadiusRampExponent, ControlInfoPropertyNames.SliderSmallChange, 0.05);
        configUI.SetPropertyControlValue(PropertyNames.BlurRadiusRampExponent, ControlInfoPropertyNames.UpDownIncrement, 0.01);
        configUI.SetPropertyControlValue(PropertyNames.BlurRadiusRampExponent, ControlInfoPropertyNames.DecimalPlaces, 2);
        configUI.SetPropertyControlValue(PropertyNames.BlurRadiusRampExponent, ControlInfoPropertyNames.ShowHeaderLine, false);
        configUI.SetPropertyControlValue(PropertyNames.Gamma, ControlInfoPropertyNames.DisplayName, "Gamma");
        configUI.SetPropertyControlValue(PropertyNames.Gamma, ControlInfoPropertyNames.SliderLargeChange, 0.25);
        configUI.SetPropertyControlValue(PropertyNames.Gamma, ControlInfoPropertyNames.SliderSmallChange, 0.05);
        configUI.SetPropertyControlValue(PropertyNames.Gamma, ControlInfoPropertyNames.UpDownIncrement, 0.01);
        configUI.SetPropertyControlValue(PropertyNames.Gamma, ControlInfoPropertyNames.DecimalPlaces, 2);
        configUI.SetPropertyControlValue(PropertyNames.Gamma, ControlInfoPropertyNames.ShowHeaderLine, false);
        configUI.SetPropertyControlValue(PropertyNames.Reversed, ControlInfoPropertyNames.DisplayName, string.Empty);
        configUI.SetPropertyControlValue(PropertyNames.Reversed, ControlInfoPropertyNames.Description, "Reversed");
        configUI.SetPropertyControlValue(PropertyNames.Reversed, ControlInfoPropertyNames.ShowHeaderLine, false);
        configUI.SetPropertyControlValue(PropertyNames.Quality, ControlInfoPropertyNames.DisplayName, "Quality");
        configUI.SetPropertyControlValue(PropertyNames.Quality, ControlInfoPropertyNames.ShowHeaderLine, false);

        return configUI;
    }

    protected override void OnInitializeRenderInfo(IGpuImageEffectRenderInfo renderInfo)
    {
        renderInfo.InputAlphaMode = GpuEffectAlphaMode.Premultiplied;
        renderInfo.OutputAlphaMode = GpuEffectAlphaMode.Premultiplied;

        // Normally GPU effect plugins operate in linear gamma space (1.0), which means
        // that the input image (active layer) is converted from sRGB (gamma 2.2) to
        // scRGB, aka linear sRGB. For this effect we want to allow the user to specify
        // the gamma, similarly to the built-in Gaussian and Bokeh effects. For
        // performance, WorkingSpace is specified, which prevents this automatic gamma
        // conversion (the default is WorkingSpaceLinear).
        // We can then use the ConvertGammaEffect to do custom, user-configurable gamma
        // conversion based on the Gamma slider (mostly for artistic effect, not actually
        // for gamma "correction" purposes). Without these we would need additional
        // rendering steps (additional effects in the graph), which would reduce
        // performance a little. You can still do this without specifying WorkingSpace
        // color context, but you will lose some performance. Not very much on most GPUs,
        // but it can affect slower ones, especially older Intel iGPUs, and when using CPU
        // rendering.
        renderInfo.ColorContext = GpuEffectColorContext.WorkingSpace;

        base.OnInitializeRenderInfo(renderInfo);
    }

    private enum BlurTypeEnum
    {
        Gaussian = 0,
        Bokeh = 1
    }

    protected override IDeviceImage OnCreateOutput(IDeviceContext deviceContext)
    {
        BlurTypeEnum blurType = (BlurTypeEnum)this.Token.GetProperty<StaticListChoiceProperty>(PropertyNames.BlurType)!.Value;

        RectFloat renderBounds = Environment.Selection.RenderBounds;

        // Blur is in units of standard deviations for Gaussian, and pixels for Radius
        // The StandardDeviation class can be used to convert between the two.
        double blurRadius = this.Token.GetProperty<DoubleProperty>(PropertyNames.BlurRadius)!.Value;
        float blurValue = (float)(blurType == BlurTypeEnum.Bokeh ? blurRadius : StandardDeviation.FromRadius(blurRadius));
        if (blurValue == 0.0)
        {
            // Zero blur is a no-op, so just jump straight to that
            return Environment.SourceImage;
        }

        Point2Float centerNorm = (Point2Float)this.Token.GetProperty<DoubleVectorProperty>(PropertyNames.Center)!.Value;
        Point2Float center = renderBounds.Center + ((Vector2Float)centerNorm * (Vector2Float)renderBounds.Center);

        // The "fovea" is the center part of the image where there is no blur. It is specified
        // as a normalized value. 0.1 is 10% of the image radius, 1.0 is 100% of it. The image
        // "radius" is the maximum of its width and height, divided by 2.
        float fovea = (float)this.Token.GetProperty<DoubleProperty>(PropertyNames.Fovea)!.Value;
        float sourceRadius = Math.Max(renderBounds.Width, renderBounds.Height) / 2.0f;

        float blurRadiusRampLength = (float)this.Token.GetProperty<DoubleProperty>(PropertyNames.BlurRadiusRampLength)!.Value;
        float blurRadiusRampExponent = (float)this.Token.GetProperty<DoubleProperty>(PropertyNames.BlurRadiusRampExponent)!.Value;
        float gamma = (float)this.Token.GetProperty<DoubleProperty>(PropertyNames.Gamma)!.Value;

        bool reversed = this.Token.GetProperty<BooleanProperty>(PropertyNames.Reversed)!.Value;
        int quality = this.Token.GetProperty<Int32Property>(PropertyNames.Quality)!.Value;

        /*
        Quality is used to determine the value of the Optimization property for Gaussian
        Blur ("GBOpt"), and the Quality property for Bokeh Blur (the value is used
        directly).

        GBOpt values of 1,2,3,4 map to the Optimization values of Speed, Balanced, Quality, HighQuality

        The tessellation quality is used for the Levels property on the blur map effects
        This specifies how many copies of the image are blended together, each one at an
        increasing level of blur, in order to simulate having a per-pixel blur radius.
        The sequence is calculated as TessQ[1]=1, TessQ[2]=2, TessQ[n] = TessQ[n-1] + (TessQ[n-1] - TessQ[n-2] + 1)
        (in other words, the delta increases by 1 for each successive value)

        Quality  GBOpt   TessQ
        +------+-------+-------+
        |  1   |   1   |    1  |
        +------+-------+-------+
        |  2   |   2   |    2  |
        +------+-------+-------+
        |  3   |   3   |    4  |
        +------+-------+-------+
        |  4   |   3   |    7  |
        +------+-------+-------+
        |  5   |   3   |   11  |
        +------+-------+-------+
        |  6   |   4   |   16  |
        +------+-------+-------+
        |  7   |   4   |   22  |
        +------+-------+-------+
        |  8   |   4   |   29  |
        +------+-------+-------+
        */
        int tessellationQuality = quality switch
        {
            1 => 1,
            2 => 2,
            3 => 4,
            4 => 7,
            5 => 11,
            6 => 16,
            7 => 22,
            8 => 29,
            _ => 29
        };

        // We don't yet have support for custom pixel shaders in CodeLab, so we instead
        // will string together some of the HLSL effects. Each HLSL effect instance
        // executes 1 HLSL instruction. Direct2D is able to link these simple effects
        // together in a way that maintains reasonable performance by avoiding extra
        // rendering passes. It's not as good as proper inlining, or writing a custom
        // shader directly, but it's good enough.
        IDeviceEffect perPixelRadius;
        {
            // ((((distance(center,scenePos)/(rampLength*sourceRadius))-fovea)/fovea)^rampExponent)*radius

            // First, plot the Euclidean distance from the center point
            // The "scene position" is just the (x,y) coordinate for that pixel (but
            // with a center-of-pixel offset, e.g. (1.5,1.5) instead of (1.0,1.0))
            perPixelRadius = new HlslBinaryFunctionEffect(
                deviceContext,
                HlslBinaryFunction.Distance,
                new Vector4Float((Vector2Float)center, Vector2Float.Zero),
                new ScenePositionEffect(deviceContext, ScenePositionFormat.XY00));

            // Normalize [0,sourceRadius] to [0,1]
            perPixelRadius = new HlslBinaryOperatorEffect(
                deviceContext,
                perPixelRadius,
                HlslBinaryOperator.Multiply,
                new Vector4Float((float)(1.0 / ((double)blurRadiusRampLength * sourceRadius))));

            if (reversed)
            {
                perPixelRadius = new HlslBinaryOperatorEffect(
                    deviceContext,
                    new Vector4Float(1),
                    HlslBinaryOperator.Subtract,
                    perPixelRadius);
            }

            // Subtract fovea
            perPixelRadius = new HlslBinaryOperatorEffect(
                deviceContext,
                perPixelRadius,
                HlslBinaryOperator.Subtract,
                new Vector4Float(fovea));

            // Divide by fovea. So if fovea is 0.2, the previous operations would leave us with values in the range [-0.2, +0.8]
            // We want to stretch the [0, 0.8] range to [0, 1]
            perPixelRadius = new HlslBinaryOperatorEffect(
                deviceContext,
                perPixelRadius,
                HlslBinaryOperator.Multiply,
                new Vector4Float(1.0f / (fovea == 1.0f ? 1.0f : (1.0f - fovea))));

            // Saturate to [0,1]
            perPixelRadius = new HlslUnaryFunctionEffect(
                deviceContext,
                HlslUnaryFunction.Saturate,
                perPixelRadius);

            // Apply the exponent
            if (blurRadiusRampExponent != 1.0f)
            {
                perPixelRadius = new HlslBinaryFunctionEffect(
                    deviceContext,
                    HlslBinaryFunction.Pow,
                    perPixelRadius,
                    new Vector4Float(blurRadiusRampExponent));
            }

            // Finally, multiply by the blur radius
            perPixelRadius = new HlslBinaryOperatorEffect(
                deviceContext,
                perPixelRadius,
                HlslBinaryOperator.Multiply,
                new Vector4Float(blurValue));
        }

        IDeviceImage sourceImage = Environment.SourceImage;
        {
            ConvertGammaEffect gammaEffect = new ConvertGammaEffect(deviceContext);
            gammaEffect.Properties.Input.Set(sourceImage);
            gammaEffect.Properties.Mode.SetValue(ConvertGammaMode.CustomExponent);
            gammaEffect.Properties.CustomExponent.SetValue(gamma);

            sourceImage = gammaEffect;
        }

        IDeviceEffect blurEffect;
        switch (blurType)
        {
            case BlurTypeEnum.Gaussian:
                GaussianBlurMapOptimization optimization = quality switch
                {
                    <= 1 => GaussianBlurMapOptimization.Speed,
                    <= 2 => GaussianBlurMapOptimization.Balanced,
                    <= 5 => GaussianBlurMapOptimization.Quality,
                    <= 8 => GaussianBlurMapOptimization.HighQuality,
                    _    => GaussianBlurMapOptimization.HighQuality
                };

                GaussianBlurMapEffect gaussianBlur = new GaussianBlurMapEffect(deviceContext);
                gaussianBlur.Properties.StandardDeviationMap.Set(perPixelRadius);
                gaussianBlur.Properties.StandardDeviationLevels.SetValue(tessellationQuality);
                gaussianBlur.Properties.MaxStandardDeviation.SetValue(blurValue);
                gaussianBlur.Properties.MapIsNormalized.SetValue(false);
                gaussianBlur.Properties.Optimization.SetValue(optimization);
                gaussianBlur.Properties.BorderMode.SetValue(BorderMode.Hard);
                blurEffect = gaussianBlur;
                break;

            case BlurTypeEnum.Bokeh:
                BokehBlurMapEffect bokehBlur = new BokehBlurMapEffect(deviceContext);
                bokehBlur.Properties.RadiusMap.Set(perPixelRadius);
                bokehBlur.Properties.RadiusLevels.SetValue(tessellationQuality);
                bokehBlur.Properties.MaxRadius.SetValue(blurValue);
                bokehBlur.Properties.MapIsNormalized.SetValue(false);
                bokehBlur.Properties.Quality.SetValue(quality);
                bokehBlur.Properties.EdgeMode.SetValue(BokehBlurMapEdgeMode.Mirror);
                blurEffect = bokehBlur;
                break;

            default:
                throw new InternalErrorException();
        }

        blurEffect.SetInput(0, sourceImage);

        IDeviceImage outputImage = blurEffect;
        {
            ConvertGammaEffect invGammaEffect = new ConvertGammaEffect(deviceContext);
            invGammaEffect.Properties.Input.Set(outputImage);
            invGammaEffect.Properties.Mode.SetValue(ConvertGammaMode.InverseCustomExponent);
            invGammaEffect.Properties.CustomExponent.SetValue(gamma);

            outputImage = invGammaEffect;
        }

        return outputImage;
    }
}


using PaintDotNet;
using PaintDotNet.ComponentModel;
using PaintDotNet.Direct2D1;
using PaintDotNet.Direct2D1.Effects;
using PaintDotNet.DirectWrite;
using PaintDotNet.Effects;
using PaintDotNet.Effects.Gpu;
using PaintDotNet.Imaging;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;
using PaintDotNet.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PaintDotNet.Effects.Samples.Gpu;

// A more advanced effect that draws text into a command list, then puts a drop shadow behind it.
// This sample shows how to:
// * Get the list of font names from DirectWrite, and use it to populate an IndirectUI dropdown list control
// * Create text format and text layout objects, and then create a geometry of the text outline
// * Use that outline geometry to draw text with an outline (fill the geometry outline with a white brush, then draw the geometry outline with a stroke width)
// * Use a Direct2D command list to store the commands for drawing the text
// * Use the command list as an input to an effect graph
// * Use the ShadowEffect and CompositeEffect to apply a drop shadow to the text
// * Use the CompositeEffect so as to preserve the original layer's contents
internal sealed class OutlinedTextWithShadowGpuEffect
    : PropertyBasedGpuImageEffect
{
    public OutlinedTextWithShadowGpuEffect()
        : base(
            "Outlined Text with Shadow",
            "PDN v5.1 Samples (GPU)",
            GpuImageEffectOptions.Create() with
            {
                IsConfigurable = true
            })
    {
    }

    private enum PropertyNames
    {
        Text,
        FontSize,
        FontName,
        OutlineThickness,
        RotationAngle,
        ShadowBlurRadius
    }

    protected override PropertyCollection OnCreatePropertyCollection()
    {
        List<Property> properties = new List<Property>();

        properties.Add(new StringProperty(PropertyNames.Text, "Outlined text, with a drop shadow, using Direct2D"));

        IDirectWriteFactory dwFactory = this.Environment.DirectWriteFactory;
        using IGdiFontMap fontMap = dwFactory.GetGdiFontMap();

        string[] fontNames = fontMap.ToArray();
        Array.Sort(fontNames, StringComparer.CurrentCultureIgnoreCase);
        int defaultFontIndex = Array.FindIndex(fontNames, s => s.Equals("Calibri", StringComparison.InvariantCultureIgnoreCase));
        if (defaultFontIndex == -1)
        {
            defaultFontIndex = Array.FindIndex(fontNames, s => s.Equals("Arial", StringComparison.InvariantCultureIgnoreCase));
        }
        if (defaultFontIndex == -1)
        {
            defaultFontIndex = 0;
        }

        properties.Add(new StaticListChoiceProperty(PropertyNames.FontName, fontNames, defaultFontIndex));

        properties.Add(new Int32Property(PropertyNames.FontSize, 100, 8, 500));
        properties.Add(new Int32Property(PropertyNames.OutlineThickness, 4, 1, 20));
        properties.Add(new DoubleProperty(PropertyNames.RotationAngle, 0, -180.0, +180.0));
        properties.Add(new Int32Property(PropertyNames.ShadowBlurRadius, 4, 0, 100));

        return new PropertyCollection(properties);
    }

    protected override ControlInfo OnCreateConfigUI(PropertyCollection props)
    {
        ControlInfo configUI = CreateDefaultConfigUI(props);

        configUI.SetPropertyControlValue(PropertyNames.Text, ControlInfoPropertyNames.Multiline, true);
        configUI.SetPropertyControlType(PropertyNames.FontName, PropertyControlType.DropDown);
        configUI.SetPropertyControlType(PropertyNames.RotationAngle, PropertyControlType.AngleChooser);

        return configUI;
    }

    protected override IDeviceImage OnCreateOutput(IDeviceContext deviceContext)
    {
        SizeInt32 size = this.Environment.Document.Size;
        string text = this.Token.GetProperty<StringProperty>(PropertyNames.Text)!.Value;
        string fontName = (string)this.Token.GetProperty<StaticListChoiceProperty>(PropertyNames.FontName)!.Value;
        int fontSize = this.Token.GetProperty<Int32Property>(PropertyNames.FontSize)!.Value;
        int outlineThickness = this.Token.GetProperty<Int32Property>(PropertyNames.OutlineThickness)!.Value;
        double rotationAngle = this.Token.GetProperty<DoubleProperty>(PropertyNames.RotationAngle)!.Value;
        int shadowBlurRadius = this.Token.GetProperty<Int32Property>(PropertyNames.ShadowBlurRadius)!.Value;

        IDirect2DFactory d2dFactory = this.Environment.Direct2DFactory;
        IDirectWriteFactory dwFactory = this.Environment.DirectWriteFactory;

        ITextFormat textFormat = dwFactory.CreateTextFormat(
            fontName,
            null, 
            FontWeight.Normal, 
            FontStyle.Normal, 
            FontStretch.Normal, 
            fontSize);

        ITextLayout textLayout = dwFactory.CreateTextLayout(text, textFormat, size.Width, size.Height);
        textLayout.ParagraphAlignment = ParagraphAlignment.Center;
        textLayout.TextAlignment = TextAlignment.Center;

        IGeometry textGeometry = d2dFactory.CreateGeometryFromTextLayout(textLayout, Point2Float.Zero);

        ICommandList textImage = deviceContext.CreateCommandList();
        using (deviceContext.UseTarget(textImage))
        using (deviceContext.UseBeginDraw())
        {
            ISolidColorBrush blackBrush = deviceContext.CreateSolidColorBrush(LinearColors.Black);
            ISolidColorBrush whiteBrush = deviceContext.CreateSolidColorBrush(LinearColors.White);

            Point2Float centerPoint = new Point2Float(size.Width / 2.0f, size.Height / 2.0f);
            using (deviceContext.UseTransform(Matrix3x2Float.RotationAt((float)-rotationAngle, centerPoint)))
            {
                deviceContext.FillGeometry(textGeometry, whiteBrush);
                deviceContext.DrawGeometry(textGeometry, blackBrush, outlineThickness);
            }
        }
        textImage.Close();

        ShadowEffect shadowEffect = new ShadowEffect(deviceContext);
        shadowEffect.Properties.Input.Set(textImage);
        shadowEffect.Properties.Optimization.SetValue(ShadowOptimization.Quality);
        shadowEffect.Properties.BlurStandardDeviation.SetValue(StandardDeviation.FromRadius(shadowBlurRadius));

        CompositeEffect compositeEffect = new CompositeEffect(deviceContext);
        compositeEffect.Properties.Mode.SetValue(CompositeMode.SourceOver);
        compositeEffect.Properties.Destination.Set(this.Environment.SourceImage); // use original layer contents as background
        compositeEffect.Properties.Sources.Add(shadowEffect);
        compositeEffect.Properties.Sources.Add(textImage);

        return compositeEffect;
    }
}
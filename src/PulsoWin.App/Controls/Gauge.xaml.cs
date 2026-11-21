using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace PulsoWin.App.Controls;

/// <summary>
/// Dial circular con aguja animada. <see cref="Value"/>/<see cref="Maximum"/> definen la posición (0..1);
/// la aguja y el arco de valor siguen a <see cref="Displayed"/>, que se anima con suavizado (CubicEase, 0,6 s: termina antes del siguiente refresco de 1 s)
/// para que la aguja no salte. Con <see cref="UseThresholdColors"/> el arco es verde, amarillo (&gt;70 %) o rojo (&gt;85 %).
/// </summary>
public partial class Gauge : UserControl
{
    private const double StartAngle = -120, Sweep = 240, CenterX = 100, CenterY = 108, Radius = 80;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(Gauge), new PropertyMetadata(0d, (d, _) => ((Gauge)d).AnimateTo()));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Gauge), new PropertyMetadata(100d, (d, _) => ((Gauge)d).AnimateTo()));
    public static readonly DependencyProperty DisplayedProperty = DependencyProperty.Register(
        nameof(Displayed), typeof(double), typeof(Gauge), new PropertyMetadata(0d, (d, _) => ((Gauge)d).Redraw()));
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(Gauge), new PropertyMetadata("", (d, e) => ((Gauge)d).TitleText.Text = (string)e.NewValue));
    public static readonly DependencyProperty ValueTextProperty = DependencyProperty.Register(
        nameof(ValueText), typeof(string), typeof(Gauge), new PropertyMetadata("", (d, e) => ((Gauge)d).ValueTextBlock.Text = (string)e.NewValue));
    public static readonly DependencyProperty SubTextProperty = DependencyProperty.Register(
        nameof(SubText), typeof(string), typeof(Gauge), new PropertyMetadata("", (d, e) => ((Gauge)d).SubTextBlock.Text = (string)e.NewValue));
    public static readonly DependencyProperty ScaleTextProperty = DependencyProperty.Register(
        nameof(ScaleText), typeof(string), typeof(Gauge), new PropertyMetadata("", (d, e) => ((Gauge)d).ScaleTextBlock.Text = (string)e.NewValue));
    public static readonly DependencyProperty UseThresholdColorsProperty = DependencyProperty.Register(
        nameof(UseThresholdColors), typeof(bool), typeof(Gauge), new PropertyMetadata(true, (d, _) => ((Gauge)d).Redraw()));

    private static readonly Color Green = Color.FromRgb(0x39, 0xFF, 0x88);
    private static readonly Color Yellow = Color.FromRgb(0xFF, 0xC9, 0x3C);
    private static readonly Color Red = Color.FromRgb(0xFF, 0x4D, 0x6D);
    private static readonly Color Cyan = Color.FromRgb(0x2D, 0xE2, 0xE6);

    public Gauge()
    {
        InitializeComponent();
        Track.Data = Arc(0, 1);
        DrawTicks();
        Redraw();
    }

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    /// <summary>Fracción 0..1 que se está dibujando (animada).</summary>
    public double Displayed { get => (double)GetValue(DisplayedProperty); set => SetValue(DisplayedProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string ValueText { get => (string)GetValue(ValueTextProperty); set => SetValue(ValueTextProperty, value); }
    public string SubText { get => (string)GetValue(SubTextProperty); set => SetValue(SubTextProperty, value); }
    public string ScaleText { get => (string)GetValue(ScaleTextProperty); set => SetValue(ScaleTextProperty, value); }
    public bool UseThresholdColors { get => (bool)GetValue(UseThresholdColorsProperty); set => SetValue(UseThresholdColorsProperty, value); }

    /// <summary>Fracción 0..1 del valor actual (público para pruebas visuales).</summary>
    public static double Fraction(double value, double maximum) =>
        maximum <= 0 || double.IsNaN(value) ? 0 : Math.Clamp(value / maximum, 0, 1);

    private void AnimateTo()
    {
        var target = Fraction(Value, Maximum);
        if (!SystemParameters.ClientAreaAnimation)
        {
            BeginAnimation(DisplayedProperty, null);
            Displayed = target;
            return;
        }
        var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(600))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        BeginAnimation(DisplayedProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void Redraw()
    {
        var f = Math.Clamp(Displayed, 0, 1);
        NeedleRotation.Angle = StartAngle + Sweep * f;
        ValueArc.Data = f <= 0.001 ? null : Arc(0, f);

        var color = !UseThresholdColors ? Cyan : f > 0.85 ? Red : f > 0.70 ? Yellow : Green;
        ValueArc.Stroke = new SolidColorBrush(color);
        ArcGlow.Color = color;
    }

    private static Point PointAt(double fraction, double radius)
    {
        var rad = (StartAngle + Sweep * fraction) * Math.PI / 180;
        return new Point(CenterX + radius * Math.Sin(rad), CenterY - radius * Math.Cos(rad));
    }

    private static Geometry Arc(double from, double to)
    {
        var figure = new PathFigure { StartPoint = PointAt(from, Radius), IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(PointAt(to, Radius), new Size(Radius, Radius), 0,
            Sweep * (to - from) > 180, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    private void DrawTicks()
    {
        for (var i = 0; i <= 10; i++)
        {
            var f = i / 10.0;
            var major = i % 5 == 0;
            var outer = PointAt(f, Radius - 10);
            var inner = PointAt(f, Radius - (major ? 22 : 16));
            Ticks.Children.Add(new Line
            {
                X1 = outer.X, Y1 = outer.Y, X2 = inner.X, Y2 = inner.Y,
                Stroke = new SolidColorBrush(major ? Color.FromRgb(0xA7, 0x9E, 0xD6) : Color.FromRgb(0x4A, 0x3F, 0x86)),
                StrokeThickness = major ? 2.5 : 1.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            });
        }
    }
}

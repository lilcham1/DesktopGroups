using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace DesktopGroups;

/// <summary>The animated decorations (falling petals, sparkles) drawn on a canvas behind the panel's icons. They live only while the panel is open.</summary>
static class PanelEffects
{
    const int PetalCount = 14;
    const int SparkleCount = 16;
    const int FrameRate = 30;

    static readonly Geometry Star = Geometry.Parse("M5,0 L6,4 L10,5 L6,6 L5,10 L4,6 L0,5 L4,4 Z");

    /// <summary>Replaces whatever runs on <paramref name="layer"/> with <paramref name="effect"/>, sized to <paramref name="area"/>.</summary>
    public static void Run(Canvas layer, PanelEffect effect, Size area, bool lightText)
    {
        layer.Children.Clear(); // removed elements stop animating
        switch (effect)
        {
            case PanelEffect.Petals:
                for (var i = 0; i < PetalCount; i++)
                    layer.Children.Add(Petal(area));
                break;
            case PanelEffect.Sparkles:
                var color = lightText ? Colors.White : Color.FromRgb(0xFF, 0xC9, 0x4D);
                for (var i = 0; i < SparkleCount; i++)
                    layer.Children.Add(Sparkle(area, color));
                break;
        }
    }

    /// <summary>Falls top to bottom while swaying and spinning; a negative start time spreads petals over the panel from the first frame.</summary>
    static UIElement Petal(Size area)
    {
        var spin = new RotateTransform(Random(0, 360));
        var petal = new Ellipse
        {
            Width = Random(7, 11),
            Height = Random(5, 8),
            Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xA8, 0xC2)),
            Opacity = Random(0.6, 0.95),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = spin,
        };

        var fallSeconds = Random(5, 10);
        petal.BeginAnimation(Canvas.TopProperty, Forever(new DoubleAnimation(-12, area.Height + 12, Seconds(fallSeconds)), Random(0, fallSeconds)));

        var x = Random(0, area.Width);
        var swaySeconds = Random(1.5, 3);
        var sway = new DoubleAnimation(x - 12, x + 12, Seconds(swaySeconds)) { AutoReverse = true, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        petal.BeginAnimation(Canvas.LeftProperty, Forever(sway, Random(0, swaySeconds)));

        var spinSeconds = Random(3, 6);
        spin.BeginAnimation(RotateTransform.AngleProperty, Forever(new DoubleAnimation(0, 360, Seconds(spinSeconds)), Random(0, spinSeconds)));
        return petal;
    }

    /// <summary>Twinkles in place: fades and grows in, then out.</summary>
    static UIElement Sparkle(Size area, Color color)
    {
        var scale = new ScaleTransform();
        var size = Random(6, 11);
        var sparkle = new Path
        {
            Data = Star,
            Fill = new SolidColorBrush(color),
            Stretch = Stretch.Uniform,
            Width = size,
            Height = size,
            Opacity = 0,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scale,
        };
        Canvas.SetLeft(sparkle, Random(0, area.Width - size));
        Canvas.SetTop(sparkle, Random(0, area.Height - size));

        var seconds = Random(0.8, 1.6);
        var start = Random(0, 3);
        sparkle.BeginAnimation(UIElement.OpacityProperty, Forever(new DoubleAnimation(0, 1, Seconds(seconds)) { AutoReverse = true }, start));
        var grow = Forever(new DoubleAnimation(0.4, 1, Seconds(seconds)) { AutoReverse = true }, start);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        return sparkle;
    }

    /// <summary>
    /// Repeats forever, already <paramref name="offsetSeconds"/> into its cycle, at <see cref="FrameRate"/>:
    /// every frame redraws the whole see-through panel, and drifting petals look the same at half of WPF's default 60.
    /// </summary>
    static DoubleAnimation Forever(DoubleAnimation animation, double offsetSeconds)
    {
        animation.RepeatBehavior = RepeatBehavior.Forever;
        animation.BeginTime = TimeSpan.FromSeconds(-offsetSeconds);
        Timeline.SetDesiredFrameRate(animation, FrameRate);
        return animation;
    }

    static Duration Seconds(double seconds) => new(TimeSpan.FromSeconds(seconds));

    static double Random(double min, double max) => min + System.Random.Shared.NextDouble() * (max - min);
}

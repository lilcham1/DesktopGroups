using System.IO;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace DesktopGroups;

public enum Theme { Light, Dark, Frosted, Tinted, Sakura, NeonTokyo, Pastel, Image }
public enum ImageDim { None, Light, Medium, Strong }
public enum PanelEffect { None, Petals, Sparkles }
public enum PanelPlacement { Right, Left, Below, Above }
public enum IconScale { Small, Medium, Large }
public enum ViewMode { Grid, List }
public enum Spacing { Compact, Normal, Roomy }
public enum Corners { Small, Medium, Large }
public enum NamePosition { Above, Below, Hidden }

/// <summary>
/// A group's look, chosen from the panel's Style menu and saved in groups.json.
/// The [JsonIgnore] members derive concrete sizes, colors, and brushes from the choices; they are the only place those values live.
/// </summary>
public sealed record GroupStyle(
    Theme Theme = Theme.NeonTokyo,
    string Tint = "#378ADD",
    string? Image = null, // file name of the imported background, inside GroupStore.Root
    ImageDim ImageDim = ImageDim.Medium,
    PanelEffect Effect = PanelEffect.None,
    PanelPlacement Placement = PanelPlacement.Right, // where the panel opens relative to the group's desktop icon
    IconScale IconSize = IconScale.Small,
    ViewMode View = ViewMode.Grid,
    Spacing Spacing = Spacing.Compact,
    Corners Corners = Corners.Large,
    NamePosition NamePosition = NamePosition.Above)
{
    [JsonIgnore]
    public double IconPixels => IconSize switch { IconScale.Small => 32, IconScale.Medium => 48, _ => 64 };

    /// <summary>Width of one grid cell, DIPs.</summary>
    [JsonIgnore]
    public double CellWidth => IconSize switch { IconScale.Small => 68, IconScale.Medium => 84, _ => 104 };

    [JsonIgnore]
    public double ItemMargin => Spacing switch { Spacing.Compact => 0, Spacing.Normal => 2, _ => 6 };

    [JsonIgnore]
    public double PanelPadding => Spacing switch { Spacing.Compact => 8, Spacing.Normal => 16, _ => 24 };

    [JsonIgnore]
    public double PanelRadius => Corners switch { Corners.Small => 12, Corners.Medium => 20, _ => 28 };

    /// <summary>Corner radius on the 256px desktop tile.</summary>
    [JsonIgnore]
    public double TileRadius => Corners switch { Corners.Small => 28, Corners.Medium => 44, _ => 60 };

    /// <summary>The panel's background.</summary>
    [JsonIgnore]
    public Brush Fill => Theme switch
    {
        Theme.Light => Solid(Color.FromArgb(0xEB, 0xF4, 0xF4, 0xF7)),
        Theme.Dark => Solid(Color.FromArgb(0xE6, 0x1C, 0x1C, 0x1E)),
        Theme.Frosted => Solid(Color.FromArgb(0x73, 0xF4, 0xF4, 0xF7)), // tint over the blurred screen snapshot
        Theme.Tinted => Solid(WithAlpha(TintColor, 0xE6)),
        Theme.Sakura => Gradient("#F2FFE6EE", "#F2FFC2D4"),
        Theme.NeonTokyo => Gradient("#F20D0F2B", "#F21E0B3A"),
        Theme.Pastel => Gradient("#F2E6E0FF", "#F2DDF6EE", "#F2FFE6D5"),
        _ => ImageFill(),
    };

    /// <summary>The desktop tile's background: the plain themes are a little more see-through than the panel.</summary>
    [JsonIgnore]
    public Brush TileFill => Theme switch
    {
        Theme.Light => Solid(Color.FromArgb(0xD9, 0xF4, 0xF4, 0xF7)),
        Theme.Dark => Solid(Color.FromArgb(0xD9, 0x1C, 0x1C, 0x1E)),
        Theme.Frosted => Solid(Color.FromArgb(0xA6, 0xE8, 0xE8, 0xEC)),
        Theme.Tinted => Solid(WithAlpha(TintColor, 0xD9)),
        _ => Fill,
    };

    /// <summary>Darkening layer over a background image, so item names stay readable.</summary>
    [JsonIgnore]
    public Brush? Overlay => Theme != Theme.Image ? null : ImageDim switch
    {
        ImageDim.None => null,
        ImageDim.Light => Solid(Color.FromArgb(0x40, 0, 0, 0)),
        ImageDim.Medium => Solid(Color.FromArgb(0x73, 0, 0, 0)),
        _ => Solid(Color.FromArgb(0xA6, 0, 0, 0)),
    };

    /// <summary>Neon Tokyo's glowing edge.</summary>
    [JsonIgnore]
    public Brush? Stroke => Theme == Theme.NeonTokyo ? Gradient("#FFFF2BD9", "#FF22E4FF") : null;

    [JsonIgnore]
    public Effect? Shadow => Theme switch
    {
        Theme.Frosted => null, // a shadow would show through the thin frosted tint
        Theme.NeonTokyo => Frozen(new DropShadowEffect { Color = Color.FromRgb(0xFF, 0x2B, 0xD9), BlurRadius = 22, ShadowDepth = 0, Opacity = 0.85 }),
        _ => Frozen(new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.35 }),
    };

    /// <summary>Sakura's few static petals in the bottom-right corner of the panel.</summary>
    [JsonIgnore]
    public Brush? Decoration => Theme == Theme.Sakura ? SakuraCorner() : null;

    [JsonIgnore]
    public Color Text => Theme switch
    {
        Theme.Light or Theme.Frosted => Color.FromRgb(0x1C, 0x1C, 0x1E),
        Theme.Dark or Theme.NeonTokyo or Theme.Image => Colors.White,
        Theme.Tinted => 0.2126 * TintColor.R + 0.7152 * TintColor.G + 0.0722 * TintColor.B > 140 ? Color.FromRgb(0x1C, 0x1C, 0x1E) : Colors.White,
        Theme.Sakura => Color.FromRgb(0x5A, 0x23, 0x40),
        _ => Color.FromRgb(0x3A, 0x35, 0x50), // Pastel
    };

    [JsonIgnore]
    public bool HasLightText => Text == Colors.White;

    [JsonIgnore]
    public Color Hover => HasLightText ? Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x14, 0, 0, 0);

    Color TintColor => (Color)ColorConverter.ConvertFromString(Tint);

    Brush ImageFill()
    {
        var file = Path.Combine(GroupStore.Root, Image ?? throw new InvalidDataException("Image theme without an image file."));
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(file);
        bitmap.CacheOption = BitmapCacheOption.OnLoad; // read now, don't keep the file open
        bitmap.DecodePixelWidth = 900;
        bitmap.EndInit();
        bitmap.Freeze();
        return Frozen(new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill });
    }

    static Brush SakuraCorner()
    {
        var petals = new DrawingGroup();
        var pink = Solid(Color.FromArgb(0x80, 0xFF, 0x8F, 0xB1));
        (double X, double Y, double Angle, double Size)[] layout = [(20, 50, 20, 1), (48, 30, -35, 0.8), (70, 58, 60, 1.1), (92, 24, 10, 0.7), (104, 52, -60, 0.9)];
        foreach (var (x, y, angle, size) in layout)
        {
            var petal = new EllipseGeometry(new Point(x, y), 7 * size, 4.5 * size) { Transform = new RotateTransform(angle, x, y) };
            petals.Children.Add(new GeometryDrawing(pink, null, petal));
        }
        petals.Children.Add(new GeometryDrawing(null, null, new RectangleGeometry(new Rect(0, 0, 120, 80)))); // fixes the drawing's bounds
        return Frozen(new DrawingBrush(petals) { Stretch = Stretch.None, AlignmentX = AlignmentX.Right, AlignmentY = AlignmentY.Bottom });
    }

    static Brush Solid(Color color) => Frozen(new SolidColorBrush(color));

    /// <summary>A diagonal gradient through evenly spaced colors.</summary>
    static Brush Gradient(params string[] colors)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        for (var i = 0; i < colors.Length; i++)
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(colors[i]), (double)i / (colors.Length - 1)));
        return Frozen(brush);
    }

    static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

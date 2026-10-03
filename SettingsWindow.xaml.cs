using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using static DesktopGroups.NativeMethods;
using Forms = System.Windows.Forms;

namespace DesktopGroups;

/// <summary>
/// Everything about one group in one place: name, look, layout, where its panel opens, and deleting it.
/// Every change applies to the open panel and the desktop tile immediately.
/// </summary>
public partial class SettingsWindow : Window
{
    static readonly (string Name, string Hex)[] Tints =
    [
        ("Blue", "#378ADD"), ("Teal", "#1D9E75"), ("Coral", "#D85A30"),
        ("Pink", "#D4537E"), ("Purple", "#7F77DD"), ("Green", "#639922"),
    ];

    readonly GroupPanel _panel;
    readonly List<(RadioButton Chip, Func<GroupStyle, bool> IsSelected)> _chips = [];
    readonly List<(FrameworkElement Row, Func<GroupStyle, bool> IsShown)> _conditionalRows = [];

    /// <summary>Set when the user chose Delete; the panel deletes the group after this window closes.</summary>
    public bool DeleteRequested { get; private set; }

    public SettingsWindow(GroupPanel panel, Rect workArea)
    {
        InitializeComponent();
        _panel = panel;
        Owner = panel;
        Title = $"{panel.Group} settings";
        NameBox.Text = panel.Group;

        AddRow("Theme",
            ThemeChip("Neon Tokyo", Theme.NeonTokyo), ThemeChip("Sakura", Theme.Sakura), ThemeChip("Pastel", Theme.Pastel),
            ThemeChip("Light", Theme.Light), ThemeChip("Dark", Theme.Dark), ThemeChip("Frosted glass", Theme.Frosted),
            ThemeChip("Tinted", Theme.Tinted),
            Chip("Your image", style => style.Theme == Theme.Image,
                style => style.Image == null ? PickImage(style) : style with { Theme = Theme.Image }));
        AddRow("Color", style => style.Theme == Theme.Tinted,
        [
            .. Tints.Select(tint => Chip(Swatch(tint.Hex), style => style.Tint == tint.Hex, style => style with { Tint = tint.Hex })),
            Chip("Custom…", style => Tints.All(tint => tint.Hex != style.Tint), PickCustomTint),
        ]);
        AddRow("Image", style => style.Theme == Theme.Image, ActionButton("Choose image…", () => Apply(PickImage(_panel.CurrentStyle))));
        AddRow("Image dim", style => style.Theme == Theme.Image,
            Options(style => style.ImageDim, (style, value) => style with { ImageDim = value },
                ("None", ImageDim.None), ("Light", ImageDim.Light), ("Medium", ImageDim.Medium), ("Strong", ImageDim.Strong)));
        AddRow("Effects", Options(style => style.Effect, (style, value) => style with { Effect = value },
            ("None", PanelEffect.None), ("Falling petals", PanelEffect.Petals), ("Sparkles", PanelEffect.Sparkles)));
        AddRow("Opens", Options(style => style.Placement, (style, value) => style with { Placement = value },
            ("Right of icon", PanelPlacement.Right), ("Left", PanelPlacement.Left), ("Below", PanelPlacement.Below), ("Above", PanelPlacement.Above)));
        AddRow("Icon size", Options(style => style.IconSize, (style, value) => style with { IconSize = value },
            ("Small", IconScale.Small), ("Medium", IconScale.Medium), ("Large", IconScale.Large)));
        AddRow("View", Options(style => style.View, (style, value) => style with { View = value },
            ("Grid", ViewMode.Grid), ("List", ViewMode.List)));
        AddRow("Spacing", Options(style => style.Spacing, (style, value) => style with { Spacing = value },
            ("Compact", Spacing.Compact), ("Normal", Spacing.Normal), ("Roomy", Spacing.Roomy)));
        AddRow("Corners", Options(style => style.Corners, (style, value) => style with { Corners = value },
            ("Small", Corners.Small), ("Medium", Corners.Medium), ("Large", Corners.Large)));
        AddRow("Group name", Options(style => style.NamePosition, (style, value) => style with { NamePosition = value },
            ("Above", NamePosition.Above), ("Below", NamePosition.Below), ("Hidden", NamePosition.Hidden)));
        Refresh();

        SourceInitialized += (_, _) =>
        {
            var dark = 1;
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        };
        Loaded += (_, _) => PlaceBesidePanel(workArea);
    }

    /// <summary>To the right of the panel if it fits, else to its left, so the live preview stays visible.</summary>
    void PlaceBesidePanel(Rect workArea)
    {
        var right = _panel.Left + _panel.ActualWidth;
        Left = right + ActualWidth <= workArea.Right ? right : Math.Max(workArea.Left, _panel.Left - ActualWidth);
        Top = Math.Clamp(_panel.Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - ActualHeight));
    }

    void AddRow(string label, params UIElement[] options) => AddRow(label, null, options);

    void AddRow(string label, Func<GroupStyle, bool>? isShown, params UIElement[] options)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new TextBlock { Text = label, Foreground = (Brush)new BrushConverter().ConvertFrom("#9A9CB8")!, Margin = new Thickness(0, 4, 0, 0) });

        var wrap = new WrapPanel();
        foreach (var option in options)
            wrap.Children.Add(option);
        Grid.SetColumn(wrap, 1);
        grid.Children.Add(wrap);

        Rows.Children.Add(grid);
        if (isShown != null)
            _conditionalRows.Add((grid, isShown));
    }

    UIElement[] Options<T>(Func<GroupStyle, T> get, Func<GroupStyle, T, GroupStyle> set, params (string Label, T Value)[] options) where T : struct, Enum =>
        options.Select(option => (UIElement)Chip(option.Label, style => get(style).Equals(option.Value), style => set(style, option.Value))).ToArray();

    RadioButton ThemeChip(string label, Theme theme) => Chip(label, style => style.Theme == theme, style => style with { Theme = theme });

    /// <summary>A selectable option. <paramref name="apply"/> returns the new style, or null if the user backed out of a dialog.</summary>
    RadioButton Chip(object content, Func<GroupStyle, bool> isSelected, Func<GroupStyle, GroupStyle?> apply)
    {
        var chip = new RadioButton { Content = content, Style = (Style)Resources["Chip"] };
        chip.Click += (_, _) => Apply(apply(_panel.CurrentStyle));
        _chips.Add((chip, isSelected));
        return chip;
    }

    Button ActionButton(string text, Action onClick)
    {
        var button = new Button { Content = text, Style = (Style)Resources["Action"] };
        button.Click += (_, _) => onClick();
        return button;
    }

    static Ellipse Swatch(string hex) => new() { Width = 16, Height = 16, Fill = (Brush)new BrushConverter().ConvertFrom(hex)! };

    void Apply(GroupStyle? style)
    {
        if (style != null)
            _panel.ChangeStyle(style);
        Refresh();
    }

    /// <summary>Marks the chips matching the current style and shows only the rows that apply to it.</summary>
    void Refresh()
    {
        var style = _panel.CurrentStyle;
        foreach (var (chip, isSelected) in _chips)
            chip.IsChecked = isSelected(style);
        foreach (var (row, isShown) in _conditionalRows)
            row.Visibility = isShown(style) ? Visibility.Visible : Visibility.Collapsed;
    }

    GroupStyle? PickImage(GroupStyle style)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a background image",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
        };
        return dialog.ShowDialog(this) == true
            ? style with { Theme = Theme.Image, Image = GroupStyles.ImportImage(dialog.FileName) }
            : null;
    }

    GroupStyle? PickCustomTint(GroupStyle style)
    {
        using var dialog = new Forms.ColorDialog { FullOpen = true, Color = System.Drawing.ColorTranslator.FromHtml(style.Tint) };
        if (dialog.ShowDialog(new Win32Owner(this)) != Forms.DialogResult.OK)
            return null;
        return style with { Tint = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}" };
    }

    sealed class Win32Owner(Window window) : Forms.IWin32Window
    {
        public IntPtr Handle => new WindowInteropHelper(window).Handle;
    }

    void Rename_Click(object sender, RoutedEventArgs e) => Rename();

    void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        e.Handled = true;
        Rename();
    }

    void Rename()
    {
        var problem = _panel.Rename(NameBox.Text.Trim());
        NameProblem.Text = problem ?? "";
        NameProblem.Visibility = problem == null ? Visibility.Collapsed : Visibility.Visible;
        Title = $"{_panel.Group} settings";
    }

    void Delete_Click(object sender, RoutedEventArgs e)
    {
        DeleteRequested = true;
        Close();
    }
}

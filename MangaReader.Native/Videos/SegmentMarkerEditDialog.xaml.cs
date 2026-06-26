using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MangaReader.Native.Videos.Models;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using ColorConverter = System.Windows.Media.ColorConverter;
using Cursors = System.Windows.Input.Cursors;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace MangaReader.Native.Videos;

public partial class SegmentMarkerEditDialog : Window
{
    private static readonly string[] PresetColors =
    [
        "#F97316", // 橙
        "#EF4444", // 红
        "#EAB308", // 黄
        "#22C55E", // 绿
        "#0EA5E9", // 蓝
        "#A855F7", // 紫
        "#94A3B8"  // 灰
    ];

    private readonly VideoSegmentMarker _marker;
    private string _selectedColor;
    private readonly Dictionary<string, Border> _colorSwatches = new();

    public string ResultTitle => TitleBox.Text.Trim();
    public string ResultNote => NoteBox.Text;
    public string ResultColor => _selectedColor;

    public SegmentMarkerEditDialog(VideoSegmentMarker marker)
    {
        InitializeComponent();
        _marker = marker;
        _selectedColor = string.IsNullOrWhiteSpace(marker.Color) ? PresetColors[0] : marker.Color;

        TimeText.Text = $"时间：{marker.TimeText}";
        TitleBox.Text = marker.Title;
        NoteBox.Text = marker.Note;

        BuildColorPanel();
        SelectColor(_selectedColor);

        Loaded += (_, _) =>
        {
            TitleBox.Focus();
            TitleBox.SelectAll();
        };
    }

    private void BuildColorPanel()
    {
        var colors = new List<string>(PresetColors);
        if (!colors.Contains(_selectedColor, StringComparer.OrdinalIgnoreCase))
        {
            colors.Add(_selectedColor);
        }

        foreach (var color in colors)
        {
            var swatch = new Border
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(0, 0, 8, 0),
                CornerRadius = (CornerRadius)FindResource("RadiusTag"),
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Background = HexToBrush(color),
                Tag = color,
                ToolTip = color
            };
            swatch.MouseLeftButtonDown += Swatch_MouseLeftButtonDown;
            _colorSwatches[color] = swatch;
            ColorPanel.Children.Add(swatch);
        }
    }

    private void Swatch_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: string color })
        {
            SelectColor(color);
        }
    }

    private void SelectColor(string color)
    {
        _selectedColor = color;
        foreach (var (key, swatch) in _colorSwatches)
        {
            swatch.BorderBrush = string.Equals(key, color, StringComparison.OrdinalIgnoreCase)
                ? Brushes.White
                : Brushes.Transparent;
        }
    }

    private static SolidColorBrush HexToBrush(string hex)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
        catch
        {
            return new SolidColorBrush(Colors.Orange);
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

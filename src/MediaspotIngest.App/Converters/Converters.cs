using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MediaspotIngest.Core.Domain;

namespace MediaspotIngest.App.Converters;

/// <summary>
/// Status chips in Mediaspot's style: semantic colour as text, its translucent version as fill.
/// FixNeeded = error, Ready = primary, Exported = info, Pending = warning, Done = success.
/// </summary>
public sealed class StatusBrushConverter : IValueConverter
{
    internal static readonly Dictionary<ItemStatus, (Color Main, Color Fill)> Colors = new()
    {
        [ItemStatus.FixNeeded] = (Color.FromRgb(0xED, 0x62, 0x73), Color.FromArgb(0x30, 0xED, 0x62, 0x73)),
        [ItemStatus.Ready] = (Color.FromRgb(0x4C, 0x8B, 0xFF), Color.FromArgb(0x30, 0x2D, 0x6C, 0xE1)),
        [ItemStatus.Exported] = (Color.FromRgb(0x9C, 0x6C, 0xEA), Color.FromArgb(0x30, 0x7E, 0x3F, 0xF5)),
        [ItemStatus.Pending] = (Color.FromRgb(0xFC, 0xA4, 0x64), Color.FromArgb(0x30, 0xFC, 0xA4, 0x64)),
        [ItemStatus.Done] = (Color.FromRgb(0x1F, 0xC1, 0x9A), Color.FromArgb(0x40, 0x1F, 0xC1, 0x9A)),
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        Frozen(value is ItemStatus s && Colors.TryGetValue(s, out var c) ? c.Main : Color.FromRgb(0x99, 0x99, 0xAD));

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

    internal static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

public sealed class StatusFillConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        StatusBrushConverter.Frozen(value is ItemStatus s && StatusBrushConverter.Colors.TryGetValue(s, out var c)
            ? c.Fill
            : Color.FromArgb(0x0F, 0xC2, 0xC2, 0xD6));

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is null || value is string { Length: 0 } || value is 0 ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

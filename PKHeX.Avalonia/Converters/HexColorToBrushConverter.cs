using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PKHeX.Avalonia.Converters;

/// <summary>
/// Turns a framework-free hex color string from a ViewModel (e.g. a Bank wallpaper palette entry,
/// <c>"#4A7FC0"</c>) into a brush. Unparseable or empty values yield <see langword="null"/> (no fill).
/// </summary>
public sealed class HexColorToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string { Length: > 0 } text && Color.TryParse(text, out var color)
            ? new SolidColorBrush(color)
            : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

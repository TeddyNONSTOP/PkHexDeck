using System;
using System.Globalization;
using Avalonia.Data.Converters;
using PKHeX.Application.Abstractions;
using PKHeX.Presentation.Localization;

namespace PKHeX.Avalonia.Converters;

/// <summary>Maps controller input modes to localized settings labels.</summary>
public sealed class GamepadInputModeLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        GamepadInputMode.Automatic => LocalizedStrings.Instance["Settings_GamepadMode_Automatic"],
        GamepadInputMode.On => LocalizedStrings.Instance["Settings_GamepadMode_On"],
        GamepadInputMode.Off => LocalizedStrings.Instance["Settings_GamepadMode_Off"],
        _ => value?.ToString(),
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

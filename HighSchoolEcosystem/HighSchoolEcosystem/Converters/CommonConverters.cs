using System.Globalization;

namespace HighSchoolEcosystem.Converters;

/// <summary>
/// True when the bound string has content. Used to hide empty-state boxes and
/// error labels rather than leaving a blank widget with padding on screen.
///
/// This exists because MAUI has no built-in string-to-bool converter and the
/// alternative — an IsVisible bool alongside every message property — doubles
/// the view model surface for no benefit.
/// </summary>
public sealed class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrWhiteSpace(value as string);

    // One-way only: nothing meaningful to write back from a visibility flag.
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException(
            $"{nameof(StringNotEmptyConverter)} is one-way. Bind IsVisible, not the source string.");
}

/// <summary>
/// Negates a bool, for the "show this when that is false" case — an empty-state
/// box next to a populated list, typically.
/// </summary>
public sealed class InvertedBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;
}

/// <summary>
/// True when a collection is empty — for showing an empty-state box beside a
/// list. Separate from <see cref="HasItemsConverter"/> rather than a parameter
/// on it, because ConverterParameter is a stringly-typed flag that fails
/// silently when misspelled.
/// </summary>
public sealed class NoItemsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not System.Collections.ICollection { Count: > 0 };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(NoItemsConverter)} is one-way.");
}

/// <summary>
/// True when a collection has at least one item. Saves carrying a Has* bool
/// beside every list on a view model.
/// </summary>
public sealed class HasItemsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is System.Collections.ICollection { Count: > 0 };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(HasItemsConverter)} is one-way.");
}

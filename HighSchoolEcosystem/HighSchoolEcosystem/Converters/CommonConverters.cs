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

/// <summary>
/// Turns a collection into a height for a bounded list: as tall as its content,
/// up to a cap, after which it scrolls.
///
/// This exists because of a genuine conflict. A <c>CollectionView</c> inside a
/// <c>ScrollView</c> MUST be given a height — an unbounded one measures to zero
/// and vanishes on mobile. But a fixed height reserves the same space for one
/// exam as for ten, which is a wall of white under a single row.
///
/// So the height is computed instead: rows x rowHeight, capped. Dynamic where it
/// matters, bounded where the layout demands it. Same reasoning as
/// <c>UnitCardRow.FillWidth</c> — MAUI can't express this in XAML, so it's
/// arithmetic in device units.
///
/// ConverterParameter is "rowHeight,maxRows", e.g. "58,4". Both are needed and
/// they differ per list: a grade row carries three lines and a score, a work row
/// carries two. Getting rowHeight wrong doesn't break anything — it just makes
/// the list slightly too tall or scroll a row early.
/// </summary>
public sealed class ListHeightConverter : IValueConverter
{
    // Used when the parameter is missing or malformed. Two lines plus the rule,
    // which is the commonest row in the app.
    private const double DefaultRowHeight = 58;
    private const int DefaultMaxRows = 4;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value is System.Collections.ICollection collection ? collection.Count : 0;

        var (rowHeight, maxRows) = Parse(parameter);

        // Zero rows would collapse the control; the section around it is hidden
        // when empty anyway, so one row's worth is a safe floor and avoids a
        // zero-height CollectionView ever existing.
        var rows = Math.Clamp(count, 1, maxRows);

        return rows * rowHeight;
    }

    private static (double RowHeight, int MaxRows) Parse(object? parameter)
    {
        if (parameter is not string text) return (DefaultRowHeight, DefaultMaxRows);

        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return (DefaultRowHeight, DefaultMaxRows);

        // InvariantCulture: the parameter is a literal in XAML, not user input,
        // so it uses a dot regardless of the device's locale. Parsing it with
        // the current culture would fail on any device set to a comma decimal.
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var rowHeight))
            rowHeight = DefaultRowHeight;

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxRows))
            maxRows = DefaultMaxRows;

        return (rowHeight <= 0 ? DefaultRowHeight : rowHeight,
                maxRows <= 0 ? DefaultMaxRows : maxRows);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(ListHeightConverter)} is one-way.");
}

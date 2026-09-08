namespace HighSchoolEcosystem.Models;

/// <summary>
/// Mirrors /timetable/{entryId} — one recurring weekly period for one class.
///
/// Times are "HH:mm" strings rather than DateTimes: these are wall-clock
/// school times that repeat weekly, so they carry no date and no timezone.
/// Storing them as text keeps them sortable and avoids implying a UTC instant
/// that doesn't exist.
/// </summary>
public sealed class TimetableEntry
{
    public string Id { get; init; } = string.Empty;
    public string ClassId { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public DayOfWeek DayOfWeek { get; init; } = System.DayOfWeek.Monday;

    /// <summary>Period number within the day, 1-based.</summary>
    public int Period { get; init; } = 1;

    public string StartTime { get; init; } = "08:00";
    public string EndTime { get; init; } = "09:00";
    public string Room { get; init; } = string.Empty;
}

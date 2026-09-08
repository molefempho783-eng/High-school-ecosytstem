namespace HighSchoolEcosystem.Models;

/// <summary>
/// Mirrors /classes/{classId}/units/{unitId}. A chapter of the course — "LU 1:
/// Introduction to Usability" — holding the materials a student works through.
///
/// This sits between the class and its materials because a term's worth of
/// content in one flat list is unusable. Materials carry a UnitId; a material
/// with no UnitId is loose and shows in a "Module information" group, which is
/// also where existing material lands after this was introduced.
/// </summary>
public sealed class LearningUnit
{
    public string Id { get; init; } = string.Empty;

    /// <summary>"LU 1: Introduction to Usability and Interface Design".</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// The intro paragraph shown in a tinted box at the top of the unit page.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Position in the course. Sparse and teacher-assigned, so inserting a unit
    /// doesn't rewrite every document after it.
    /// </summary>
    public int Order { get; init; }

    /// <summary>
    /// How many activities the unit contains, as the teacher states it. Not
    /// derived from the material count: a single chapter can carry three
    /// exercises, and the teacher knows the real figure.
    /// </summary>
    public int ActivityCount { get; init; }

    /// <summary>
    /// Expected time on task, in minutes. Rendered as "1 hour and 15 minutes".
    /// Zero hides the line rather than claiming the unit takes no time.
    /// </summary>
    public int TimeOnTaskMinutes { get; init; }

    public string AuthorId { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>"1 hour and 15 minutes", or empty when unset.</summary>
    public string TimeOnTaskLabel
    {
        get
        {
            if (TimeOnTaskMinutes <= 0) return string.Empty;

            var hours = TimeOnTaskMinutes / 60;
            var minutes = TimeOnTaskMinutes % 60;

            var hourPart = hours switch
            {
                0 => string.Empty,
                1 => "1 hour",
                _ => $"{hours} hours"
            };

            var minutePart = minutes switch
            {
                0 => string.Empty,
                1 => "1 minute",
                _ => $"{minutes} minutes"
            };

            if (hourPart.Length == 0) return minutePart;
            if (minutePart.Length == 0) return hourPart;
            return $"{hourPart} and {minutePart}";
        }
    }

    public string ActivityCountLabel => ActivityCount == 1 ? "1 activity" : $"{ActivityCount} activities";
}

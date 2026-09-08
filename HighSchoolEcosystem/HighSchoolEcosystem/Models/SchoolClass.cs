namespace HighSchoolEcosystem.Models;

/// <summary>
/// Mirrors /classes/{classId}. Named SchoolClass rather than Class because
/// `class` is a C# keyword.
///
/// <see cref="TeacherId"/> is the ownership key: the Firestore rules let a
/// teacher create a class only with their own UID here, and only edit classes
/// where it already matches.
/// </summary>
public sealed class SchoolClass
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string TeacherId { get; init; } = string.Empty;
    public string TeacherName { get; init; } = string.Empty;
    public string Room { get; init; } = string.Empty;

    /// <summary>UIDs of enrolled students. Drives read access to the class.</summary>
    public List<string> StudentIds { get; init; } = [];
}

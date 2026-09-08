namespace HighSchoolEcosystem.Models;

/// <summary>
/// Mirrors /classes/{classId}/progress/{studentId}. Which materials this
/// student has marked complete, driving every "1/4 Topics Completed" bar.
///
/// One document per student per class, rather than a completion record per
/// material. Rendering a class page needs the whole picture at once, so one
/// read beats N — and a student ticking a box is one small write either way.
///
/// The document ID is the student's UID, which is what lets the rules say
/// "you may only write your own" without a lookup.
/// </summary>
public sealed class ClassProgress
{
    /// <summary>
    /// IDs of materials marked complete.
    ///
    /// A list of strings because FirestoreValueConverter reads every array back
    /// as List&lt;string&gt; and has no map support. Order is meaningless — treat
    /// it as a set. IDs of deleted materials may linger here; that's harmless,
    /// since progress is always computed against the materials that currently
    /// exist.
    /// </summary>
    public List<string> CompletedMaterialIds { get; init; } = [];

    public DateTimeOffset UpdatedAt { get; init; }

    public bool IsComplete(string materialId)
        => CompletedMaterialIds.Contains(materialId, StringComparer.Ordinal);
}

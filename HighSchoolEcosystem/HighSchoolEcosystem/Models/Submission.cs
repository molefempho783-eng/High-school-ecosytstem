namespace HighSchoolEcosystem.Models;

/// <summary>
/// A student's submission, at
/// /classes/{classId}/assignments/{assignmentId}/submissions/{studentId}.
/// The document ID is the student's UID, so there's exactly one per student per
/// assignment and resubmitting overwrites.
///
/// IMPORTANT — how this is written: the security rules reject any student
/// `create` whose payload contains `Grade` or `Feedback`, and any student
/// `update` that changes them. Because FirestoreValueConverter serialises
/// *every* property, saving this whole object as a student would include those
/// fields and be denied. Student writes therefore go through
/// <c>PatchFieldsAsync</c> with an explicit field list — see
/// <c>ClassService.SubmitAsync</c>. Don't switch it to SetDocumentAsync.
/// </summary>
/// <summary>
/// A submission together with the assignment it belongs to.
///
/// Needed because a Submission document holds no assignment ID — the ID is in
/// its path — so anything reading several at once has no way to pair them up
/// afterwards. Returning the pair makes the join explicit instead of relying on
/// list ordering, which is how it silently breaks.
/// </summary>
public sealed record ClassSubmission(string AssignmentId, Submission Submission);

public sealed class Submission
{
    public string StudentId { get; init; } = string.Empty;

    /// <summary>Denormalised so a teacher marking work doesn't need a user lookup per row.</summary>
    public string StudentName { get; init; } = string.Empty;

    public DateTimeOffset SubmittedAt { get; init; }

    /// <summary>The written answer. Empty for a purely multiple-choice assignment.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Multiple-choice picks, encoded as "questionId\u001foptionText".
    ///
    /// A string list rather than a map because FirestoreValueConverter reads
    /// every array back as List&lt;string&gt; and has no map support.
    ///
    /// Two deliberate choices: keyed by question ID so reordering or deleting a
    /// question can't reassign an answer, and storing the option TEXT rather
    /// than its index so reordering the *options* can't either. The separator is
    /// the ASCII unit separator, which won't occur in typed text — a colon
    /// would collide with answers that contain one.
    /// </summary>
    public List<string> Answers { get; init; } = [];

    private const char Separator = '\u001f';

    /// <summary>
    /// Teacher-assigned mark, or -1 when not yet marked. A sentinel rather than
    /// null because a legitimate mark of 0 has to be distinguishable from
    /// "ungraded", and the converter has no clean nullable-int round trip.
    /// </summary>
    public int Grade { get; init; } = -1;

    public string Feedback { get; init; } = string.Empty;

    /// <summary>
    /// Set by the auto-marking Cloud Function to the SubmittedAt value it
    /// marked, so it can tell a fresh submission from its own write-back and
    /// avoid triggering itself forever. Server-written only — the client never
    /// sends it (see ClassService.SubmitAsync's explicit field list).
    /// </summary>
    public DateTimeOffset AutoMarkedAt { get; init; }

    /// <summary>
    /// Multiple-choice points earned and available, written by the Cloud
    /// Function. -1 means it hasn't run — either the function isn't deployed or
    /// the assignment has no questions.
    /// </summary>
    public int AutoScore { get; init; } = -1;
    public int AutoScoreMax { get; init; } = -1;

    // ---- attached file ----

    /// <summary>
    /// Download URL for the student's uploaded file, or empty. Set only when the
    /// assignment accepts files.
    /// </summary>
    public string FileUrl { get; init; } = string.Empty;

    /// <summary>
    /// The Storage object path, kept so a resubmission can replace the old file
    /// rather than leaving it billable and unreferenced.
    /// </summary>
    public string FileStoragePath { get; init; } = string.Empty;

    /// <summary>Original filename, so a teacher sees what they're opening.</summary>
    public string FileName { get; init; } = string.Empty;

    public long FileSizeBytes { get; init; }

    public bool HasFile => !string.IsNullOrWhiteSpace(FileUrl);

    /// <summary>"report.pdf · 1.4 MB", for the marking list.</summary>
    public string FileLabel
    {
        get
        {
            if (!HasFile) return string.Empty;

            var name = string.IsNullOrWhiteSpace(FileName) ? "Attached file" : FileName;
            if (FileSizeBytes <= 0) return name;

            var size = FileSizeBytes < 1024 * 1024
                ? $"{FileSizeBytes / 1024.0:0.#} KB"
                : $"{FileSizeBytes / (1024.0 * 1024.0):0.#} MB";

            return $"{name} · {size}";
        }
    }

    public bool HasWritten => !string.IsNullOrWhiteSpace(Text);

    /// <summary>
    /// Set when a teacher enters a grade by hand. Distinct from AutoMarkedAt,
    /// which the Cloud Function sets.
    /// </summary>
    public DateTimeOffset TeacherMarkedAt { get; init; }

    public bool IsTeacherMarked => TeacherMarkedAt != default;

    /// <summary>
    /// True when a teacher still has to look at this — there's written work or a
    /// file, and no human has graded it.
    ///
    /// Checks TeacherMarkedAt, NOT IsMarked: the auto-marker sets Grade for the
    /// multiple-choice part, so IsMarked goes true while the written answer
    /// remains unread. That's exactly the case this has to catch.
    /// </summary>
    public bool NeedsTeacherMarking => (HasWritten || HasFile) && !IsTeacherMarked;

    public bool IsMarked => Grade >= 0;

    /// <summary>True once the multiple-choice part has been scored server-side.</summary>
    public bool IsAutoMarked => AutoScoreMax >= 0;

    /// <summary>Reads the chosen option text for a question, or null if unanswered.</summary>
    public string? AnswerFor(string questionId)
    {
        var prefix = questionId + Separator;
        foreach (var entry in Answers)
        {
            if (entry.StartsWith(prefix, StringComparison.Ordinal))
                return entry[prefix.Length..];
        }
        return null;
    }

    public static string Encode(string questionId, string optionText)
        => $"{questionId}{Separator}{optionText}";
}

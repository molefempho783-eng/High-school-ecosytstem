namespace HighSchoolEcosystem.Models;

/// <summary>
/// Distinguishes graded coursework from homework in the same collection.
///
/// Test and Exam are assessments rather than work to hand in: they surface on
/// the class page as upcoming dates and are excluded from the dashboard's
/// "Work to do" list, because a student can't submit them from their phone.
///
/// Append only. The converter writes the enum NAME, so reordering these would
/// silently retype every existing document.
/// </summary>
public enum AssignmentKind
{
    Assignment,
    Homework,
    Test,
    Exam
}

/// <summary>
/// How a student hands this in. The teacher chooses when setting the work.
///
/// Independent of multiple-choice questions: an assignment can have questions
/// AND accept a file, because the questions are marked automatically and the
/// file is the part the teacher reads.
/// </summary>
public enum SubmissionMode
{
    /// <summary>Type an answer in the app. The default, and the old behaviour.</summary>
    Written,

    /// <summary>Attach a file — a scan, a document, a photo of written work.</summary>
    File,

    /// <summary>Either will do. Useful when some pupils have a scanner and some don't.</summary>
    Either,

    /// <summary>
    /// Multiple-choice only. There is nothing to type and nothing to attach —
    /// the paper IS the questions, and the auto-marker does all the marking.
    ///
    /// Appended last, deliberately: the converter writes the enum NAME, so a new
    /// value at the end can't retype any document already saved.
    /// </summary>
    Questions
}

/// <summary>Extension helpers so the "is this submittable?" rule lives in one place.</summary>
public static class AssignmentKindExtensions
{
    /// <summary>
    /// True for work a student hands in. False for tests and exams, which are
    /// written under supervision — showing them a submit box would be wrong.
    /// </summary>
    public static bool IsSubmittable(this AssignmentKind kind)
        => kind is AssignmentKind.Assignment or AssignmentKind.Homework;

    /// <summary>True for a scheduled sit-down assessment.</summary>
    public static bool IsAssessment(this AssignmentKind kind)
        => kind is AssignmentKind.Test or AssignmentKind.Exam;
}

/// <summary>
/// Mirrors /classes/{classId}/assignments/{assignmentId}. Lives under the class
/// so the security rules can key access off class membership without any extra
/// lookup.
/// </summary>
public sealed class Assignment
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public AssignmentKind Kind { get; init; } = AssignmentKind.Assignment;

    /// <summary>Stored as a Firestore timestamp; always held in UTC.</summary>
    public DateTimeOffset DueDate { get; init; } = DateTimeOffset.UtcNow.AddDays(7);

    public int Points { get; init; } = 100;

    /// <summary>
    /// How the student hands it in. Defaults to Written, which is what every
    /// assignment created before this existed will deserialise as — the
    /// converter reads a missing enum as the zero value.
    /// </summary>
    public SubmissionMode SubmissionMode { get; init; } = SubmissionMode.Written;

    public bool AcceptsWritten => SubmissionMode is SubmissionMode.Written or SubmissionMode.Either;
    public bool AcceptsFile => SubmissionMode is SubmissionMode.File or SubmissionMode.Either;

    /// <summary>
    /// True when the questions are the whole paper. The student sees no answer
    /// box and no attach button, and nothing here reaches the marking queue —
    /// the Cloud Function grades it end to end.
    /// </summary>
    public bool IsQuestionsOnly => SubmissionMode is SubmissionMode.Questions;

    /// <summary>Tells the student what's expected, in one line.</summary>
    public string SubmissionHint => SubmissionMode switch
    {
        SubmissionMode.File => "Attach a file to hand this in.",
        SubmissionMode.Either => "Type your answer, attach a file, or both.",
        SubmissionMode.Questions => "Answer the questions above, then hand in.",
        _ => "Type your answer to hand this in."
    };
}

/// <summary>
/// One line in a teacher's marking queue: an assignment with work waiting on a
/// person to read it.
///
/// Carries the class as well as the assignment because the queue is built
/// across every class a teacher owns — "7 waiting" is useless without knowing
/// which room it belongs to.
/// </summary>
public sealed record MarkingQueueItem(
    string ClassId,
    string ClassName,
    string AssignmentId,
    string AssignmentTitle,
    AssignmentKind Kind,
    DateTimeOffset DueDate,
    int Count)
{
    /// <summary>Capped: the badge is a fixed-width circle.</summary>
    public string CountLabel => Count > 99 ? "99+" : Count.ToString();

    public string Waiting => Count == 1 ? "1 to mark" : $"{Count} to mark";

    /// <summary>
    /// Tense matters here. The queue now includes work handed in early, so a
    /// deadline can be in the future — "was due 20 Sept" on something not yet
    /// due reads as a mistake and makes a teacher doubt the whole widget.
    /// </summary>
    public string DueLabel => DueDate <= DateTimeOffset.Now
        ? $"was due {DueDate.ToLocalTime():ddd d MMM}"
        : $"due {DueDate.ToLocalTime():ddd d MMM}";

    /// <summary>True when this is early hand-in rather than a backlog.</summary>
    public bool IsEarly => DueDate > DateTimeOffset.Now;

    /// <summary>Spelled out, because a bare number tells a screen reader nothing.</summary>
    public string Description =>
        $"{AssignmentTitle}, {ClassName}. {Waiting}.";
}

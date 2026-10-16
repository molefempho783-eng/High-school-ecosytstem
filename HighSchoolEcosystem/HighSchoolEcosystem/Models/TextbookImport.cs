namespace HighSchoolEcosystem.Models;

/// <summary>
/// Mirrors /classes/{classId}/textbookJobs/{jobId}: one textbook a teacher has
/// uploaded for the AI to turn into draft course content.
///
/// Status is written by the Cloud Functions in functions/textbooks.js:
/// Queued → ReadingOutline → Generating → Ready, or Failed at any step.
/// Strings rather than an enum so a status added server-side can't crash an
/// older app build — the converter throws on an unknown enum name.
/// </summary>
public sealed class TextbookJob
{
    public string Id { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    /// <summary>classes/{classId}/textbooks/{file} — what the function reads.</summary>
    public string StoragePath { get; init; } = string.Empty;

    /// <summary>Tokenised URL, used when the PDF is attached for students.</summary>
    public string DownloadUrl { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }

    public string Status { get; init; } = TextbookStatus.Queued;
    public string Error { get; init; } = string.Empty;

    /// <summary>The title the AI read off the book. Empty until the outline is done.</summary>
    public string BookTitle { get; init; } = string.Empty;

    public int ChapterCount { get; init; }
    public int ChaptersDone { get; init; }
    public int ChaptersFailed { get; init; }

    /// <summary>
    /// The File material the original PDF was attached as, once the first
    /// chapter is published. Empty means not attached yet — and that deleting
    /// this job may delete the PDF, because nothing else points at it.
    /// </summary>
    public string TextbookMaterialId { get; init; } = string.Empty;

    public string RequestedBy { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; }

    public string DisplayTitle => string.IsNullOrWhiteSpace(BookTitle) ? FileName : BookTitle;

    public bool IsWorking => Status is TextbookStatus.Queued or TextbookStatus.ReadingOutline or TextbookStatus.Generating;

    /// <summary>One line for a teacher: where the job is, in words.</summary>
    public string StatusLine => Status switch
    {
        TextbookStatus.Queued => "Waiting to start…",
        TextbookStatus.ReadingOutline => "Reading the textbook and finding its chapters…",
        TextbookStatus.Generating => $"Writing notes and quizzes: {ChaptersDone} of {ChapterCount} chapters done",
        TextbookStatus.Ready when ChaptersFailed > 0 =>
            $"Ready to review · {ChapterCount - ChaptersFailed} of {ChapterCount} chapters drafted, {ChaptersFailed} failed",
        TextbookStatus.Ready => $"Ready to review · {ChapterCount} chapters drafted",
        TextbookStatus.Failed => string.IsNullOrWhiteSpace(Error) ? "Failed" : $"Failed: {Error}",
        _ => Status
    };
}

/// <summary>
/// Mirrors /classes/{classId}/textbookJobs/{jobId}/chapters/{chapterId}: one
/// chapter's DRAFT — notes the teacher can edit before publishing.
///
/// Status: Pending → Generating → Ready, or Failed; Published once the teacher
/// has copied it into the class.
/// </summary>
public sealed class TextbookChapter
{
    public string Id { get; init; } = string.Empty;
    public int Index { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public int StartPage { get; init; }
    public int EndPage { get; init; }
    public string Status { get; init; } = TextbookStatus.Pending;
    public string Notes { get; init; } = string.Empty;
    public int EstimatedMinutes { get; init; }
    public int QuestionCount { get; init; }
    public string Error { get; init; } = string.Empty;

    // Filled in step by step as the chapter is published, so a publish that
    // fails half-way resumes rather than creating a second unit.
    public string PublishedUnitId { get; init; } = string.Empty;
    public string PublishedMaterialId { get; init; } = string.Empty;
    public string PublishedAssignmentId { get; init; } = string.Empty;

    public string PagesLabel => EndPage > StartPage ? $"PDF pages {StartPage}–{EndPage}" : $"PDF page {StartPage}";

    public bool IsReady => Status == TextbookStatus.Ready;
    public bool IsFailed => Status == TextbookStatus.Failed;
    public bool IsPublished => Status == TextbookStatus.Published;
    public bool IsWorking => Status is TextbookStatus.Pending or TextbookStatus.Generating;
}

/// <summary>
/// A draft quiz question, answer included. Lives only under the chapter draft,
/// which students can't read; publishing splits it into /questions and
/// /answerKeys like every other question.
/// </summary>
public sealed class TextbookQuestion
{
    public string Id { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public List<string> Options { get; init; } = [];
    public int CorrectIndex { get; init; }
    public int Order { get; init; }
}

public static class TextbookStatus
{
    public const string Queued = "Queued";
    public const string ReadingOutline = "ReadingOutline";
    public const string Generating = "Generating";
    public const string Pending = "Pending";
    public const string Ready = "Ready";
    public const string Failed = "Failed";
    public const string Published = "Published";
}

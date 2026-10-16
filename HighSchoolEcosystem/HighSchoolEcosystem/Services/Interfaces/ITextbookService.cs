using HighSchoolEcosystem.Models;

namespace HighSchoolEcosystem.Services.Interfaces;

/// <summary>
/// AI textbook import: upload a PDF, let Gemini draft units, notes and quizzes
/// (functions/textbooks.js), review the drafts, then publish them into the
/// class. Teacher-only; firestore.rules enforces that, this doesn't.
/// </summary>
public interface ITextbookService
{
    /// <summary>False when no Storage bucket is configured — the feature is hidden.</summary>
    bool IsConfigured { get; }

    /// <summary>Uploads the PDF and queues it for the AI. Returns the job ID.</summary>
    Task<string> StartImportAsync(string classId, string fileName, Stream pdf, long sizeBytes,
        CancellationToken cancellationToken = default);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<TextbookJob>> GetJobsAsync(string classId, CancellationToken cancellationToken = default);

    Task<TextbookJob?> GetJobAsync(string classId, string jobId, CancellationToken cancellationToken = default);

    /// <summary>In book order.</summary>
    Task<IReadOnlyList<TextbookChapter>> GetChaptersAsync(string classId, string jobId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TextbookQuestion>> GetQuestionsAsync(string classId, string jobId, string chapterId,
        CancellationToken cancellationToken = default);

    /// <summary>Saves the teacher's edits to a draft's title and notes.</summary>
    Task SaveChapterEditsAsync(string classId, string jobId, string chapterId, string title, string notes,
        CancellationToken cancellationToken = default);

    Task DeleteQuestionAsync(string classId, string jobId, string chapterId, string questionId,
        CancellationToken cancellationToken = default);

    /// <summary>Asks the AI to write a failed (or unwanted) chapter again.</summary>
    Task RetryChapterAsync(string classId, string jobId, string chapterId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies a draft into the class: a unit, a study-notes chapter inside it,
    /// and — if it has questions — a practice quiz. Also attaches the original
    /// PDF to the class the first time any chapter is published. Resumable: a
    /// publish that fails half-way picks up where it stopped.
    /// </summary>
    Task PublishChapterAsync(string classId, TextbookJob job, TextbookChapter chapter,
        string title, string notes, CancellationToken cancellationToken = default);

    /// <summary>Removes a draft chapter. Doesn't touch anything already published.</summary>
    Task DiscardChapterAsync(string classId, string jobId, string chapterId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the job and all its drafts. The PDF is deleted too unless it was
    /// attached to the class — then the material owns it.
    /// </summary>
    Task DeleteJobAsync(string classId, TextbookJob job, CancellationToken cancellationToken = default);
}

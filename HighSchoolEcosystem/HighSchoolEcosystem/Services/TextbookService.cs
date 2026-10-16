using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.Services;

/// <summary>
/// See <see cref="ITextbookService"/>. The AI work itself happens server-side
/// in functions/textbooks.js; this uploads, reads the drafts back, and
/// publishes them through the same IClassService calls a teacher's own edits
/// use — so published content is indistinguishable from hand-made content and
/// every existing editing tool works on it.
/// </summary>
public sealed class TextbookService : ITextbookService
{
    private readonly IFirestoreClient _firestore;
    private readonly IFirebaseAuthService _auth;
    private readonly IStorageService _storage;
    private readonly IClassService _classes;

    public TextbookService(
        IFirestoreClient firestore, IFirebaseAuthService auth,
        IStorageService storage, IClassService classes)
    {
        _firestore = firestore;
        _auth = auth;
        _storage = storage;
        _classes = classes;
    }

    public bool IsConfigured => _storage.IsConfigured;

    private static string JobsPath(string classId) => $"classes/{classId}/textbookJobs";
    private static string JobPath(string classId, string jobId) => $"{JobsPath(classId)}/{jobId}";
    private static string ChaptersPath(string classId, string jobId) => $"{JobPath(classId, jobId)}/chapters";
    private static string ChapterPath(string classId, string jobId, string chapterId)
        => $"{ChaptersPath(classId, jobId)}/{chapterId}";
    private static string QuestionsPath(string classId, string jobId, string chapterId)
        => $"{ChapterPath(classId, jobId, chapterId)}/questions";

    // ==================== start ====================

    public async Task<string> StartImportAsync(
        string classId, string fileName, Stream pdf, long sizeBytes,
        CancellationToken cancellationToken = default)
    {
        var session = _auth.CurrentSession
            ?? throw new InvalidOperationException("You need to be signed in to upload a textbook.");

        // Upload first: the job document is what triggers the AI, and a job
        // pointing at a file that isn't there would just fail.
        var upload = await _storage.UploadTextbookAsync(classId, fileName, pdf, cancellationToken);

        var jobId = Guid.NewGuid().ToString("n");

        // An explicit field list rather than SetDocumentAsync: the converter
        // writes every readable property, and TextbookJob carries computed
        // ones (StatusLine, IsWorking) that don't belong in the database.
        await _firestore.PatchFieldsAsync(JobPath(classId, jobId), new Dictionary<string, object?>
        {
            ["FileName"] = fileName,
            ["StoragePath"] = upload.StoragePath,
            ["DownloadUrl"] = upload.DownloadUrl,
            ["FileSizeBytes"] = upload.SizeBytes > 0 ? upload.SizeBytes : sizeBytes,
            // The rule requires exactly this status on create; the function
            // only picks up queued jobs.
            ["Status"] = TextbookStatus.Queued,
            ["Error"] = string.Empty,
            ["BookTitle"] = string.Empty,
            ["ChapterCount"] = 0,
            ["ChaptersDone"] = 0,
            ["ChaptersFailed"] = 0,
            ["TextbookMaterialId"] = string.Empty,
            // Must equal request.auth.uid or the rule denies the create.
            ["RequestedBy"] = session.UserId,
            ["CreatedAt"] = DateTimeOffset.UtcNow,
            ["UpdatedAt"] = DateTimeOffset.UtcNow
        }, cancellationToken);

        return jobId;
    }

    // ==================== read ====================

    public async Task<IReadOnlyList<TextbookJob>> GetJobsAsync(
        string classId, CancellationToken cancellationToken = default)
    {
        var documents = await _firestore.ListDocumentsAsync<TextbookJob>(JobsPath(classId), cancellationToken);
        return documents
            .Select(d => WithId(d.Value, d.Id))
            .OrderByDescending(j => j.CreatedAt)
            .ToList();
    }

    public async Task<TextbookJob?> GetJobAsync(
        string classId, string jobId, CancellationToken cancellationToken = default)
    {
        var job = await _firestore.GetDocumentAsync<TextbookJob>(JobPath(classId, jobId), cancellationToken);
        return job is null ? null : WithId(job, jobId);
    }

    public async Task<IReadOnlyList<TextbookChapter>> GetChaptersAsync(
        string classId, string jobId, CancellationToken cancellationToken = default)
    {
        var documents = await _firestore.ListDocumentsAsync<TextbookChapter>(
            ChaptersPath(classId, jobId), cancellationToken);
        return documents
            .Select(d => WithId(d.Value, d.Id))
            .OrderBy(c => c.Index)
            .ToList();
    }

    private async Task<TextbookChapter?> GetChapterAsync(
        string classId, string jobId, string chapterId, CancellationToken cancellationToken)
    {
        var chapter = await _firestore.GetDocumentAsync<TextbookChapter>(
            ChapterPath(classId, jobId, chapterId), cancellationToken);
        return chapter is null ? null : WithId(chapter, chapterId);
    }

    public async Task<IReadOnlyList<TextbookQuestion>> GetQuestionsAsync(
        string classId, string jobId, string chapterId, CancellationToken cancellationToken = default)
    {
        var documents = await _firestore.ListDocumentsAsync<TextbookQuestion>(
            QuestionsPath(classId, jobId, chapterId), cancellationToken);
        return documents
            .Select(d => new TextbookQuestion
            {
                Id = d.Id,
                Text = d.Value.Text,
                Options = d.Value.Options,
                CorrectIndex = d.Value.CorrectIndex,
                Order = d.Value.Order
            })
            .OrderBy(q => q.Order)
            .ToList();
    }

    // ==================== edit ====================

    public Task SaveChapterEditsAsync(
        string classId, string jobId, string chapterId, string title, string notes,
        CancellationToken cancellationToken = default)
        => _firestore.PatchFieldsAsync(ChapterPath(classId, jobId, chapterId), new Dictionary<string, object?>
        {
            ["Title"] = title.Trim(),
            ["Notes"] = notes.Trim(),
            ["UpdatedAt"] = DateTimeOffset.UtcNow
        }, cancellationToken);

    public Task DeleteQuestionAsync(
        string classId, string jobId, string chapterId, string questionId,
        CancellationToken cancellationToken = default)
        => _firestore.DeleteDocumentAsync($"{QuestionsPath(classId, jobId, chapterId)}/{questionId}", cancellationToken);

    public Task RetryChapterAsync(
        string classId, string jobId, string chapterId, CancellationToken cancellationToken = default)
        // Moving INTO Pending is what triggers generateTextbookChapter again.
        => _firestore.PatchFieldsAsync(ChapterPath(classId, jobId, chapterId), new Dictionary<string, object?>
        {
            ["Status"] = TextbookStatus.Pending,
            ["Error"] = string.Empty,
            ["UpdatedAt"] = DateTimeOffset.UtcNow
        }, cancellationToken);

    // ==================== publish ====================

    public async Task PublishChapterAsync(
        string classId, TextbookJob job, TextbookChapter chapter, string title, string notes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("Give the unit a title before publishing.");
        if (string.IsNullOrWhiteSpace(notes))
            throw new InvalidOperationException("The notes are empty. Retry the chapter or write some before publishing.");

        // Re-read both: the copies the page holds can be minutes old, and the
        // Published* IDs are what make a repeated publish resume instead of
        // creating a second unit.
        var freshJob = await GetJobAsync(classId, job.Id, cancellationToken) ?? job;
        var fresh = await GetChapterAsync(classId, job.Id, chapter.Id, cancellationToken) ?? chapter;
        if (fresh.IsPublished) return;

        var chapterPath = ChapterPath(classId, job.Id, chapter.Id);
        title = title.Trim();
        notes = notes.Trim();

        // Save the teacher's latest edits first, so nothing typed is lost if a
        // later step fails.
        await SaveChapterEditsAsync(classId, job.Id, chapter.Id, title, notes, cancellationToken);

        // 1. The original PDF, once per textbook, as loose material ("Module
        //    information") so students can open the source.
        if (string.IsNullOrWhiteSpace(freshJob.TextbookMaterialId) && !string.IsNullOrWhiteSpace(freshJob.DownloadUrl))
        {
            var materialId = await _classes.SaveMaterialAsync(classId, new CourseMaterial
            {
                Kind = MaterialKind.File,
                Title = freshJob.DisplayTitle,
                Summary = "The full textbook this course is based on.",
                Url = freshJob.DownloadUrl,
                StoragePath = freshJob.StoragePath,
                FileName = freshJob.FileName,
                FileSizeBytes = freshJob.FileSizeBytes,
                UnitId = string.Empty,
                Order = 0
            }, cancellationToken);

            await _firestore.PatchFieldsAsync(JobPath(classId, job.Id), new Dictionary<string, object?>
            {
                ["TextbookMaterialId"] = materialId,
                ["UpdatedAt"] = DateTimeOffset.UtcNow
            }, cancellationToken);
        }

        var questions = await GetQuestionsAsync(classId, job.Id, chapter.Id, cancellationToken);
        var hasQuiz = questions.Count > 0;

        // 2. The unit, appended after whatever the class already has.
        var unitId = fresh.PublishedUnitId;
        if (string.IsNullOrWhiteSpace(unitId))
        {
            var units = await _classes.GetUnitsAsync(classId, cancellationToken);
            unitId = await _classes.SaveUnitAsync(classId, new LearningUnit
            {
                Title = title,
                Description = fresh.Summary,
                Order = units.Count == 0 ? 0 : units.Max(u => u.Order) + 1,
                ActivityCount = hasQuiz ? 2 : 1,
                TimeOnTaskMinutes = fresh.EstimatedMinutes
            }, cancellationToken);

            await PatchChapterAsync(chapterPath, "PublishedUnitId", unitId, cancellationToken);
        }

        // 3. The study notes, as a Chapter material inside that unit.
        if (string.IsNullOrWhiteSpace(fresh.PublishedMaterialId))
        {
            var materialId = await _classes.SaveMaterialAsync(classId, new CourseMaterial
            {
                Kind = MaterialKind.Chapter,
                Title = $"{title}: study notes",
                Summary = fresh.Summary,
                Body = notes,
                UnitId = unitId,
                Order = 0
            }, cancellationToken);

            await PatchChapterAsync(chapterPath, "PublishedMaterialId", materialId, cancellationToken);
        }

        // 4. The practice quiz. Auto-marked by the existing markSubmission
        //    function, like any Questions assignment.
        if (hasQuiz)
        {
            var assignmentId = fresh.PublishedAssignmentId;
            if (string.IsNullOrWhiteSpace(assignmentId))
            {
                assignmentId = await _classes.SaveAssignmentAsync(classId, new Assignment
                {
                    Title = $"{title}: practice quiz",
                    Description = "Practice questions on this unit, marked automatically. "
                                + "Generated from the textbook and checked by your teacher.",
                    Kind = AssignmentKind.Homework,
                    SubmissionMode = SubmissionMode.Questions,
                    Points = questions.Count,
                    // Two weeks, to the end of that day. The teacher can change
                    // it like any other assignment.
                    DueDate = new DateTimeOffset(DateTime.Today.AddDays(15).AddSeconds(-1),
                        DateTimeOffset.Now.Offset).ToUniversalTime()
                }, cancellationToken);

                await PatchChapterAsync(chapterPath, "PublishedAssignmentId", assignmentId, cancellationToken);
            }

            // Re-saving with the draft's own ID is idempotent, so a resumed
            // publish completes a half-written quiz instead of duplicating it.
            for (var i = 0; i < questions.Count; i++)
            {
                var q = questions[i];
                await _classes.SaveQuestionAsync(classId, assignmentId, new HomeworkQuestion
                {
                    Id = q.Id,
                    Text = q.Text,
                    Options = q.Options,
                    Points = 1,
                    Order = i
                }, q.CorrectIndex, cancellationToken);
            }
        }

        // 5. Done. Last, so a chapter only says Published once everything is.
        await PatchChapterAsync(chapterPath, "Status", TextbookStatus.Published, cancellationToken);
    }

    private Task PatchChapterAsync(string chapterPath, string field, string value, CancellationToken cancellationToken)
        => _firestore.PatchFieldsAsync(chapterPath, new Dictionary<string, object?>
        {
            [field] = value,
            ["UpdatedAt"] = DateTimeOffset.UtcNow
        }, cancellationToken);

    // ==================== remove ====================

    public async Task DiscardChapterAsync(
        string classId, string jobId, string chapterId, CancellationToken cancellationToken = default)
    {
        // Questions first: Firestore doesn't cascade, and once the chapter is
        // gone nothing in the app can find them to remove them.
        var questions = await GetQuestionsAsync(classId, jobId, chapterId, cancellationToken);
        foreach (var q in questions)
            await DeleteQuestionAsync(classId, jobId, chapterId, q.Id, cancellationToken);

        await _firestore.DeleteDocumentAsync(ChapterPath(classId, jobId, chapterId), cancellationToken);
    }

    public async Task DeleteJobAsync(
        string classId, TextbookJob job, CancellationToken cancellationToken = default)
    {
        var fresh = await GetJobAsync(classId, job.Id, cancellationToken) ?? job;

        // The PDF goes first, while the job that points at it still exists —
        // but only if no material has taken ownership of it. Once attached for
        // students, deleting that material is what removes the file.
        if (string.IsNullOrWhiteSpace(fresh.TextbookMaterialId) && !string.IsNullOrWhiteSpace(fresh.StoragePath))
        {
            try
            {
                await _storage.DeleteAsync(fresh.StoragePath, cancellationToken);
            }
            catch (Exception ex)
            {
                // A stray file must not block clearing the job away.
                CrashLogger.Log($"Textbook.DeletePdf:{job.Id}", ex);
            }
        }

        var chapters = await GetChaptersAsync(classId, job.Id, cancellationToken);
        foreach (var chapter in chapters)
            await DiscardChapterAsync(classId, job.Id, chapter.Id, cancellationToken);

        await _firestore.DeleteDocumentAsync(JobPath(classId, job.Id), cancellationToken);
    }

    // ==================== helpers ====================

    // The ID lives in the path, not the body, so it's grafted on after reading.
    private static TextbookJob WithId(TextbookJob v, string id) => new()
    {
        Id = id,
        FileName = v.FileName,
        StoragePath = v.StoragePath,
        DownloadUrl = v.DownloadUrl,
        FileSizeBytes = v.FileSizeBytes,
        Status = v.Status,
        Error = v.Error,
        BookTitle = v.BookTitle,
        ChapterCount = v.ChapterCount,
        ChaptersDone = v.ChaptersDone,
        ChaptersFailed = v.ChaptersFailed,
        TextbookMaterialId = v.TextbookMaterialId,
        RequestedBy = v.RequestedBy,
        CreatedAt = v.CreatedAt,
        UpdatedAt = v.UpdatedAt
    };

    private static TextbookChapter WithId(TextbookChapter v, string id) => new()
    {
        Id = id,
        Index = v.Index,
        Title = v.Title,
        Summary = v.Summary,
        StartPage = v.StartPage,
        EndPage = v.EndPage,
        Status = v.Status,
        Notes = v.Notes,
        EstimatedMinutes = v.EstimatedMinutes,
        QuestionCount = v.QuestionCount,
        Error = v.Error,
        PublishedUnitId = v.PublishedUnitId,
        PublishedMaterialId = v.PublishedMaterialId,
        PublishedAssignmentId = v.PublishedAssignmentId
    };
}

using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.Services;

public sealed class ClassService : IClassService
{
    private readonly IFirestoreClient _firestore;
    private readonly IFirebaseAuthService _auth;
    private readonly IUserProfileService _profiles;
    private readonly IStorageService _storage;

    public ClassService(
        IFirestoreClient firestore, IFirebaseAuthService auth,
        IUserProfileService profiles, IStorageService storage)
    {
        _firestore = firestore;
        _auth = auth;
        _profiles = profiles;
        _storage = storage;
    }

    public async Task<IReadOnlyList<SchoolClass>> GetAllClassesAsync(CancellationToken cancellationToken = default)
    {
        var documents = await _firestore.ListDocumentsAsync<SchoolClass>("classes", cancellationToken);
        return documents
            .Select(d => WithId(d.Value, d.Id))
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<SchoolClass>> GetMyClassesAsync(CancellationToken cancellationToken = default)
    {
        var uid = _auth.CurrentSession?.UserId;
        if (string.IsNullOrEmpty(uid)) return [];

        // Filtered client-side rather than with a Firestore query: the rules
        // already allow any school member to read class documents, and the
        // per-school volume is small enough that a full list is cheaper than
        // maintaining a composite index.
        var all = await GetAllClassesAsync(cancellationToken);
        return all.Where(c => c.TeacherId == uid).ToList();
    }

    public async Task<IReadOnlyList<SchoolClass>> GetMyEnrolledClassesAsync(CancellationToken cancellationToken = default)
    {
        var uid = _auth.CurrentSession?.UserId;
        if (string.IsNullOrEmpty(uid)) return [];

        // Same client-side filter as GetMyClassesAsync: the rules already let any
        // school member read class documents, so one list beats maintaining an
        // index per query.
        var all = await GetAllClassesAsync(cancellationToken);
        return all.Where(c => c.StudentIds.Contains(uid)).ToList();
    }

    public async Task<IReadOnlyList<ClassAssignment>> GetAssignmentsForAsync(
        IReadOnlyList<SchoolClass> classes, CancellationToken cancellationToken = default)
    {
        var results = new List<ClassAssignment>();

        foreach (var schoolClass in classes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var assignments = await GetAssignmentsAsync(schoolClass.Id, cancellationToken);
                results.AddRange(assignments.Select(a =>
                    new ClassAssignment(schoolClass.Id, schoolClass.Name, a)));
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                // Enrolled in the class document but denied its assignments —
                // means the roster and the rules disagree. Skip that class
                // rather than failing the student's whole dashboard.
                System.Diagnostics.Debug.WriteLine(
                    $"[classes] assignments denied for '{schoolClass.Id}' ({schoolClass.Name})");
            }
        }

        return results.OrderBy(r => r.Assignment.DueDate).ToList();
    }

    public async Task<IReadOnlyList<UserProfile>> GetStudentsAsync(CancellationToken cancellationToken = default)
    {
        var documents = await _firestore.ListDocumentsAsync<UserProfile>("users", cancellationToken);
        return documents
            // HasRole, not Role ==: a Student who also writes for the
            // newspaper still belongs in class rosters.
            .Where(d => d.Value.HasRole(Role.Student))
            // The document ID is the authoritative UID; the Uid field is written
            // by the client at bootstrap and could be stale.
            .Select(d => new UserProfile
            {
                Uid = d.Id,
                DisplayName = d.Value.DisplayName,
                Email = d.Value.Email,
                Role = d.Value.Role,
                Roles = d.Value.Roles,
                ClassIds = d.Value.ClassIds,
                LinkedStudentIds = d.Value.LinkedStudentIds
            })
            .OrderBy(u => string.IsNullOrWhiteSpace(u.DisplayName) ? u.Email : u.DisplayName,
                     StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<SchoolClass?> GetClassAsync(string classId, CancellationToken cancellationToken = default)
    {
        var found = await _firestore.GetDocumentAsync<SchoolClass>($"classes/{classId}", cancellationToken);
        return found is null ? null : WithId(found, classId);
    }

    public async Task<string> CreateClassAsync(string name, string subject, string room, CancellationToken cancellationToken = default)
    {
        var session = _auth.CurrentSession
            ?? throw new InvalidOperationException("Sign in before creating a class.");

        // Denormalised so class lists don't need a lookup per row. Kept in sync
        // only on create — a display-name change won't rewrite existing classes.
        var profile = await _profiles.GetOrCreateCurrentProfileAsync(cancellationToken);

        var classId = Guid.NewGuid().ToString("n");
        var newClass = new SchoolClass
        {
            Id = classId,
            Name = name.Trim(),
            Subject = subject.Trim(),
            Room = room.Trim(),
            TeacherId = session.UserId,
            TeacherName = profile?.DisplayName ?? session.Email ?? string.Empty,
            StudentIds = []
        };

        await _firestore.SetDocumentAsync($"classes/{classId}", newClass, cancellationToken);
        return classId;
    }

    public Task UpdateClassDetailsAsync(string classId, string name, string subject, string room, CancellationToken cancellationToken = default)
        => _firestore.PatchFieldsAsync($"classes/{classId}", new Dictionary<string, object?>
        {
            ["Name"] = name.Trim(),
            ["Subject"] = subject.Trim(),
            ["Room"] = room.Trim()
        }, cancellationToken);

    public Task SetEnrolledStudentsAsync(string classId, IReadOnlyList<string> studentUids, CancellationToken cancellationToken = default)
        => _firestore.PatchFieldsAsync($"classes/{classId}",
            new Dictionary<string, object?> { ["StudentIds"] = studentUids.ToList() }, cancellationToken);

    public Task DeleteClassAsync(string classId, CancellationToken cancellationToken = default)
        // Note: this leaves the assignments subcollection orphaned. Firestore has
        // no cascading delete; clearing it properly needs a server-side job.
        => _firestore.DeleteDocumentAsync($"classes/{classId}", cancellationToken);

    // ---- assignments ----

    public async Task<IReadOnlyList<Assignment>> GetAssignmentsAsync(string classId, CancellationToken cancellationToken = default)
    {
        var documents = await _firestore.ListDocumentsAsync<Assignment>($"classes/{classId}/assignments", cancellationToken);
        return documents
            .Select(d => new Assignment
            {
                Id = d.Id,
                Title = d.Value.Title,
                Description = d.Value.Description,
                Kind = d.Value.Kind,
                DueDate = d.Value.DueDate,
                Points = d.Value.Points,
                // Carried through explicitly. This projection dropped it once,
                // which meant every assignment read back as Written no matter
                // what the teacher chose — a silent feature-wide failure.
                SubmissionMode = d.Value.SubmissionMode
            })
            .OrderBy(a => a.DueDate)
            .ToList();
    }

    /// <summary>
    /// Writes the assignment and returns its ID — the caller needs it to send a
    /// teacher straight to the question editor for work they just created.
    /// </summary>
    public async Task<string> SaveAssignmentAsync(string classId, Assignment assignment, CancellationToken cancellationToken = default)
    {
        var id = string.IsNullOrWhiteSpace(assignment.Id) ? Guid.NewGuid().ToString("n") : assignment.Id;
        var toSave = new Assignment
        {
            Id = id,
            Title = assignment.Title.Trim(),
            Description = assignment.Description.Trim(),
            Kind = assignment.Kind,
            DueDate = assignment.DueDate,
            Points = assignment.Points,
            // Was missing. Rebuilding the object field by field silently
            // discarded the teacher's choice of how work is handed in.
            SubmissionMode = assignment.SubmissionMode
        };
        await _firestore.SetDocumentAsync($"classes/{classId}/assignments/{id}", toSave, cancellationToken);
        return id;
    }

    public Task DeleteAssignmentAsync(string classId, string assignmentId, CancellationToken cancellationToken = default)
        => _firestore.DeleteDocumentAsync($"classes/{classId}/assignments/{assignmentId}", cancellationToken);

    // ---- multiple-choice questions ----

    public async Task<IReadOnlyList<HomeworkQuestion>> GetQuestionsAsync(string classId, string assignmentId, CancellationToken cancellationToken = default)
    {
        var documents = await _firestore.ListDocumentsAsync<HomeworkQuestion>(
            $"classes/{classId}/assignments/{assignmentId}/questions", cancellationToken);

        return documents
            .Select(d => new HomeworkQuestion
            {
                Id = d.Id,
                Text = d.Value.Text,
                Options = d.Value.Options,
                Points = d.Value.Points,
                Order = d.Value.Order
            })
            .OrderBy(q => q.Order)
            .ToList();
    }

    public async Task<IReadOnlyDictionary<string, int>> GetAnswerKeyAsync(string classId, string assignmentId, CancellationToken cancellationToken = default)
    {
        var documents = await _firestore.ListDocumentsAsync<QuestionAnswerKey>(
            $"classes/{classId}/assignments/{assignmentId}/answerKeys", cancellationToken);

        // Document ID is the question ID it answers.
        return documents.ToDictionary(d => d.Id, d => d.Value.CorrectIndex);
    }

    public async Task SaveQuestionAsync(string classId, string assignmentId, HomeworkQuestion question, int correctIndex, CancellationToken cancellationToken = default)
    {
        if (question.Options.Count < 2)
            throw new InvalidOperationException("A multiple-choice question needs at least two options.");
        if (correctIndex < 0 || correctIndex >= question.Options.Count)
            throw new InvalidOperationException("The correct answer must be one of the options provided.");

        var id = string.IsNullOrWhiteSpace(question.Id) ? Guid.NewGuid().ToString("n") : question.Id;
        var basePath = $"classes/{classId}/assignments/{assignmentId}";

        var toSave = new HomeworkQuestion
        {
            Id = id,
            Text = question.Text.Trim(),
            Options = question.Options,
            Points = question.Points,
            Order = question.Order
        };

        // Two writes, no transaction. Question first so a failure on the second
        // leaves a visible question with no answer (shown as "not set") rather
        // than an orphaned key nothing references.
        //
        // Recoverable: passing a non-empty Id re-saves the same two documents,
        // so re-editing the question and saving repairs a half-written pair.
        await _firestore.SetDocumentAsync($"{basePath}/questions/{id}", toSave, cancellationToken);
        await _firestore.SetDocumentAsync($"{basePath}/answerKeys/{id}",
            new QuestionAnswerKey { CorrectIndex = correctIndex }, cancellationToken);
    }

    public async Task DeleteQuestionAsync(string classId, string assignmentId, string questionId, CancellationToken cancellationToken = default)
    {
        var basePath = $"classes/{classId}/assignments/{assignmentId}";
        await _firestore.DeleteDocumentAsync($"{basePath}/questions/{questionId}", cancellationToken);
        await _firestore.DeleteDocumentAsync($"{basePath}/answerKeys/{questionId}", cancellationToken);
    }

    // ---- submissions ----

    public async Task<Submission?> GetMySubmissionAsync(string classId, string assignmentId, CancellationToken cancellationToken = default)
    {
        var uid = _auth.CurrentSession?.UserId;
        if (string.IsNullOrEmpty(uid)) return null;

        return await _firestore.GetDocumentAsync<Submission>(
            $"classes/{classId}/assignments/{assignmentId}/submissions/{uid}", cancellationToken);
    }

    public async Task SubmitAsync(string classId, string assignmentId, string text,
        IReadOnlyList<string> answers, CancellationToken cancellationToken = default)
    {
        var session = _auth.CurrentSession
            ?? throw new InvalidOperationException("Sign in before submitting.");

        var profile = await _profiles.GetOrCreateCurrentProfileAsync(cancellationToken);

        // PatchFieldsAsync, NOT SetDocumentAsync: the converter would serialise
        // every property including Grade and Feedback, and the rules reject a
        // student write that carries either. Listing the fields explicitly keeps
        // the teacher's mark untouched on a resubmit, too.
        await _firestore.PatchFieldsAsync(
            $"classes/{classId}/assignments/{assignmentId}/submissions/{session.UserId}",
            new Dictionary<string, object?>
            {
                ["StudentId"] = session.UserId,
                ["StudentName"] = profile?.DisplayName ?? session.Email ?? string.Empty,
                ["SubmittedAt"] = DateTimeOffset.UtcNow,
                ["Text"] = text.Trim(),
                ["Answers"] = answers.ToList()
            }, cancellationToken);
    }

    // ---- timetable ----

    public async Task<IReadOnlyList<TimetableEntry>> GetTimetableAsync(CancellationToken cancellationToken = default)
    {
        var documents = await _firestore.ListDocumentsAsync<TimetableEntry>("timetable", cancellationToken);
        return documents
            .Select(d => new TimetableEntry
            {
                Id = d.Id,
                ClassId = d.Value.ClassId,
                ClassName = d.Value.ClassName,
                DayOfWeek = d.Value.DayOfWeek,
                Period = d.Value.Period,
                StartTime = d.Value.StartTime,
                EndTime = d.Value.EndTime,
                Room = d.Value.Room
            })
            .OrderBy(e => e.DayOfWeek)
            .ThenBy(e => e.StartTime, StringComparer.Ordinal)
            .ToList();
    }

    public Task SaveTimetableEntryAsync(TimetableEntry entry, CancellationToken cancellationToken = default)
    {
        var id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("n") : entry.Id;
        var toSave = new TimetableEntry
        {
            Id = id,
            ClassId = entry.ClassId,
            ClassName = entry.ClassName,
            DayOfWeek = entry.DayOfWeek,
            Period = entry.Period,
            StartTime = entry.StartTime,
            EndTime = entry.EndTime,
            Room = entry.Room
        };
        return _firestore.SetDocumentAsync($"timetable/{id}", toSave, cancellationToken);
    }

    public Task DeleteTimetableEntryAsync(string entryId, CancellationToken cancellationToken = default)
        => _firestore.DeleteDocumentAsync($"timetable/{entryId}", cancellationToken);

    // ==================== course materials ====================

    private static string MaterialsPath(string classId) => $"classes/{classId}/materials";

    public async Task<IReadOnlyList<CourseMaterial>> GetMaterialsAsync(
        string classId, CancellationToken cancellationToken = default)
    {
        var documents = await _firestore.ListDocumentsAsync<CourseMaterial>(
            MaterialsPath(classId), cancellationToken);

        return documents
            .Select(d => WithId(d.Value, d.Id))
            .OrderBy(m => m.Order)
            // Tie-break on creation so two materials sharing an Order — which
            // sparse ordering makes possible — still land in a stable sequence
            // rather than swapping places between loads.
            .ThenBy(m => m.CreatedAt)
            .ToList();
    }

    public async Task<string> SaveMaterialAsync(
        string classId, CourseMaterial material, CancellationToken cancellationToken = default)
    {
        var session = _auth.CurrentSession
            ?? throw new InvalidOperationException("You need to be signed in to add material.");

        var isNew = string.IsNullOrWhiteSpace(material.Id);
        var id = isNew ? Guid.NewGuid().ToString("n") : material.Id;

        if (isNew)
        {
            var profile = await _profiles.GetOrCreateCurrentProfileAsync(cancellationToken);

            await _firestore.SetDocumentAsync($"{MaterialsPath(classId)}/{id}", new CourseMaterial
            {
                Kind = material.Kind,
                Title = material.Title.Trim(),
                Summary = material.Summary.Trim(),
                Body = material.Body,
                Url = material.Url.Trim(),
                StoragePath = material.StoragePath,
                FileName = material.FileName,
                FileSizeBytes = material.FileSizeBytes,
                Order = material.Order,
                // Must equal request.auth.uid or the rule denies the create.
                AuthorId = session.UserId,
                AuthorName = profile?.DisplayName ?? session.Email ?? string.Empty,
                CreatedAt = DateTimeOffset.UtcNow
            }, cancellationToken);

            return id;
        }

        // Editing: patch an explicit field list. A whole-object write would
        // resend AuthorId, and the rule requires it to be unchanged — which it
        // would be, but only by luck. Patching makes that structural.
        await _firestore.PatchFieldsAsync($"{MaterialsPath(classId)}/{id}",
            new Dictionary<string, object?>
            {
                ["Kind"] = material.Kind,
                ["Title"] = material.Title.Trim(),
                ["Summary"] = material.Summary.Trim(),
                ["Body"] = material.Body,
                ["Url"] = material.Url.Trim(),
                ["StoragePath"] = material.StoragePath,
                ["FileName"] = material.FileName,
                ["FileSizeBytes"] = material.FileSizeBytes,
                ["Order"] = material.Order,
                ["UpdatedAt"] = DateTimeOffset.UtcNow
            }, cancellationToken);

        return id;
    }

    public async Task DeleteMaterialAsync(
        string classId, CourseMaterial material, CancellationToken cancellationToken = default)
    {
        // File first, document second. The document holds the only pointer to
        // the object, so deleting it first would strand the file in the bucket
        // with nothing left to say it exists.
        if (material.Kind == MaterialKind.File && !string.IsNullOrWhiteSpace(material.StoragePath))
        {
            try
            {
                await _storage.DeleteAsync(material.StoragePath, cancellationToken);
            }
            catch (Exception ex)
            {
                // A stranded file is untidy; a material row that won't delete is
                // a bug the teacher can see. Log and carry on.
                CrashLogger.Log("ClassService.DeleteMaterialFile", ex);
            }
        }

        await _firestore.DeleteDocumentAsync(
            $"{MaterialsPath(classId)}/{material.Id}", cancellationToken);
    }

    public async Task MoveMaterialAsync(
        string classId, CourseMaterial material, bool up, CancellationToken cancellationToken = default)
    {
        var all = await GetMaterialsAsync(classId, cancellationToken);

        // Reorder WITHIN the material's own unit. Ordering across the whole
        // class would let "up" from the top of one unit swap a document into
        // the unit above it, silently moving it between chapters.
        var unitId = material.UnitId ?? string.Empty;
        var siblings = all
            .Where(m => (m.UnitId ?? string.Empty) == unitId)
            .ToList();

        var index = siblings.FindIndex(m => m.Id == material.Id);
        if (index < 0) return;

        var swapWith = up ? index - 1 : index + 1;
        if (swapWith < 0 || swapWith >= siblings.Count) return;

        var a = siblings[index];
        var b = siblings[swapWith];

        // Rewrite both positions from the list index rather than swapping the
        // stored Order values: duplicates in the existing data would otherwise
        // make the swap a no-op and the row would appear stuck.
        await _firestore.PatchFieldsAsync($"{MaterialsPath(classId)}/{a.Id}",
            new Dictionary<string, object?> { ["Order"] = swapWith }, cancellationToken);

        await _firestore.PatchFieldsAsync($"{MaterialsPath(classId)}/{b.Id}",
            new Dictionary<string, object?> { ["Order"] = index }, cancellationToken);
    }

    public async Task SubmitWithFileAsync(
        string classId, string assignmentId, string text, IReadOnlyList<string> answers,
        Stream fileContent, string fileName, string contentType,
        CancellationToken cancellationToken = default)
    {
        var session = _auth.CurrentSession
            ?? throw new InvalidOperationException("Sign in before submitting.");

        // Upload FIRST. If it fails, nothing is written and the student still
        // has their work — a submission document pointing at a file that isn't
        // there is worse than no submission at all.
        var upload = await _storage.UploadSubmissionAsync(
            classId, assignmentId, session.UserId, fileName, fileContent, contentType,
            cancellationToken);

        var profile = await _profiles.GetOrCreateCurrentProfileAsync(cancellationToken);

        // Explicit field list, same reason as SubmitAsync: the rules reject a
        // student write carrying Grade, Feedback or any marking-state field.
        await _firestore.PatchFieldsAsync(
            $"classes/{classId}/assignments/{assignmentId}/submissions/{session.UserId}",
            new Dictionary<string, object?>
            {
                ["StudentId"] = session.UserId,
                ["StudentName"] = profile?.DisplayName ?? session.Email ?? string.Empty,
                ["SubmittedAt"] = DateTimeOffset.UtcNow,
                ["Text"] = text.Trim(),
                ["Answers"] = answers.ToList(),
                ["FileUrl"] = upload.DownloadUrl,
                ["FileStoragePath"] = upload.StoragePath,
                ["FileName"] = fileName,
                ["FileSizeBytes"] = upload.SizeBytes
            }, cancellationToken);
    }

    // ==================== marking ====================

    public async Task<IReadOnlyList<Submission>> GetSubmissionsForAssignmentAsync(
        string classId, string assignmentId, CancellationToken cancellationToken = default)
    {
        var documents = await _firestore.ListDocumentsAsync<Submission>(
            $"classes/{classId}/assignments/{assignmentId}/submissions", cancellationToken);

        return documents
            .Select(d => WithStudentId(d.Value, d.Id))
            // Work needing attention first, then the rest newest-first. A
            // teacher opening this wants the pile they still have to read.
            .OrderByDescending(s => s.NeedsTeacherMarking)
            .ThenByDescending(s => s.SubmittedAt)
            .ToList();
    }

    /// <summary>
    /// Counts, per assignment, the submissions a person still has to read.
    ///
    /// Sequential rather than parallel on purpose: a teacher with twenty
    /// assignments would otherwise open twenty simultaneous HTTPS connections
    /// on a phone, which is slower than doing them in turn and far more likely
    /// to time out on a school network.
    ///
    /// One assignment failing must not blank the rest of the badges, so a
    /// failed read is skipped rather than thrown — a missing badge is a much
    /// smaller harm than an unusable screen.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, int>> GetNeedsMarkingCountsAsync(
        string classId, IReadOnlyList<string> assignmentIds, CancellationToken cancellationToken = default)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var assignmentId in assignmentIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var submissions = await GetSubmissionsForAssignmentAsync(classId, assignmentId, cancellationToken);
                var waiting = submissions.Count(s => s.NeedsTeacherMarking);
                if (waiting > 0) counts[assignmentId] = waiting;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                CrashLogger.Log("NeedsMarkingCount", ex);
            }
        }

        return counts;
    }

    /// <summary>
    /// How far back the marking queue looks. Work older than this is still
    /// markable from the class page — it just stops costing a read on every
    /// dashboard open, which is the difference between a screen that loads and
    /// one that doesn't on a school network.
    /// </summary>
    private static readonly TimeSpan MarkingQueueWindow = TimeSpan.FromDays(45);

    public async Task<IReadOnlyList<MarkingQueueItem>> GetMarkingQueueAsync(
        IReadOnlyList<SchoolClass> classes, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var oldest = now - MarkingQueueWindow;
        var queue = new List<MarkingQueueItem>();

        foreach (var schoolClass in classes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var assignments = await GetAssignmentsAsync(schoolClass.Id, cancellationToken);

                // Only work that can be handed in, and only once its deadline
                // has passed — marking before then would be marking a class
                // that hasn't finished writing.
                var due = assignments
                    .Where(a => a.Kind.IsSubmittable())
                    .Where(a => a.DueDate <= now && a.DueDate >= oldest)
                    .ToList();

                if (due.Count == 0) continue;

                var counts = await GetNeedsMarkingCountsAsync(
                    schoolClass.Id, due.Select(a => a.Id).ToList(), cancellationToken);

                foreach (var assignment in due)
                {
                    if (!counts.TryGetValue(assignment.Id, out var waiting) || waiting == 0) continue;

                    queue.Add(new MarkingQueueItem(
                        schoolClass.Id,
                        string.IsNullOrWhiteSpace(schoolClass.Name) ? schoolClass.Subject : schoolClass.Name,
                        assignment.Id,
                        assignment.Title,
                        assignment.Kind,
                        assignment.DueDate,
                        waiting));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One unreadable class must not empty the whole queue.
                CrashLogger.Log($"ClassService.MarkingQueue:{schoolClass.Id}", ex);
            }
        }

        // Longest-waiting first: the work that has been sitting unmarked the
        // longest is the work a teacher is most overdue on.
        return queue.OrderBy(q => q.DueDate).ToList();
    }

    public Task SetGradeAsync(
        string classId, string assignmentId, string studentId, int grade, string feedback,
        CancellationToken cancellationToken = default)
        => _firestore.PatchFieldsAsync(
            $"classes/{classId}/assignments/{assignmentId}/submissions/{studentId}",
            new Dictionary<string, object?>
            {
                ["Grade"] = grade,
                ["Feedback"] = feedback.Trim(),
                // Stamped so the work leaves the "needs marking" queue and the
                // auto-marker can tell a human grade from its own.
                ["TeacherMarkedAt"] = DateTimeOffset.UtcNow
            }, cancellationToken);

    // The document ID is the student's UID and isn't in the body.
    private static Submission WithStudentId(Submission value, string studentId) => new()
    {
        StudentId = string.IsNullOrWhiteSpace(value.StudentId) ? studentId : value.StudentId,
        StudentName = value.StudentName,
        SubmittedAt = value.SubmittedAt,
        Text = value.Text,
        Answers = value.Answers,
        Grade = value.Grade,
        Feedback = value.Feedback,
        AutoMarkedAt = value.AutoMarkedAt,
        AutoScore = value.AutoScore,
        AutoScoreMax = value.AutoScoreMax,
        TeacherMarkedAt = value.TeacherMarkedAt,
        FileUrl = value.FileUrl,
        FileStoragePath = value.FileStoragePath,
        FileName = value.FileName,
        FileSizeBytes = value.FileSizeBytes
    };

    // ==================== class announcements ====================

    private static string ClassAnnouncementsPath(string classId) => $"classes/{classId}/announcements";

    public async Task<IReadOnlyList<Announcement>> GetClassAnnouncementsAsync(
        string classId, bool includeExpired = false, CancellationToken cancellationToken = default)
    {
        var documents = await _firestore.ListDocumentsAsync<Announcement>(
            ClassAnnouncementsPath(classId), cancellationToken);

        return documents
            .Select(d => WithClassContext(d.Value, d.Id, classId, string.Empty))
            // Readers don't see lapsed notices; the teacher who wrote them does.
            .Where(a => includeExpired || !a.IsExpired)
            .OrderByDescending(a => a.IsPinned)
            .ThenByDescending(a => a.PostedAt)
            .ToList();
    }

    public async Task<string> SaveClassAnnouncementAsync(
        string classId, string className, Announcement announcement,
        CancellationToken cancellationToken = default)
    {
        var session = _auth.CurrentSession
            ?? throw new InvalidOperationException("You need to be signed in to post a notice.");

        var isNew = string.IsNullOrWhiteSpace(announcement.Id);
        var id = isNew ? Guid.NewGuid().ToString("n") : announcement.Id;

        if (isNew)
        {
            var profile = await _profiles.GetOrCreateCurrentProfileAsync(cancellationToken);

            await _firestore.SetDocumentAsync($"{ClassAnnouncementsPath(classId)}/{id}", new Announcement
            {
                Title = announcement.Title.Trim(),
                Body = announcement.Body.Trim(),
                IsPinned = announcement.IsPinned,
                ExpiresAt = announcement.ExpiresAt,
                // Denormalised so a merged feed can label the notice without a
                // class lookup per row.
                ClassName = className,
                // Must equal request.auth.uid or the rule denies the create.
                AuthorId = session.UserId,
                AuthorName = profile?.DisplayName ?? session.Email ?? string.Empty,
                PostedAt = DateTimeOffset.UtcNow
            }, cancellationToken);

            return id;
        }

        // Explicit field list: the rule requires AuthorId unchanged, and a
        // whole-object write would resend it.
        await _firestore.PatchFieldsAsync($"{ClassAnnouncementsPath(classId)}/{id}",
            new Dictionary<string, object?>
            {
                ["Title"] = announcement.Title.Trim(),
                ["Body"] = announcement.Body.Trim(),
                ["IsPinned"] = announcement.IsPinned,
                ["ExpiresAt"] = announcement.ExpiresAt,
                ["EditedAt"] = DateTimeOffset.UtcNow
            }, cancellationToken);

        return id;
    }

    public Task DeleteClassAnnouncementAsync(
        string classId, string announcementId, CancellationToken cancellationToken = default)
        => _firestore.DeleteDocumentAsync(
            $"{ClassAnnouncementsPath(classId)}/{announcementId}", cancellationToken);

    public async Task<IReadOnlyList<Announcement>> GetAnnouncementsForClassesAsync(
        IReadOnlyList<SchoolClass> classes, CancellationToken cancellationToken = default)
    {
        var all = new List<Announcement>();

        // One read per class. Fine at school scale — a student is in under ten —
        // and it avoids a collection-group query, which would need its own index
        // and a rule that can't see the parent class to check enrolment.
        foreach (var schoolClass in classes)
        {
            try
            {
                var documents = await _firestore.ListDocumentsAsync<Announcement>(
                    ClassAnnouncementsPath(schoolClass.Id), cancellationToken);

                // The live class name wins over the stored copy, so a rename
                // shows through immediately.
                all.AddRange(documents
                    .Select(d => WithClassContext(d.Value, d.Id, schoolClass.Id, schoolClass.Name))
                    // This feed is always for readers — students and parents —
                    // so lapsed notices never belong in it.
                    .Where(a => !a.IsExpired));
            }
            catch (Exception ex)
            {
                // One unreadable class shouldn't blank the whole feed.
                CrashLogger.Log($"ClassService.ClassAnnouncements:{schoolClass.Id}", ex);
            }
        }

        return all
            .OrderByDescending(a => a.IsPinned)
            .ThenByDescending(a => a.PostedAt)
            .ToList();
    }

    // The document ID and the class both live in the path, not the body, so
    // they're grafted on after deserialisation.
    private static Announcement WithClassContext(
        Announcement value, string id, string classId, string className) => new()
    {
        Id = id,
        Title = value.Title,
        Body = value.Body,
        AuthorId = value.AuthorId,
        AuthorName = value.AuthorName,
        PostedAt = value.PostedAt,
        EditedAt = value.EditedAt,
        IsPinned = value.IsPinned,
        IsPublic = value.IsPublic,
        ExpiresAt = value.ExpiresAt,
        ClassId = classId,
        ClassName = string.IsNullOrWhiteSpace(className) ? value.ClassName : className
    };

    // ==================== parents ====================

    public async Task<IReadOnlyList<SchoolClass>> GetClassesForStudentAsync(
        string studentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(studentId)) return [];

        // Filtered client-side: class documents are readable school-wide, and a
        // per-school roll is small enough that one list beats an index.
        var all = await GetAllClassesAsync(cancellationToken);
        return all.Where(c => c.StudentIds.Contains(studentId, StringComparer.Ordinal)).ToList();
    }

    public async Task<IReadOnlyList<ClassSubmission>> GetSubmissionsForStudentAsync(
        string classId, string studentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(studentId)) return [];

        var assignments = await GetAssignmentsAsync(classId, cancellationToken);

        var results = new List<ClassSubmission>();
        foreach (var assignment in assignments)
        {
            try
            {
                var submission = await _firestore.GetDocumentAsync<Submission>(
                    $"classes/{classId}/assignments/{assignment.Id}/submissions/{studentId}",
                    cancellationToken);

                if (submission is not null)
                    results.Add(new ClassSubmission(assignment.Id, submission));
            }
            catch (Exception ex)
            {
                // One unreadable submission shouldn't blank the whole list.
                CrashLogger.Log($"ClassService.GetSubmissionForStudent:{assignment.Id}", ex);
            }
        }

        return results;
    }

    // ==================== learning units ====================

    private static string UnitsPath(string classId) => $"classes/{classId}/units";

    public async Task<IReadOnlyList<LearningUnit>> GetUnitsAsync(
        string classId, CancellationToken cancellationToken = default)
    {
        var documents = await _firestore.ListDocumentsAsync<LearningUnit>(
            UnitsPath(classId), cancellationToken);

        return documents
            .Select(d => WithId(d.Value, d.Id))
            .OrderBy(u => u.Order)
            .ThenBy(u => u.CreatedAt)
            .ToList();
    }

    public async Task<string> SaveUnitAsync(
        string classId, LearningUnit unit, CancellationToken cancellationToken = default)
    {
        var session = _auth.CurrentSession
            ?? throw new InvalidOperationException("You need to be signed in to add a unit.");

        var isNew = string.IsNullOrWhiteSpace(unit.Id);
        var id = isNew ? Guid.NewGuid().ToString("n") : unit.Id;

        if (isNew)
        {
            await _firestore.SetDocumentAsync($"{UnitsPath(classId)}/{id}", new LearningUnit
            {
                Title = unit.Title.Trim(),
                Description = unit.Description.Trim(),
                Order = unit.Order,
                ActivityCount = unit.ActivityCount,
                TimeOnTaskMinutes = unit.TimeOnTaskMinutes,
                // Must equal request.auth.uid or the rule denies the create.
                AuthorId = session.UserId,
                CreatedAt = DateTimeOffset.UtcNow
            }, cancellationToken);

            return id;
        }

        // Explicit field list so AuthorId and CreatedAt survive untouched — the
        // rule requires AuthorId to be unchanged.
        await _firestore.PatchFieldsAsync($"{UnitsPath(classId)}/{id}",
            new Dictionary<string, object?>
            {
                ["Title"] = unit.Title.Trim(),
                ["Description"] = unit.Description.Trim(),
                ["Order"] = unit.Order,
                ["ActivityCount"] = unit.ActivityCount,
                ["TimeOnTaskMinutes"] = unit.TimeOnTaskMinutes
            }, cancellationToken);

        return id;
    }

    public async Task DeleteUnitAsync(
        string classId, string unitId, CancellationToken cancellationToken = default)
    {
        // Detach first, delete second. If the unit document went first and the
        // detach then failed, the materials would be stranded pointing at a
        // unit that no longer exists — invisible in the UI but still billable.
        var materials = await GetMaterialsAsync(classId, cancellationToken);

        foreach (var material in materials.Where(m => m.UnitId == unitId))
        {
            try
            {
                await _firestore.PatchFieldsAsync($"{MaterialsPath(classId)}/{material.Id}",
                    new Dictionary<string, object?> { ["UnitId"] = string.Empty },
                    cancellationToken);
            }
            catch (Exception ex)
            {
                // Better a material stuck in a deleted unit than a half-deleted
                // course. Log and continue; it resurfaces under "Module
                // information" on the next detach attempt.
                CrashLogger.Log($"ClassService.DetachMaterial:{material.Id}", ex);
            }
        }

        await _firestore.DeleteDocumentAsync($"{UnitsPath(classId)}/{unitId}", cancellationToken);
    }

    private static LearningUnit WithId(LearningUnit source, string id) => new()
    {
        Id = id,
        Title = source.Title,
        Description = source.Description,
        Order = source.Order,
        ActivityCount = source.ActivityCount,
        TimeOnTaskMinutes = source.TimeOnTaskMinutes,
        AuthorId = source.AuthorId,
        CreatedAt = source.CreatedAt
    };

    // ==================== progress ====================

    public async Task<ClassProgress> GetMyProgressAsync(
        string classId, CancellationToken cancellationToken = default)
    {
        var uid = _auth.CurrentSession?.UserId;
        if (string.IsNullOrEmpty(uid)) return new ClassProgress();

        try
        {
            var progress = await _firestore.GetDocumentAsync<ClassProgress>(
                $"classes/{classId}/progress/{uid}", cancellationToken);

            // No document yet is the normal state for a student who hasn't
            // ticked anything — an empty record, not an error.
            return progress ?? new ClassProgress();
        }
        catch (Exception ex)
        {
            // Progress is a nicety. Failing to read it must not stop the class
            // page loading its materials.
            CrashLogger.Log("ClassService.GetMyProgress", ex);
            return new ClassProgress();
        }
    }

    public async Task SetMaterialCompleteAsync(
        string classId, string materialId, bool complete, CancellationToken cancellationToken = default)
    {
        var uid = _auth.CurrentSession?.UserId
            ?? throw new InvalidOperationException("You need to be signed in to track progress.");

        var current = await GetMyProgressAsync(classId, cancellationToken);
        var ids = current.CompletedMaterialIds.ToList();

        if (complete)
        {
            if (ids.Contains(materialId, StringComparer.Ordinal)) return;
            ids.Add(materialId);
        }
        else
        {
            if (!ids.Remove(materialId)) return;
        }

        await _firestore.PatchFieldsAsync($"classes/{classId}/progress/{uid}",
            new Dictionary<string, object?>
            {
                ["CompletedMaterialIds"] = ids,
                ["UpdatedAt"] = DateTimeOffset.UtcNow
            }, cancellationToken);
    }

    // ==================== grades ====================

    public async Task<IReadOnlyList<ClassSubmission>> GetMySubmissionsForClassAsync(
        string classId, CancellationToken cancellationToken = default)
    {
        var uid = _auth.CurrentSession?.UserId;
        if (string.IsNullOrEmpty(uid)) return [];

        var assignments = await GetAssignmentsAsync(classId, cancellationToken);

        // One read per assignment. Fine at class scale (a term is tens, not
        // thousands) and it avoids a collection-group query, which would need
        // its own index and a rule that can't check class membership.
        //
        // Each result is paired with its assignment ID here, while we still
        // know it — the document itself doesn't record which assignment it
        // belongs to.
        var results = new List<ClassSubmission>();
        foreach (var assignment in assignments)
        {
            try
            {
                var submission = await _firestore.GetDocumentAsync<Submission>(
                    $"classes/{classId}/assignments/{assignment.Id}/submissions/{uid}",
                    cancellationToken);

                if (submission is not null)
                    results.Add(new ClassSubmission(assignment.Id, submission));
            }
            catch (Exception ex)
            {
                // One unreadable submission shouldn't blank the whole grade
                // list — show the marks we could load.
                CrashLogger.Log($"ClassService.GetMySubmission:{assignment.Id}", ex);
            }
        }

        return results;
    }

    private static CourseMaterial WithId(CourseMaterial source, string id) => new()
    {
        Id = id,
        Kind = source.Kind,
        Title = source.Title,
        Summary = source.Summary,
        Body = source.Body,
        Url = source.Url,
        StoragePath = source.StoragePath,
        FileName = source.FileName,
        FileSizeBytes = source.FileSizeBytes,
        Order = source.Order,
        AuthorId = source.AuthorId,
        AuthorName = source.AuthorName,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt
    };

    private static SchoolClass WithId(SchoolClass source, string id) => new()
    {
        Id = id,
        Name = source.Name,
        Subject = source.Subject,
        TeacherId = source.TeacherId,
        TeacherName = source.TeacherName,
        Room = source.Room,
        StudentIds = source.StudentIds
    };
}

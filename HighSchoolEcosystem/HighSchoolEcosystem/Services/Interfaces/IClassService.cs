using HighSchoolEcosystem.Models;

namespace HighSchoolEcosystem.Services.Interfaces;

/// <summary>
/// Classes, assignments and the timetable. Writes are gated by the Firestore
/// rules (a teacher may only touch classes where TeacherId is their own UID),
/// so this is a convenience layer, not the security boundary.
/// </summary>
public interface IClassService
{
    /// <summary>Every class in the school. Readable by any school member.</summary>
    Task<IReadOnlyList<SchoolClass>> GetAllClassesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Everyone with the Student role, for building class rosters. Requires a
    /// staff role — the rules let staff read the user directory.
    /// </summary>
    Task<IReadOnlyList<UserProfile>> GetStudentsAsync(CancellationToken cancellationToken = default);

    /// <summary>Classes owned by the signed-in teacher.</summary>
    Task<IReadOnlyList<SchoolClass>> GetMyClassesAsync(CancellationToken cancellationToken = default);

    /// <summary>Classes the signed-in user is enrolled in as a student.</summary>
    Task<IReadOnlyList<SchoolClass>> GetMyEnrolledClassesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Every assignment across the given classes, each paired with the class it
    /// belongs to, ordered by due date. One request per class — Firestore has no
    /// collection-group read the security rules could safely allow here.
    /// </summary>
    Task<IReadOnlyList<ClassAssignment>> GetAssignmentsForAsync(
        IReadOnlyList<SchoolClass> classes, CancellationToken cancellationToken = default);

    Task<SchoolClass?> GetClassAsync(string classId, CancellationToken cancellationToken = default);

    /// <summary>Creates a class owned by the signed-in teacher. Returns its new ID.</summary>
    Task<string> CreateClassAsync(string name, string subject, string room, CancellationToken cancellationToken = default);

    Task UpdateClassDetailsAsync(string classId, string name, string subject, string room, CancellationToken cancellationToken = default);

    /// <summary>Replaces the enrolled student list for a class.</summary>
    Task SetEnrolledStudentsAsync(string classId, IReadOnlyList<string> studentUids, CancellationToken cancellationToken = default);

    Task DeleteClassAsync(string classId, CancellationToken cancellationToken = default);

    // ---- assignments ----

    Task<IReadOnlyList<Assignment>> GetAssignmentsAsync(string classId, CancellationToken cancellationToken = default);
    /// <summary>Saves the assignment and returns its ID, new or existing.</summary>
    Task<string> SaveAssignmentAsync(string classId, Assignment assignment, CancellationToken cancellationToken = default);
    Task DeleteAssignmentAsync(string classId, string assignmentId, CancellationToken cancellationToken = default);

    // ---- multiple-choice questions ----

    /// <summary>
    /// Questions for an assignment, without answers. Readable by the class.
    /// </summary>
    Task<IReadOnlyList<HomeworkQuestion>> GetQuestionsAsync(string classId, string assignmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The correct option index per question ID. Teacher/admin only — students
    /// get PERMISSION_DENIED, which is the whole point of the split.
    /// </summary>
    Task<IReadOnlyDictionary<string, int>> GetAnswerKeyAsync(string classId, string assignmentId, CancellationToken cancellationToken = default);

    /// <summary>Writes the question and its answer key together.</summary>
    Task SaveQuestionAsync(string classId, string assignmentId, HomeworkQuestion question, int correctIndex, CancellationToken cancellationToken = default);

    Task DeleteQuestionAsync(string classId, string assignmentId, string questionId, CancellationToken cancellationToken = default);

    // ---- submissions ----

    /// <summary>The signed-in student's own submission, or null if they haven't submitted.</summary>
    Task<Submission?> GetMySubmissionAsync(string classId, string assignmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or replaces the signed-in student's submission. Writes only the
    /// fields a student is allowed to set — the rules reject any payload
    /// containing Grade or Feedback.
    /// </summary>
    Task SubmitAsync(string classId, string assignmentId, string text,
        IReadOnlyList<string> answers, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads the student's file and records it on their submission, alongside
    /// whatever text and answers they also gave.
    ///
    /// Separate from SubmitAsync because the upload can fail on its own and the
    /// caller needs to know that before the document write happens — a
    /// submission pointing at a file that isn't there is worse than no file.
    /// </summary>
    Task SubmitWithFileAsync(string classId, string assignmentId, string text,
        IReadOnlyList<string> answers, Stream fileContent, string fileName, string contentType,
        CancellationToken cancellationToken = default);

    // ---- marking ----

    /// <summary>
    /// Every student's submission for one assignment, for the teacher's marking
    /// screen. Readable by that class's teacher and admins; the rules deny
    /// everyone else.
    /// </summary>
    Task<IReadOnlyList<Submission>> GetSubmissionsForAssignmentAsync(string classId, string assignmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many submissions for each assignment in a class are still waiting on
    /// a person — the number behind the orange badge.
    ///
    /// Keyed by assignment ID; assignments with nothing waiting are omitted
    /// rather than mapped to zero, so a caller can test with TryGetValue.
    ///
    /// Costs one read per assignment, which is why it is a separate call: the
    /// screens that want it fetch it AFTER their list is on screen, so a class
    /// with a term of work behind it still paints immediately.
    /// </summary>
    Task<IReadOnlyDictionary<string, int>> GetNeedsMarkingCountsAsync(
        string classId, IReadOnlyList<string> assignmentIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Everything across a teacher's classes that is waiting on a person to
    /// read it, newest deadline first.
    ///
    /// Deliberately NOT every assignment ever set. Only work that can be handed
    /// in, and only where the deadline has passed inside the recent window — a
    /// teacher's dashboard would otherwise open dozens of connections to ask
    /// about homework from last term that nobody is going to mark now.
    /// </summary>
    Task<IReadOnlyList<MarkingQueueItem>> GetMarkingQueueAsync(
        IReadOnlyList<SchoolClass> classes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a teacher's mark and feedback. Writes TeacherMarkedAt so the
    /// work leaves the "needs marking" queue and the auto-marker's loop guard
    /// can tell a human grade from its own.
    /// </summary>
    Task SetGradeAsync(string classId, string assignmentId, string studentId, int grade, string feedback, CancellationToken cancellationToken = default);

    // ---- course materials ----

    /// <summary>
    /// Chapters, links and files for a class, in teacher-defined order.
    /// Readable by anyone enrolled; the rules deny everyone else.
    /// </summary>
    Task<IReadOnlyList<CourseMaterial>> GetMaterialsAsync(string classId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or replaces a material. Pass an empty Id to create. Returns the
    /// document ID either way.
    /// </summary>
    Task<string> SaveMaterialAsync(string classId, CourseMaterial material, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the material and, for an upload, its Storage object too — the
    /// document is the only record of where the file lives, so it has to go
    /// second.
    /// </summary>
    Task DeleteMaterialAsync(string classId, CourseMaterial material, CancellationToken cancellationToken = default);

    /// <summary>Swaps a material with its neighbour, for manual reordering.</summary>
    Task MoveMaterialAsync(string classId, CourseMaterial material, bool up, CancellationToken cancellationToken = default);

    // ---- learning units ----

    /// <summary>The course's units, in teacher-defined order.</summary>
    Task<IReadOnlyList<LearningUnit>> GetUnitsAsync(string classId, CancellationToken cancellationToken = default);

    /// <summary>Creates or updates a unit. Empty Id creates. Returns the document ID.</summary>
    Task<string> SaveUnitAsync(string classId, LearningUnit unit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a unit and detaches its materials rather than deleting them —
    /// losing a term's chapters because a unit was removed would be
    /// unrecoverable. Detached material reappears under "Module information".
    /// </summary>
    Task DeleteUnitAsync(string classId, string unitId, CancellationToken cancellationToken = default);

    // ---- progress ----

    /// <summary>The signed-in student's completion record for one class.</summary>
    Task<ClassProgress> GetMyProgressAsync(string classId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a material complete or not for the signed-in student. Writes the
    /// whole list back, which is safe because a student only ever has one
    /// device editing their own progress.
    /// </summary>
    Task SetMaterialCompleteAsync(string classId, string materialId, bool complete, CancellationToken cancellationToken = default);

    // ---- grades ----

    /// <summary>
    /// The signed-in student's submissions across one class, so the class page
    /// can show their marks. Assignments the student never submitted are
    /// absent, not zero — never submitted and marked zero are different facts.
    /// </summary>
    Task<IReadOnlyList<ClassSubmission>> GetMySubmissionsForClassAsync(string classId, CancellationToken cancellationToken = default);

    // ---- class announcements ----

    /// <summary>
    /// Notices posted to one class, newest first with pinned ones on top.
    /// Readable by the class's teacher, its students, and their parents.
    /// </summary>
    /// <param name="includeExpired">
    /// True for the teacher's own management view, so they can see and extend a
    /// notice that has lapsed. False everywhere a student or parent reads.
    /// </param>
    Task<IReadOnlyList<Announcement>> GetClassAnnouncementsAsync(string classId, bool includeExpired = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates a class notice. Empty Id creates. Only the class's own
    /// teacher (or an admin) may write — the rules enforce it.
    /// </summary>
    Task<string> SaveClassAnnouncementAsync(string classId, string className, Announcement announcement, CancellationToken cancellationToken = default);

    Task DeleteClassAnnouncementAsync(string classId, string announcementId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every class notice across a set of classes, for a merged dashboard feed.
    /// Each result carries its ClassId and ClassName so the feed can label it.
    /// </summary>
    Task<IReadOnlyList<Announcement>> GetAnnouncementsForClassesAsync(IReadOnlyList<SchoolClass> classes, CancellationToken cancellationToken = default);

    // ---- parents ----

    /// <summary>
    /// Classes a given student is enrolled in. Used by the parent dashboard,
    /// where the caller is not the student — GetMyEnrolledClassesAsync keys off
    /// the signed-in UID and would return the parent's own (empty) enrolment.
    /// </summary>
    Task<IReadOnlyList<SchoolClass>> GetClassesForStudentAsync(string studentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// One student's submissions for one class, readable by a linked parent as
    /// well as the student themselves.
    /// </summary>
    Task<IReadOnlyList<ClassSubmission>> GetSubmissionsForStudentAsync(string classId, string studentId, CancellationToken cancellationToken = default);

    // ---- timetable ----

    Task<IReadOnlyList<TimetableEntry>> GetTimetableAsync(CancellationToken cancellationToken = default);
    Task SaveTimetableEntryAsync(TimetableEntry entry, CancellationToken cancellationToken = default);
    Task DeleteTimetableEntryAsync(string entryId, CancellationToken cancellationToken = default);
}

namespace HighSchoolEcosystem.Models;

/// <summary>
/// Mirrors /conversations/{teacherId}_{parentId}_{studentId}: one private thread
/// between a teacher and a parent about one child. The rules only allow it to
/// be opened while that child is in that teacher's class and linked to that
/// parent. Admins can read every thread.
///
/// Created with an explicit field list (ConversationService.OpenAsync) — never
/// SetDocumentAsync, which would also write Id and the computed properties.
/// </summary>
public sealed class Conversation
{
    public string Id { get; init; } = string.Empty;

    public string TeacherId { get; init; } = string.Empty;
    public string TeacherName { get; init; } = string.Empty;
    public string ParentId { get; init; } = string.Empty;
    public string ParentName { get; init; } = string.Empty;
    public string StudentId { get; init; } = string.Empty;
    public string StudentName { get; init; } = string.Empty;

    /// <summary>The class that made this conversation allowed.</summary>
    public string ClassId { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;

    /// <summary>[TeacherId, ParentId] — what the read rule and the list query use.</summary>
    public List<string> ParticipantIds { get; init; } = [];

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset LastMessageAt { get; init; }
    public string LastMessagePreview { get; init; } = string.Empty;
    public string LastSenderId { get; init; } = string.Empty;

    public static string IdFor(string teacherId, string parentId, string studentId)
        => $"{teacherId}_{parentId}_{studentId}";
}

/// <summary>Mirrors /conversations/{id}/messages/{messageId}. Text only.</summary>
public sealed class DirectMessage
{
    public string Id { get; init; } = string.Empty;
    public string AuthorId { get; init; } = string.Empty;
    public string AuthorName { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public DateTimeOffset SentAt { get; init; }
}

/// <summary>
/// Someone the signed-in user may start a conversation with, and the child and
/// class that make it allowed. Worked out from class rosters and parents'
/// LinkedStudentIds — never stored.
/// </summary>
public sealed record ConversationContact(
    string TeacherId, string TeacherName,
    string ParentId, string ParentName,
    string StudentId, string StudentName,
    string ClassId, string ClassName)
{
    public string ConversationId => Conversation.IdFor(TeacherId, ParentId, StudentId);
}

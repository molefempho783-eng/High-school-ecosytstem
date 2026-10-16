using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.Services;

/// <summary>See <see cref="IConversationService"/>.</summary>
public sealed class ConversationService : IConversationService
{
    private const string Collection = "conversations";

    private readonly IFirestoreClient _firestore;
    private readonly IFirebaseAuthService _auth;
    private readonly IUserProfileService _profiles;

    private UserProfile? _me;

    public ConversationService(IFirestoreClient firestore, IFirebaseAuthService auth, IUserProfileService profiles)
    {
        _firestore = firestore;
        _auth = auth;
        _profiles = profiles;
    }

    public string CurrentUserId => _auth.CurrentSession?.UserId ?? string.Empty;

    private async Task<UserProfile?> MeAsync(CancellationToken cancellationToken)
        => _me ??= await _profiles.GetOrCreateCurrentProfileAsync(cancellationToken);

    private static string NameOf(UserProfile? user, string fallback)
        => string.IsNullOrWhiteSpace(user?.DisplayName) ? fallback : user!.DisplayName;

    // ---------------------------------------------------------------- threads

    public async Task<IReadOnlyList<Conversation>> GetMyConversationsAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(CurrentUserId)) return [];

        // array-contains on the participant list is what the read rule can
        // prove; sorted here rather than with orderBy, which would need a
        // composite index.
        var documents = await _firestore.QueryWhereAsync<Conversation>(
            Collection, nameof(Conversation.ParticipantIds), "ARRAY_CONTAINS", CurrentUserId, cancellationToken);

        return documents
            .Select(d => WithId(d.Value, d.Id))
            .OrderByDescending(c => c.LastMessageAt > c.CreatedAt ? c.LastMessageAt : c.CreatedAt)
            .ToList();
    }

    public async Task<Conversation?> GetAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var value = await _firestore.GetDocumentAsync<Conversation>($"{Collection}/{conversationId}", cancellationToken);
        return value is null ? null : WithId(value, conversationId);
    }

    public async Task<Conversation> OpenAsync(ConversationContact contact, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(contact.ConversationId, cancellationToken);
        if (existing is not null) return existing;

        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation
        {
            Id = contact.ConversationId,
            TeacherId = contact.TeacherId,
            TeacherName = contact.TeacherName,
            ParentId = contact.ParentId,
            ParentName = contact.ParentName,
            StudentId = contact.StudentId,
            StudentName = contact.StudentName,
            ClassId = contact.ClassId,
            ClassName = contact.ClassName,
            ParticipantIds = [contact.TeacherId, contact.ParentId],
            CreatedAt = now
        };

        // The rule re-checks the relationship (class roster + parent link) and
        // that the ID is built from exactly these three people.
        await _firestore.PatchFieldsAsync($"{Collection}/{conversation.Id}", new Dictionary<string, object?>
        {
            ["TeacherId"] = conversation.TeacherId,
            ["TeacherName"] = conversation.TeacherName,
            ["ParentId"] = conversation.ParentId,
            ["ParentName"] = conversation.ParentName,
            ["StudentId"] = conversation.StudentId,
            ["StudentName"] = conversation.StudentName,
            ["ClassId"] = conversation.ClassId,
            ["ClassName"] = conversation.ClassName,
            ["ParticipantIds"] = conversation.ParticipantIds,
            ["CreatedAt"] = now,
            ["LastMessageAt"] = now,
            ["LastMessagePreview"] = string.Empty,
            ["LastSenderId"] = string.Empty
        }, cancellationToken);

        return conversation;
    }

    // --------------------------------------------------------------- contacts

    public async Task<IReadOnlyList<ConversationContact>> GetContactsAsync(CancellationToken cancellationToken = default)
    {
        var me = await MeAsync(cancellationToken);
        if (me is null) return [];

        var contacts = new List<ConversationContact>();
        if (me.HasRole(Role.Parent)) contacts.AddRange(await TeachersOfMyChildrenAsync(me, cancellationToken));
        if (me.HasRole(Role.Teacher)) contacts.AddRange(await ParentsOfMyStudentsAsync(me, cancellationToken));

        return contacts
            .Where(c => c.TeacherId != c.ParentId) // a teacher whose own child is in their class
            .DistinctBy(c => c.ConversationId)
            .OrderBy(c => c.StudentName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(c => c.ClassName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>For a parent: each linked child's class teachers.</summary>
    private async Task<List<ConversationContact>> TeachersOfMyChildrenAsync(UserProfile me, CancellationToken ct)
    {
        var result = new List<ConversationContact>();
        foreach (var childId in me.LinkedStudentIds.Distinct())
        {
            var child = await _firestore.GetDocumentAsync<UserProfile>($"users/{childId}", ct);
            var childName = NameOf(child, "Your child");

            var classes = await _firestore.QueryWhereAsync<SchoolClass>(
                "classes", nameof(SchoolClass.StudentIds), "ARRAY_CONTAINS", childId, ct);

            foreach (var c in classes)
            {
                if (string.IsNullOrEmpty(c.Value.TeacherId)) continue;
                result.Add(new ConversationContact(
                    c.Value.TeacherId, string.IsNullOrWhiteSpace(c.Value.TeacherName) ? "Teacher" : c.Value.TeacherName,
                    me.Uid, NameOf(me, "Parent"),
                    childId, childName,
                    c.Id, ClassLabel(c.Value)));
            }
        }
        return result;
    }

    /// <summary>For a teacher: the parents of every student in their classes.</summary>
    private async Task<List<ConversationContact>> ParentsOfMyStudentsAsync(UserProfile me, CancellationToken ct)
    {
        var result = new List<ConversationContact>();
        var classes = await _firestore.QueryWhereAsync<SchoolClass>(
            "classes", nameof(SchoolClass.TeacherId), "EQUAL", me.Uid, ct);

        foreach (var c in classes)
        {
            var students = c.Value.StudentIds.Distinct().ToList();
            if (students.Count == 0) continue;

            // Parents of this class, 30 students at a time (the query's limit).
            var parents = new List<FirestoreDocument<UserProfile>>();
            for (var i = 0; i < students.Count; i += 30)
            {
                parents.AddRange(await _firestore.QueryWhereAsync<UserProfile>(
                    "users", nameof(UserProfile.LinkedStudentIds), "ARRAY_CONTAINS_ANY",
                    students.Skip(i).Take(30).ToList(), ct));
            }
            if (parents.Count == 0) continue;

            // Names only for students who actually have a linked parent.
            var studentNames = new Dictionary<string, string>();
            foreach (var studentId in parents.SelectMany(p => p.Value.LinkedStudentIds).Intersect(students).Distinct())
            {
                var student = await _firestore.GetDocumentAsync<UserProfile>($"users/{studentId}", ct);
                studentNames[studentId] = NameOf(student, "Student");
            }

            foreach (var parent in parents)
            foreach (var studentId in parent.Value.LinkedStudentIds.Intersect(students))
            {
                result.Add(new ConversationContact(
                    me.Uid, NameOf(me, "Teacher"),
                    parent.Id, NameOf(parent.Value, "Parent"),
                    studentId, studentNames.GetValueOrDefault(studentId, "Student"),
                    c.Id, ClassLabel(c.Value)));
            }
        }
        return result;
    }

    private static string ClassLabel(SchoolClass c)
        => string.IsNullOrWhiteSpace(c.Subject) ? c.Name : c.Subject;

    // --------------------------------------------------------------- messages

    private static string MessagesPath(string conversationId) => $"{Collection}/{conversationId}/messages";

    public async Task<IReadOnlyList<DirectMessage>> GetRecentMessagesAsync(
        string conversationId, int count, CancellationToken cancellationToken = default)
    {
        var documents = await _firestore.QueryByTimeAsync<DirectMessage>(
            MessagesPath(conversationId), nameof(DirectMessage.SentAt), newestFirst: true, limit: count,
            cancellationToken: cancellationToken);
        return documents.Select(d => WithId(d.Value, d.Id)).Reverse().ToList();
    }

    public async Task<IReadOnlyList<DirectMessage>> GetMessagesSinceAsync(
        string conversationId, DateTimeOffset after, CancellationToken cancellationToken = default)
    {
        var documents = await _firestore.QueryByTimeAsync<DirectMessage>(
            MessagesPath(conversationId), nameof(DirectMessage.SentAt), newestFirst: false, limit: 100,
            after: after, cancellationToken: cancellationToken);
        return documents.Select(d => WithId(d.Value, d.Id)).ToList();
    }

    public async Task<DirectMessage> SendAsync(
        Conversation conversation, string text, CancellationToken cancellationToken = default)
    {
        text = text.Trim();
        if (text.Length == 0) throw new InvalidOperationException("Type a message first.");
        if (text.Length > IConversationService.MaxLength)
            throw new InvalidOperationException($"Messages can be up to {IConversationService.MaxLength} characters.");

        var me = await MeAsync(cancellationToken);
        var message = new DirectMessage
        {
            Id = Guid.NewGuid().ToString("n"),
            AuthorId = CurrentUserId,
            AuthorName = NameOf(me, "Someone"),
            Text = text,
            SentAt = DateTimeOffset.UtcNow
        };

        await _firestore.PatchFieldsAsync($"{MessagesPath(conversation.Id)}/{message.Id}", new Dictionary<string, object?>
        {
            ["AuthorId"] = message.AuthorId,
            ["AuthorName"] = message.AuthorName,
            ["Text"] = message.Text,
            ["SentAt"] = message.SentAt
        }, cancellationToken);

        // The thread summary, for the inbox list and unread dots. Best effort:
        // the message itself is already safely written.
        try
        {
            await _firestore.PatchFieldsAsync($"{Collection}/{conversation.Id}", new Dictionary<string, object?>
            {
                ["LastMessageAt"] = message.SentAt,
                ["LastMessagePreview"] = text.Length > 120 ? text[..119] + "…" : text,
                ["LastSenderId"] = CurrentUserId
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Conversation.Summary", ex);
        }

        MarkRead(conversation.Id);
        return message;
    }

    // ----------------------------------------------------------------- unread

    private string? ReadKey(string conversationId)
        => string.IsNullOrEmpty(CurrentUserId) ? null : $"dm-read:{CurrentUserId}:{conversationId}";

    public void MarkRead(string conversationId)
    {
        if (ReadKey(conversationId) is not { } key) return;
        try { Preferences.Default.Set(key, DateTimeOffset.UtcNow.UtcTicks); }
        catch (Exception ex) { CrashLogger.Log("Conversation.MarkRead", ex); }
    }

    public bool IsUnread(Conversation conversation)
    {
        if (string.IsNullOrEmpty(conversation.LastSenderId) || conversation.LastSenderId == CurrentUserId) return false;
        if (ReadKey(conversation.Id) is not { } key) return false;

        long ticks = 0;
        try { ticks = Preferences.Default.Get(key, 0L); }
        catch (Exception ex) { CrashLogger.Log("Conversation.ReadMarker", ex); }

        return conversation.LastMessageAt.UtcTicks > ticks;
    }

    // ---------------------------------------------------------------- helpers

    private static Conversation WithId(Conversation v, string id) => new()
    {
        Id = id,
        TeacherId = v.TeacherId, TeacherName = v.TeacherName,
        ParentId = v.ParentId, ParentName = v.ParentName,
        StudentId = v.StudentId, StudentName = v.StudentName,
        ClassId = v.ClassId, ClassName = v.ClassName,
        ParticipantIds = v.ParticipantIds,
        CreatedAt = v.CreatedAt,
        LastMessageAt = v.LastMessageAt,
        LastMessagePreview = v.LastMessagePreview,
        LastSenderId = v.LastSenderId
    };

    private static DirectMessage WithId(DirectMessage v, string id) => new()
    {
        Id = id, AuthorId = v.AuthorId, AuthorName = v.AuthorName, Text = v.Text, SentAt = v.SentAt
    };
}

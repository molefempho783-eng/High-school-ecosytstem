using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.Services;

/// <summary>See <see cref="IClassChatService"/>.</summary>
public sealed class ClassChatService : IClassChatService
{
    private readonly IFirestoreClient _firestore;
    private readonly IFirebaseAuthService _auth;
    private readonly IUserProfileService _profiles;
    private readonly IStorageService _storage;

    /// <summary>The sender's display name, read once rather than per message.</summary>
    private string? _displayName;

    public ClassChatService(IFirestoreClient firestore, IFirebaseAuthService auth,
        IUserProfileService profiles, IStorageService storage)
    {
        _firestore = firestore;
        _auth = auth;
        _profiles = profiles;
        _storage = storage;
    }

    private static string MessagesPath(string classId) => $"classes/{classId}/messages";

    public async Task<IReadOnlyList<ClassMessage>> GetRecentAsync(
        string classId, int count, CancellationToken cancellationToken = default)
    {
        // Newest N, then flipped so the screen reads top to bottom in time order.
        var documents = await _firestore.QueryByTimeAsync<ClassMessage>(
            MessagesPath(classId), nameof(ClassMessage.SentAt), newestFirst: true, limit: count,
            cancellationToken: cancellationToken);

        return documents.Select(d => WithId(d.Value, d.Id)).Reverse().ToList();
    }

    public async Task<IReadOnlyList<ClassMessage>> GetSinceAsync(
        string classId, DateTimeOffset after, CancellationToken cancellationToken = default)
    {
        var documents = await _firestore.QueryByTimeAsync<ClassMessage>(
            MessagesPath(classId), nameof(ClassMessage.SentAt), newestFirst: false, limit: 100,
            after: after, cancellationToken: cancellationToken);

        return documents.Select(d => WithId(d.Value, d.Id)).ToList();
    }

    public async Task<ClassMessage> SendAsync(
        string classId, string text, bool asTeacher, ChatAttachment? attachment = null,
        CancellationToken cancellationToken = default)
    {
        var session = _auth.CurrentSession
            ?? throw new InvalidOperationException("You need to be signed in to send a message.");

        text = text.Trim();
        if (text.Length == 0 && attachment is null) throw new InvalidOperationException("Type a message first.");
        if (text.Length > IClassChatService.MaxLength)
            throw new InvalidOperationException($"Messages can be up to {IClassChatService.MaxLength} characters.");
        if (attachment is not null && attachment.Bytes.LongLength > IClassChatService.MaxAttachmentBytes)
            throw new InvalidOperationException("Attachments can be up to 15 MB.");
        if (attachment is not null && !_storage.IsConfigured)
            throw new InvalidOperationException("Sending files isn't set up for this school yet.");

        _displayName ??= await ResolveNameAsync(session, cancellationToken);

        // Upload FIRST: a message pointing at a missing file is worse than no
        // message. If the message write then fails, the file is removed again.
        UploadResult? upload = null;
        if (attachment is not null)
        {
            using var stream = new MemoryStream(attachment.Bytes);
            upload = await _storage.UploadChatAttachmentAsync(
                classId, session.UserId, attachment.FileName, stream, attachment.ContentType, cancellationToken);
        }

        var message = new ClassMessage
        {
            Id = Guid.NewGuid().ToString("n"),
            AuthorId = session.UserId,
            AuthorName = _displayName,
            AuthorRole = asTeacher ? "Teacher" : "Student",
            Text = text,
            SentAt = DateTimeOffset.UtcNow,
            AttachmentUrl = upload?.DownloadUrl ?? string.Empty,
            AttachmentPath = upload?.StoragePath ?? string.Empty,
            AttachmentName = attachment?.FileName ?? string.Empty,
            AttachmentKind = attachment is null ? string.Empty : attachment.IsImage ? "Image" : "File",
            AttachmentSize = upload is null ? 0 : (upload.SizeBytes > 0 ? upload.SizeBytes : attachment!.Bytes.LongLength)
        };

        // Exactly the fields the rule allows — see ClassMessage.
        var fields = new Dictionary<string, object?>
        {
            ["AuthorId"] = message.AuthorId,
            ["AuthorName"] = message.AuthorName,
            ["AuthorRole"] = message.AuthorRole,
            ["Text"] = message.Text,
            ["SentAt"] = message.SentAt
        };
        if (message.HasAttachment)
        {
            fields["AttachmentUrl"] = message.AttachmentUrl;
            fields["AttachmentPath"] = message.AttachmentPath;
            fields["AttachmentName"] = message.AttachmentName;
            fields["AttachmentKind"] = message.AttachmentKind;
            fields["AttachmentSize"] = message.AttachmentSize;
        }

        try
        {
            await _firestore.PatchFieldsAsync($"{MessagesPath(classId)}/{message.Id}", fields, cancellationToken);
        }
        catch
        {
            if (upload is not null)
            {
                try { await _storage.DeleteAsync(upload.StoragePath, CancellationToken.None); }
                catch (Exception cleanup) { CrashLogger.Log("ClassChat.CleanupUpload", cleanup); }
            }
            throw;
        }

        return message;
    }

    // ---- unread ----

    private string? ReadKey(string classId)
        => _auth.CurrentSession?.UserId is { Length: > 0 } uid ? $"chat-read:{uid}:{classId}" : null;

    public void MarkRead(string classId)
    {
        if (ReadKey(classId) is not { } key) return;
        try { Preferences.Default.Set(key, DateTimeOffset.UtcNow.UtcTicks); }
        catch (Exception ex) { CrashLogger.Log("ClassChat.MarkRead", ex); }
    }

    public async Task<int> GetUnreadCountAsync(string classId, CancellationToken cancellationToken = default)
    {
        if (ReadKey(classId) is not { } key) return 0;

        long ticks = 0;
        try { ticks = Preferences.Default.Get(key, 0L); }
        catch (Exception ex) { CrashLogger.Log("ClassChat.ReadMarker", ex); }

        // Never opened on this device: everything counts as unread, capped by
        // what the badge can show anyway.
        DateTimeOffset? since = ticks > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;

        return await _firestore.CountByTimeAsync(MessagesPath(classId), nameof(ClassMessage.SentAt), since, cancellationToken);
    }

    public async Task DeleteAsync(string classId, ClassMessage message, CancellationToken cancellationToken = default)
    {
        // The file first, while the message that points at it still exists —
        // the other order can leave an object nothing references.
        if (!string.IsNullOrWhiteSpace(message.AttachmentPath))
        {
            try { await _storage.DeleteAsync(message.AttachmentPath, cancellationToken); }
            catch (Exception ex)
            {
                // A file we couldn't remove mustn't stop the message going.
                CrashLogger.Log($"ClassChat.DeleteAttachment:{message.Id}", ex);
            }
        }

        await _firestore.DeleteDocumentAsync($"{MessagesPath(classId)}/{message.Id}", cancellationToken);
    }

    private async Task<string> ResolveNameAsync(AuthSession session, CancellationToken cancellationToken)
    {
        try
        {
            var profile = await _profiles.GetOrCreateCurrentProfileAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(profile?.DisplayName)) return profile!.DisplayName;
        }
        catch (Exception ex)
        {
            CrashLogger.Log("ClassChat.Name", ex);
        }
        return session.DisplayName ?? session.Email ?? "Someone";
    }

    private static ClassMessage WithId(ClassMessage v, string id) => new()
    {
        Id = id,
        AuthorId = v.AuthorId,
        AuthorName = v.AuthorName,
        AuthorRole = v.AuthorRole,
        Text = v.Text,
        SentAt = v.SentAt,
        AttachmentUrl = v.AttachmentUrl,
        AttachmentPath = v.AttachmentPath,
        AttachmentName = v.AttachmentName,
        AttachmentKind = v.AttachmentKind,
        AttachmentSize = v.AttachmentSize
    };
}

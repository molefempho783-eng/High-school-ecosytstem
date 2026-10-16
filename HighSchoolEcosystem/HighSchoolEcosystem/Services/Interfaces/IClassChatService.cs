using HighSchoolEcosystem.Models;

namespace HighSchoolEcosystem.Services.Interfaces;

/// <summary>
/// A class's group chat: its students and its teacher, nobody else, and no
/// one-to-one messages. Membership is enforced in firestore.rules.
/// </summary>
public interface IClassChatService
{
    /// <summary>Longest message the rules accept.</summary>
    const int MaxLength = 1000;

    /// <summary>Largest attachment storage.rules accepts (15 MB).</summary>
    const long MaxAttachmentBytes = 15L * 1024 * 1024;

    /// <summary>The most recent messages, oldest first, ready to display.</summary>
    Task<IReadOnlyList<ClassMessage>> GetRecentAsync(string classId, int count,
        CancellationToken cancellationToken = default);

    /// <summary>Messages sent after <paramref name="after"/>, oldest first.</summary>
    Task<IReadOnlyList<ClassMessage>> GetSinceAsync(string classId, DateTimeOffset after,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts as the signed-in user: text, an attachment, or both. The attachment
    /// is uploaded first, so a message never points at a file that isn't there.
    /// Returns the sent message.
    /// </summary>
    Task<ClassMessage> SendAsync(string classId, string text, bool asTeacher,
        ChatAttachment? attachment = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that the signed-in user has seen this class's chat up to now.
    /// Kept on the device (Preferences), so reading on a phone doesn't clear the
    /// badge on a laptop — a deliberate trade for needing no extra writes or rules.
    /// </summary>
    void MarkRead(string classId);

    /// <summary>
    /// Messages sent since this user last read the chat — the circle on the
    /// class card. One COUNT read per class, not one per message.
    /// </summary>
    Task<int> GetUnreadCountAsync(string classId, CancellationToken cancellationToken = default);

    /// <summary>Deletes the message, and its attachment first.</summary>
    Task DeleteAsync(string classId, ClassMessage message, CancellationToken cancellationToken = default);
}

using HighSchoolEcosystem.Models;

namespace HighSchoolEcosystem.Services.Interfaces;

/// <summary>
/// Private teacher-parent messaging. A parent can reach the teachers of their
/// linked children's classes; a teacher can reach the parents of the students
/// in their classes. Someone who is both (a teacher who is also a parent) gets
/// both lists. The relationship is re-checked by firestore.rules when a thread
/// is opened, so these lists are a convenience, not the boundary.
/// </summary>
public interface IConversationService
{
    /// <summary>Longest message the rules accept.</summary>
    const int MaxLength = 2000;

    /// <summary>The signed-in user's threads, most recent activity first.</summary>
    Task<IReadOnlyList<Conversation>> GetMyConversationsAsync(CancellationToken cancellationToken = default);

    /// <summary>Everyone the user may start a thread with.</summary>
    Task<IReadOnlyList<ConversationContact>> GetContactsAsync(CancellationToken cancellationToken = default);

    Task<Conversation?> GetAsync(string conversationId, CancellationToken cancellationToken = default);

    /// <summary>The thread for this contact, created if it doesn't exist yet.</summary>
    Task<Conversation> OpenAsync(ConversationContact contact, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DirectMessage>> GetRecentMessagesAsync(string conversationId, int count,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DirectMessage>> GetMessagesSinceAsync(string conversationId, DateTimeOffset after,
        CancellationToken cancellationToken = default);

    Task<DirectMessage> SendAsync(Conversation conversation, string text, CancellationToken cancellationToken = default);

    /// <summary>Records that this thread has been read up to now, on this device.</summary>
    void MarkRead(string conversationId);

    /// <summary>
    /// True when the other person has written since this device last read the
    /// thread. Costs no reads: it compares the thread's LastMessageAt with a
    /// local marker.
    /// </summary>
    bool IsUnread(Conversation conversation);

    /// <summary>The signed-in user's ID, for telling "mine" from "theirs".</summary>
    string CurrentUserId { get; }
}

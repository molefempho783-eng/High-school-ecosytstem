namespace HighSchoolEcosystem.Models;

/// <summary>
/// Mirrors /classes/{classId}/messages/{messageId}: one message in a class's
/// group chat — text, an attachment (picture or file), or both.
///
/// Written with an explicit field list (ClassChatService.SendAsync), never
/// SetDocumentAsync — the rule only accepts the listed fields, and the
/// converter would also send Id and the computed properties.
/// </summary>
public sealed class ClassMessage
{
    public string Id { get; init; } = string.Empty;
    public string AuthorId { get; init; } = string.Empty;
    public string AuthorName { get; init; } = string.Empty;

    /// <summary>"Teacher" or "Student". The rules check it against the class.</summary>
    public string AuthorRole { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;
    public DateTimeOffset SentAt { get; init; }

    // ---- attachment (all empty when there isn't one) ----

    /// <summary>Tokenised download URL, so an Image can bind straight to it.</summary>
    public string AttachmentUrl { get; init; } = string.Empty;

    /// <summary>
    /// The Storage object path, classes/{classId}/chat/{senderId}/{file}. Kept so
    /// deleting the message deletes the file — otherwise it stays in the bucket,
    /// billable, with nothing pointing at it.
    /// </summary>
    public string AttachmentPath { get; init; } = string.Empty;

    /// <summary>The original file name, shown on a file card.</summary>
    public string AttachmentName { get; init; } = string.Empty;

    /// <summary>"Image" (shown inline) or "File" (shown as a card).</summary>
    public string AttachmentKind { get; init; } = string.Empty;

    public long AttachmentSize { get; init; }

    public bool HasAttachment => !string.IsNullOrWhiteSpace(AttachmentUrl);
    public bool IsImage => HasAttachment && AttachmentKind == "Image";
    public bool IsFile => HasAttachment && !IsImage;
}

/// <summary>A picture or file picked in the chat, waiting to be sent.</summary>
public sealed record ChatAttachment(byte[] Bytes, string FileName, string ContentType)
{
    public bool IsImage => ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}

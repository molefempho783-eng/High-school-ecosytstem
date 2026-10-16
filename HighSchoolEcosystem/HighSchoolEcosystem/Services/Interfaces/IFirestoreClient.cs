namespace HighSchoolEcosystem.Services.Interfaces;

/// <summary>A document and its Firestore ID (the last segment of its path).</summary>
public sealed record FirestoreDocument<T>(string Id, T Value);

/// <summary>
/// Thin wrapper over the Cloud Firestore REST API. Used instead of a native
/// Firestore SDK so the same code path works on all four MAUI targets
/// (Android, iOS, Windows, MacCatalyst). This app links no native Firebase
/// SDKs at all — auth goes over REST too (see FirebaseAuthService).
/// </summary>
public interface IFirestoreClient
{
    /// <summary>
    /// Reads a single document, e.g. "users/uid123" or "classes/abc123".
    /// Returns null if the document doesn't exist.
    /// </summary>
    Task<T?> GetDocumentAsync<T>(string documentPath, CancellationToken cancellationToken = default) where T : class, new();

    /// <summary>
    /// Creates or merges a document at the given path with the given model's properties.
    /// </summary>
    Task SetDocumentAsync<T>(string documentPath, T value, CancellationToken cancellationToken = default) where T : notnull;

    /// <summary>
    /// Lists every document in a collection, following pagination. The security
    /// rules are evaluated per document, so this only returns what the caller is
    /// allowed to read — and fails outright if any returned document is denied.
    /// </summary>
    Task<IReadOnlyList<FirestoreDocument<T>>> ListDocumentsAsync<T>(string collectionPath, CancellationToken cancellationToken = default) where T : class, new();

    /// <summary>
    /// Reads at most <paramref name="limit"/> documents from a collection, ordered
    /// by one timestamp field. With <paramref name="after"/>, only documents whose
    /// field is later than it. Unlike <see cref="ListDocumentsAsync"/> this never
    /// reads the whole collection, which is what makes polling a chat affordable.
    /// A single-field order and range need no composite index.
    /// </summary>
    Task<IReadOnlyList<FirestoreDocument<T>>> QueryByTimeAsync<T>(
        string collectionPath, string timeField, bool newestFirst, int limit,
        DateTimeOffset? after = null, CancellationToken cancellationToken = default) where T : class, new();

    /// <summary>
    /// Documents matching one filter, e.g. ("ParticipantIds", "ARRAY_CONTAINS", uid)
    /// or ("LinkedStudentIds", "ARRAY_CONTAINS_ANY", up-to-30 ids) or
    /// ("TeacherId", "EQUAL", uid). Single-field filters need no composite
    /// index; sort the results in code rather than adding an orderBy.
    /// </summary>
    Task<IReadOnlyList<FirestoreDocument<T>>> QueryWhereAsync<T>(
        string collectionPath, string field, string op, object value,
        CancellationToken cancellationToken = default) where T : class, new();

    /// <summary>
    /// Counts documents whose timestamp field is later than <paramref name="after"/>
    /// (or all of them when it's null) with a server-side COUNT aggregation —
    /// billed as one read per 1,000 documents counted, rather than one per
    /// document, which is what makes an unread badge per class affordable.
    /// </summary>
    Task<int> CountByTimeAsync(string collectionPath, string timeField, DateTimeOffset? after,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates only the named fields, leaving everything else in the document
    /// untouched. Use in preference to <see cref="SetDocumentAsync"/> when
    /// editing someone else's document.
    /// </summary>
    Task PatchFieldsAsync(string documentPath, IReadOnlyDictionary<string, object?> fields, CancellationToken cancellationToken = default);

    Task DeleteDocumentAsync(string documentPath, CancellationToken cancellationToken = default);
}

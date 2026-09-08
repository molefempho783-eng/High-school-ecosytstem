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
    /// Updates only the named fields, leaving everything else in the document
    /// untouched. Use in preference to <see cref="SetDocumentAsync"/> when
    /// editing someone else's document.
    /// </summary>
    Task PatchFieldsAsync(string documentPath, IReadOnlyDictionary<string, object?> fields, CancellationToken cancellationToken = default);

    Task DeleteDocumentAsync(string documentPath, CancellationToken cancellationToken = default);
}

namespace HighSchoolEcosystem.Services.Interfaces;

/// <summary>The result of a successful upload.</summary>
/// <param name="DownloadUrl">A tokenised URL a student can open directly.</param>
/// <param name="StoragePath">
/// The object path, kept so the material can delete its own file later.
/// Without it an unreferenced object stays in the bucket, billable forever.
/// </param>
public sealed record UploadResult(string DownloadUrl, string StoragePath, long SizeBytes);

/// <summary>
/// Firebase Cloud Storage over REST, matching the rest of the app — the native
/// SDKs are Android/iOS only and would break the Windows build.
/// </summary>
public interface IStorageService
{
    /// <summary>False when no bucket is configured; the UI hides upload entirely.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Uploads a class material file. The object path is
    /// classes/{classId}/materials/{fileName}, which is exactly what
    /// storage.rules gates on — don't change it without changing the rules.
    /// </summary>
    Task<UploadResult> UploadClassMaterialAsync(
        string classId, string fileName, Stream content, string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a student's own submission file. The object path is
    /// classes/{classId}/submissions/{assignmentId}/{studentId}/{fileName},
    /// which is what storage.rules gates on — don't change it without changing
    /// the rules.
    /// </summary>
    Task<UploadResult> UploadSubmissionAsync(
        string classId, string assignmentId, string studentId,
        string fileName, Stream content, string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an object. Swallows a 404: the file already being gone is the
    /// outcome the caller wanted, and throwing would block deleting the
    /// material document and orphan it instead.
    /// </summary>
    Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default);
}

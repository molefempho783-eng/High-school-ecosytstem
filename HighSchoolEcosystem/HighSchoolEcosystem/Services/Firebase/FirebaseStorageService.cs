using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using HighSchoolEcosystem.Services.Interfaces;
// CrashLogger lives in the parent namespace, which isn't implicitly in scope
// from Services.Firebase.
using HighSchoolEcosystem.Services;

namespace HighSchoolEcosystem.Services.Firebase;

/// <summary>
/// Cloud Storage over the REST API. REST rather than Plugin.Firebase for the
/// same reason auth and Firestore are: the native SDKs throw
/// PlatformNotSupportedException on Windows, and the app has to run there.
/// </summary>
public sealed class FirebaseStorageService : IStorageService
{
    private readonly HttpClient _http;
    private readonly FirebaseEndpoints _endpoints;
    private readonly IFirebaseAuthService _auth;

    public FirebaseStorageService(
        HttpClient http, FirebaseEndpoints endpoints, IFirebaseAuthService auth)
    {
        _http = http;
        _endpoints = endpoints;
        _auth = auth;
    }

    public bool IsConfigured => _endpoints.IsStorageConfigured;

    public Task<UploadResult> UploadClassMaterialAsync(
        string classId, string fileName, Stream content, string contentType,
        CancellationToken cancellationToken = default)
        // Path must match storage.rules exactly.
        => UploadToPathAsync(
            $"classes/{classId}/materials/{SafeFileName(fileName)}",
            content, contentType, cancellationToken);

    /// <summary>
    /// The actual upload. Both callers differ only in the object path, and that
    /// path is the thing storage.rules matches on — so it's the caller's to
    /// decide and this method's job to get right.
    /// </summary>
    private async Task<UploadResult> UploadToPathAsync(
        string objectPath, Stream content, string contentType,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            throw new InvalidOperationException(
                "File uploads aren't set up for this school. Set Firebase:StorageBucket in "
                + "Configuration/appsettings.json.");

        // Upload POSTs to the OBJECT COLLECTION with ?name=, not to the object's
        // own URL. /o/{path} is the metadata endpoint — POSTing there is what
        // made every upload fail silently.
        //
        // The name is still escaped: the whole path travels as one query value,
        // and an unescaped slash would be read as a path separator.
        var url = _endpoints.StorageUploadUrl + "?name=" + Uri.EscapeDataString(objectPath);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        await AuthoriseAsync(request, cancellationToken);

        var streamContent = new StreamContent(content);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        request.Content = streamContent;

        using var response = await _http.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            // Show what Storage actually said. Replacing it with a guess is what
            // made the last round of this undiagnosable — the server explains
            // rule denials, and swallowing that leaves nothing to act on.
            var refusal = await response.Content.ReadAsStringAsync(cancellationToken);
            CrashLogger.Log("Storage.Upload.Denied",
                new InvalidOperationException($"{(int)response.StatusCode} on {objectPath}: {refusal}"));

            throw new InvalidOperationException(
                $"Storage refused the upload: {Summarise(refusal)}"
                + "\n\nMost likely: you don't have permission for this path, the Storage "
                + "rules haven't been deployed, or the rules exceeded the two-Firestore-read "
                + "limit that cross-service rules allow.");
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException(
                $"The storage bucket \"{_endpoints.StorageBucket}\" doesn't exist. "
                + "Check Firebase console > Storage for the real bucket name and set it as "
                + "Firebase:StorageBucket in Configuration/appsettings.json. Projects created "
                + "before late 2024 usually end in .appspot.com rather than .firebasestorage.app.");

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            CrashLogger.Log("Storage.Upload",
                new InvalidOperationException($"{(int)response.StatusCode}: {error}"));

            throw new InvalidOperationException(
                $"The upload failed ({(int)response.StatusCode}). {Summarise(error)}");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        // Storage returns a downloadTokens field; the public URL needs it
        // appended as ?alt=media&token=... or the request 403s.
        var token = root.TryGetProperty("downloadTokens", out var t) ? t.GetString() : null;

        var size = root.TryGetProperty("size", out var s) && long.TryParse(s.GetString(), out var parsed)
            ? parsed
            : 0L;

        var downloadUrl = _endpoints.StorageObjectBaseUrl
                        + Uri.EscapeDataString(objectPath)
                        + "?alt=media"
                        + (string.IsNullOrEmpty(token) ? string.Empty : $"&token={token}");

        return new UploadResult(downloadUrl, objectPath, size);
    }

    public Task<UploadResult> UploadSubmissionAsync(
        string classId, string assignmentId, string studentId,
        string fileName, Stream content, string contentType,
        CancellationToken cancellationToken = default)
        // Same upload mechanics, different path. Kept as a separate method so
        // the path shape lives next to the rule it has to match.
        => UploadToPathAsync(
            $"classes/{classId}/submissions/{assignmentId}/{studentId}/{SafeFileName(fileName)}",
            content, contentType, cancellationToken);

    public async Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(storagePath)) return;

        var url = _endpoints.StorageObjectBaseUrl + Uri.EscapeDataString(storagePath);

        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        await AuthoriseAsync(request, cancellationToken);

        using var response = await _http.SendAsync(request, cancellationToken);

        // Already gone is success as far as the caller is concerned. Throwing
        // here would stop the material document being deleted and leave a row
        // pointing at nothing.
        if (response.StatusCode == HttpStatusCode.NotFound) return;

        response.EnsureSuccessStatusCode();
    }

    private async Task AuthoriseAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var idToken = await _auth.GetIdTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(idToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
    }

    /// <summary>
    /// Pulls the human-readable part out of a Storage error body, so the
    /// teacher sees "Permission denied" rather than a page of JSON. Falls back
    /// to a generic line when the shape isn't what we expect — the full body is
    /// already in the crash log either way.
    /// </summary>
    private static string Summarise(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "Please try again.";

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                    return error.GetString() ?? "Please try again.";

                if (error.TryGetProperty("message", out var message))
                    return message.GetString() ?? "Please try again.";
            }
        }
        catch (JsonException)
        {
            // Storage sometimes returns plain text or HTML for infrastructure
            // errors. Truncate rather than dumping a page into a label.
        }

        return body.Length > 160 ? body[..160] + "…" : body;
    }

    /// <summary>
    /// Strips path separators and anything else that could escape the intended
    /// prefix. A filename of "../../users/admin" would otherwise write outside
    /// the class folder that storage.rules protects.
    /// </summary>
    private static string SafeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return $"file-{Guid.NewGuid():n}";

        var cleaned = new string(fileName
            .Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' or ' ')
            .ToArray())
            .Trim();

        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "file";

        // Prefix with a short unique stamp so re-uploading a file with the same
        // name doesn't silently overwrite last term's copy.
        return $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{cleaned}";
    }
}

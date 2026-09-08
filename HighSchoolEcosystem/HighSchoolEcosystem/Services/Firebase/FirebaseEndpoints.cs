using Microsoft.Extensions.Configuration;

namespace HighSchoolEcosystem.Services.Firebase;

/// <summary>
/// Resolves the Firebase REST base URLs for this build, honouring the
/// emulator settings in Configuration/appsettings.json.
///
/// Everything in this app talks to Firebase over REST (no native Firebase
/// SDKs), so the same URLs work identically on Android, iOS, Windows and
/// MacCatalyst.
///
/// Deliberately does NOT throw on bad configuration: this type is constructed
/// while the first page is being resolved, so throwing here would crash the app
/// at launch with no UI. Instead <see cref="ConfigurationError"/> is recorded
/// and surfaced by <see cref="EnsureConfigured"/> on first actual use, where
/// the view models can catch it and show the message on screen.
/// </summary>
public sealed class FirebaseEndpoints
{
    // The Android emulator reaches the host machine's loopback via 10.0.2.2,
    // every other target can use localhost directly.
#if ANDROID
    private const string DefaultEmulatorHost = "10.0.2.2";
#else
    private const string DefaultEmulatorHost = "localhost";
#endif

    public bool UseEmulator { get; }
    public string ProjectId { get; }
    public string WebApiKey { get; }

    /// <summary>Null when the Firebase configuration is usable; otherwise a user-facing explanation.</summary>
    public string? ConfigurationError { get; }

    /// <summary>Base for Identity Toolkit calls, e.g. {IdentityToolkitBaseUrl}/accounts:signUp</summary>
    public string IdentityToolkitBaseUrl { get; }

    /// <summary>Base for the ID-token refresh endpoint, e.g. {SecureTokenBaseUrl}/token</summary>
    public string SecureTokenBaseUrl { get; }

    /// <summary>Base for Firestore documents, with a trailing slash.</summary>
    public string FirestoreDocumentsBaseUrl { get; }

    /// <summary>
    /// The Cloud Storage bucket, e.g. "my-project.firebasestorage.app". Empty
    /// when unset, which disables file uploads without breaking anything else —
    /// links and chapters still work.
    /// </summary>
    public string StorageBucket { get; }

    /// <summary>
    /// Base for Storage object operations, with a trailing slash. Append a
    /// URL-encoded object path to address a single object — used for download
    /// URLs and deletes.
    /// </summary>
    public string StorageObjectBaseUrl { get; }

    /// <summary>
    /// The object COLLECTION, with no trailing slash. Uploads POST here with
    /// ?name={encodedPath}; posting to an object's own URL hits the metadata
    /// endpoint instead and the upload never happens.
    /// </summary>
    public string StorageUploadUrl => StorageObjectBaseUrl.TrimEnd('/');

    /// <summary>False when no bucket is configured, so the UI can hide upload.</summary>
    public bool IsStorageConfigured => !string.IsNullOrWhiteSpace(StorageBucket);

    public FirebaseEndpoints(IConfiguration configuration)
    {
        UseEmulator = bool.TryParse(configuration["Firebase:UseEmulator"], out var useEmulator) && useEmulator;

        var projectId = configuration["Firebase:ProjectId"];
        var webApiKey = configuration["Firebase:WebApiKey"];

        if (string.IsNullOrWhiteSpace(projectId))
        {
            ConfigurationError =
                "Firebase:ProjectId is not set in Configuration/appsettings.json.";
        }
        else if (string.IsNullOrWhiteSpace(webApiKey) && !UseEmulator)
        {
            // The emulator ignores the key, but real Firebase requires it.
            ConfigurationError =
                "Firebase:WebApiKey is not set in Configuration/appsettings.json. " +
                "Copy it from the Firebase console: Project settings > General > Web API Key.";
        }

        ProjectId = string.IsNullOrWhiteSpace(projectId) ? "unconfigured-project" : projectId;
        WebApiKey = string.IsNullOrWhiteSpace(webApiKey) ? "emulator-ignores-this-key" : webApiKey;

        var host = configuration["Firebase:EmulatorHost"];
        if (string.IsNullOrWhiteSpace(host)) host = DefaultEmulatorHost;

        var authPort = int.TryParse(configuration["Firebase:AuthEmulatorPort"], out var parsedAuthPort) ? parsedAuthPort : 9099;
        var firestorePort = int.TryParse(configuration["Firebase:FirestoreEmulatorPort"], out var parsedStorePort) ? parsedStorePort : 8081;
        var storagePort = int.TryParse(configuration["Firebase:StorageEmulatorPort"], out var parsedStoragePort) ? parsedStoragePort : 9199;

        // Defaults to the conventional bucket name for the project, so a school
        // that hasn't set it explicitly still gets working uploads.
        var bucket = configuration["Firebase:StorageBucket"];
        StorageBucket = string.IsNullOrWhiteSpace(bucket)
            ? (string.IsNullOrWhiteSpace(projectId) ? string.Empty : $"{projectId}.firebasestorage.app")
            : bucket;

        if (UseEmulator)
        {
            // The emulators mirror the production paths behind a host/port prefix,
            // e.g. http://localhost:9099/identitytoolkit.googleapis.com/v1/...
            IdentityToolkitBaseUrl = $"http://{host}:{authPort}/identitytoolkit.googleapis.com/v1";
            SecureTokenBaseUrl = $"http://{host}:{authPort}/securetoken.googleapis.com/v1";
            FirestoreDocumentsBaseUrl =
                $"http://{host}:{firestorePort}/v1/projects/{ProjectId}/databases/(default)/documents/";
            StorageObjectBaseUrl =
                $"http://{host}:{storagePort}/v0/b/{StorageBucket}/o/";
        }
        else
        {
            IdentityToolkitBaseUrl = "https://identitytoolkit.googleapis.com/v1";
            SecureTokenBaseUrl = "https://securetoken.googleapis.com/v1";
            FirestoreDocumentsBaseUrl =
                $"https://firestore.googleapis.com/v1/projects/{ProjectId}/databases/(default)/documents/";
            StorageObjectBaseUrl =
                $"https://firebasestorage.googleapis.com/v0/b/{StorageBucket}/o/";
        }
    }

    /// <summary>Throws a user-facing InvalidOperationException if Firebase isn't configured.</summary>
    public void EnsureConfigured()
    {
        if (ConfigurationError is not null)
            throw new InvalidOperationException(ConfigurationError);
    }

    public string WithKey(string url) => $"{url}{(url.Contains('?') ? "&" : "?")}key={WebApiKey}";
}

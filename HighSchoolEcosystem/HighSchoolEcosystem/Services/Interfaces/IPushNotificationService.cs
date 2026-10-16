namespace HighSchoolEcosystem.Services.Interfaces;

/// <summary>
/// Push notifications through Firebase Cloud Messaging, on Android and iOS.
/// The notifications themselves are SENT by Cloud Functions
/// (functions/notifications.js); the app's only jobs are to tell the server
/// where to deliver (a token per device, under /users/{uid}/devices) and to
/// open the right screen when one is tapped.
///
/// On Windows and Mac, and on a phone build without its Firebase config file,
/// this is a no-op: <see cref="IsAvailable"/> is false and every call returns.
/// </summary>
public interface IPushNotificationService
{
    bool IsAvailable { get; }

    /// <summary>
    /// Asks permission if needed and records this device for the signed-in
    /// user. Call after sign-in; safe to call again (it just refreshes).
    /// Never throws — push failing must not stop anyone using the app.
    /// </summary>
    Task RegisterAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Forgets this device for the signed-in user. Call BEFORE signing out:
    /// the delete needs their credentials, and without it the next person to
    /// sign in on a shared tablet would get the previous user's notifications.
    /// </summary>
    Task UnregisterAsync(CancellationToken cancellationToken = default);
}

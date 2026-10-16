using HighSchoolEcosystem.Services.Interfaces;
using HighSchoolEcosystem.Views;
using Microsoft.Extensions.DependencyInjection;
#if (ANDROID || IOS) && PUSH_CONFIGURED
using Plugin.Firebase.CloudMessaging;
using Plugin.Firebase.CloudMessaging.EventArgs;
#endif

namespace HighSchoolEcosystem.Services;

/// <summary>
/// See <see cref="IPushNotificationService"/>. A singleton, because it owns
/// the plugin's event subscriptions. Firestore is resolved per call rather than
/// injected: IFirestoreClient is transient (it carries an HttpClient), and a
/// singleton holding one would be a captive dependency.
/// </summary>
public sealed class PushNotificationService : IPushNotificationService
{
    private const string DeviceIdKey = "push-device-id";

    private readonly IServiceProvider _services;
    private readonly IFirebaseAuthService _auth;

    /// <summary>A tap that arrived before the home page was ready (cold start).</summary>
    private IDictionary<string, string>? _pendingTap;

    public PushNotificationService(IServiceProvider services, IFirebaseAuthService auth)
    {
        _services = services;
        _auth = auth;

#if (ANDROID || IOS) && PUSH_CONFIGURED
        try
        {
            if (CrossFirebaseCloudMessaging.IsSupported)
            {
                CrossFirebaseCloudMessaging.Current.TokenChanged += OnTokenChanged;
                CrossFirebaseCloudMessaging.Current.NotificationTapped += OnNotificationTapped;
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Push.Subscribe", ex);
        }
#endif
    }

#if (ANDROID || IOS) && PUSH_CONFIGURED
    public bool IsAvailable => CrossFirebaseCloudMessaging.IsSupported;
#else
    public bool IsAvailable => false;
#endif

    /// <summary>
    /// One ID per install, so a person with a phone and a tablet gets both, and
    /// a token refresh overwrites this device's entry instead of piling up.
    /// </summary>
    private static string DeviceId
    {
        get
        {
            var id = Preferences.Default.Get(DeviceIdKey, string.Empty);
            if (string.IsNullOrEmpty(id))
            {
                id = Guid.NewGuid().ToString("n");
                Preferences.Default.Set(DeviceIdKey, id);
            }
            return id;
        }
    }

    private static string DevicePath(string uid) => $"users/{uid}/devices/{DeviceId}";

    public async Task RegisterAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || !_auth.IsSignedIn) return;

#if (ANDROID || IOS) && PUSH_CONFIGURED
        try
        {
#if ANDROID
            // Android 13+ needs the user's say-so. iOS asks inside CheckIfValidAsync.
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
                if (status != PermissionStatus.Granted)
                    status = await Permissions.RequestAsync<Permissions.PostNotifications>();
                if (status != PermissionStatus.Granted) return;
            }
#endif
            await CrossFirebaseCloudMessaging.Current.CheckIfValidAsync();
            var token = await CrossFirebaseCloudMessaging.Current.GetTokenAsync();
            await SaveTokenAsync(token, cancellationToken);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Push.Register", ex);
        }

        // A notification tapped to launch the app is handled once there's a
        // home page to navigate from — which is what calling this signals.
        if (_pendingTap is { } pending)
        {
            _pendingTap = null;
            Open(pending);
        }
#else
        await Task.CompletedTask;
#endif
    }

    public async Task UnregisterAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || _auth.CurrentSession?.UserId is not { Length: > 0 } uid) return;
        try
        {
            var firestore = _services.GetRequiredService<IFirestoreClient>();
            await firestore.DeleteDocumentAsync(DevicePath(uid), cancellationToken);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Push.Unregister", ex);
        }
    }

    private async Task SaveTokenAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        if (_auth.CurrentSession?.UserId is not { Length: > 0 } uid) return;

        var firestore = _services.GetRequiredService<IFirestoreClient>();
        await firestore.PatchFieldsAsync(DevicePath(uid), new Dictionary<string, object?>
        {
            ["Token"] = token,
            ["Platform"] = DeviceInfo.Current.Platform.ToString(),
            ["UpdatedAt"] = DateTimeOffset.UtcNow
        }, cancellationToken);
    }

#if (ANDROID || IOS) && PUSH_CONFIGURED
    private async void OnTokenChanged(object? sender, FCMTokenChangedEventArgs e)
    {
        try { await SaveTokenAsync(e.Token, CancellationToken.None); }
        catch (Exception ex) { CrashLogger.Log("Push.TokenChanged", ex); }
    }

    private void OnNotificationTapped(object? sender, FCMNotificationTappedEventArgs e)
    {
        var data = e.Notification?.Data;
        if (data is null || data.Count == 0) return;

        // Cold start: the app is still on the startup or sign-in screen. Hold
        // it until RegisterAsync runs, which happens once home is showing.
        if (!_auth.IsSignedIn || CurrentNavigation() is null) { _pendingTap = data; return; }
        Open(data);
    }
#endif

    /// <summary>
    /// Opens the screen a notification is about. Class chat, teacher-parent
    /// messages and the Newspaper get their own screen; everything else (notices, homework, marks, sport)
    /// lives on the dashboard, which is where the app opens anyway and which
    /// reloads on appearing.
    /// </summary>
    private void Open(IDictionary<string, string> data)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                // Straight after sign-in the home page may still be being set
                // as the root; give it a moment rather than dropping the tap.
                var navigation = CurrentNavigation();
                for (var i = 0; navigation is null && i < 10; i++)
                {
                    await Task.Delay(300);
                    navigation = CurrentNavigation();
                }
                if (navigation is null) return;

                data.TryGetValue("type", out var type);
                switch (type)
                {
                    case "chat" when data.TryGetValue("classId", out var classId) && !string.IsNullOrEmpty(classId):
                        var chat = _services.GetRequiredService<ClassChatPage>();
                        chat.Load(classId, data.TryGetValue("className", out var name) ? name : "Class chat");
                        await navigation.PushAsync(chat);
                        break;

                    case "dm" when data.TryGetValue("conversationId", out var conversationId) && !string.IsNullOrEmpty(conversationId):
                        var thread = _services.GetRequiredService<ConversationPage>();
                        thread.Load(conversationId);
                        await navigation.PushAsync(thread);
                        break;

                    case "newspaper":
                        await navigation.PushAsync(_services.GetRequiredService<NewspaperPage>());
                        break;
                }
            }
            catch (Exception ex)
            {
                CrashLogger.Log("Push.Open", ex);
            }
        });
    }

    /// <summary>The signed-in app's navigation stack, or null before home is shown.</summary>
    private static INavigation? CurrentNavigation()
    {
        var root = Application.Current?.Windows.FirstOrDefault()?.Page as NavigationPage;
        if (root?.RootPage is null or StartupPage or LoginPage or RegisterPage) return null;
        return root.Navigation;
    }
}

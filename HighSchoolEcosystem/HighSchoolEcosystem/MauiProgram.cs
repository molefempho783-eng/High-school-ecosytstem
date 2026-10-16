using System.Reflection;
using CommunityToolkit.Maui;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Firebase;
using HighSchoolEcosystem.Services.Interfaces;
using HighSchoolEcosystem.ViewModels;
using HighSchoolEcosystem.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
#if (ANDROID || IOS) && PUSH_CONFIGURED
using Plugin.Firebase.CloudMessaging;
#endif

namespace HighSchoolEcosystem;

public static class MauiProgram
{
    private const string AppSettingsResourceName = "HighSchoolEcosystem.Configuration.appsettings.json";

    public static MauiApp CreateMauiApp()
    {
        // First thing, so faults during startup are recorded too.
        CrashLogger.Register();

        var builder = MauiApp.CreateBuilder();

        // appsettings.json ships as an embedded resource (see the csproj) because
        // a plain copy-to-output file isn't packaged into an APK or app bundle.
        using var settingsStream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(AppSettingsResourceName);
        if (settingsStream is not null)
        {
            builder.Configuration.AddJsonStream(settingsStream);
        }

        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                // Icons. A bundled subset of Material Symbols, so the rail and row
                // icons look the same on every platform; see Icons.cs and
                // tools/fonts/build-icon-font.py.
                fonts.AddFont("MaterialSymbolsRounded.ttf", "MaterialSymbols");
            })
            .ConfigureLifecycleEvents(RegisterPushStartup);

        // Registered explicitly (rather than relying on MauiAppBuilder to have
        // already added it) so FirebaseEndpoints/SchoolInfoProvider can inject it.
        builder.Services.AddSingleton<IConfiguration>(builder.Configuration);

        // All Firebase access is over REST, so there's no native SDK to
        // initialise and the same code path serves every target platform.
        builder.Services.AddSingleton<FirebaseEndpoints>();

        // HttpClient's default timeout is 100 seconds, which on a flaky mobile
        // connection means a spinner that looks frozen. Fail fast instead so the
        // view models can show a "couldn't reach the server" message.
        // string.Empty is the default client name — i.e. what FirebaseAuthService's
        // parameterless IHttpClientFactory.CreateClient() resolves to. Spelled
        // literally rather than via Options.DefaultName because CommunityToolkit.Maui
        // also exports a type called `Options`, which makes the name ambiguous here.
        builder.Services.AddHttpClient(string.Empty,
            client => client.Timeout = TimeSpan.FromSeconds(30));
        builder.Services.AddSingleton<IAuthTokenStore, SecureAuthTokenStore>();

        // Singleton: owns the signed-in session for the lifetime of the app.
        builder.Services.AddSingleton<IFirebaseAuthService, FirebaseAuthService>();
        // Singleton: it owns the push plugin's event subscriptions.
        builder.Services.AddSingleton<IPushNotificationService, PushNotificationService>();
        builder.Services.AddSingleton<ISchoolInfoProvider, SchoolInfoProvider>();

        // Typed HttpClient registrations are transient, so anything depending on
        // IFirestoreClient must be transient too (avoids a captive dependency).
        builder.Services.AddHttpClient<IFirestoreClient, FirestoreClient>(
            client => client.Timeout = TimeSpan.FromSeconds(30));
        builder.Services.AddTransient<IUserProfileService, UserProfileService>();
        builder.Services.AddTransient<IAdminService, AdminService>();
        builder.Services.AddTransient<IClassService, ClassService>();
        builder.Services.AddTransient<IContentService, ContentService>();
        builder.Services.AddTransient<ITextbookService, TextbookService>();
        builder.Services.AddTransient<IClassChatService, ClassChatService>();
        builder.Services.AddTransient<IConversationService, ConversationService>();
        builder.Services.AddTransient<ITeamService, TeamService>();
        // Longer than the 30 s used elsewhere: a whole textbook can be 50 MB, and
        // on a school connection that takes minutes. Uploads show a spinner and
        // are always something the teacher just started, so a long wait is
        // expected rather than looking frozen.
        builder.Services.AddHttpClient<IStorageService, FirebaseStorageService>(
            client => client.Timeout = TimeSpan.FromMinutes(10));
        builder.Services.AddTransient<IHomePageResolver, HomePageResolver>();

        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<RegisterViewModel>();
        builder.Services.AddTransient<ForgotPasswordViewModel>();
        builder.Services.AddTransient<AccountViewModel>();
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<AdminDashboardViewModel>();
        builder.Services.AddTransient<AdminUsersViewModel>();
        builder.Services.AddTransient<AdminUserDetailViewModel>();
        builder.Services.AddTransient<AdminInvitesViewModel>();
        builder.Services.AddTransient<AdminSettingsViewModel>();
        builder.Services.AddTransient<TeacherDashboardViewModel>();
        builder.Services.AddTransient<TeacherClassesViewModel>();
        builder.Services.AddTransient<TeacherClassDetailViewModel>();
        builder.Services.AddTransient<TeacherTimetableViewModel>();
        builder.Services.AddTransient<TeacherQuestionsViewModel>();
        builder.Services.AddTransient<TeacherStudentMarksViewModel>();
        builder.Services.AddTransient<TeamDetailViewModel>();
        builder.Services.AddTransient<StudentAssignmentViewModel>();
        builder.Services.AddTransient<SportsViewModel>();
        builder.Services.AddTransient<NewspaperViewModel>();
        builder.Services.AddTransient<ClassChatViewModel>();
        builder.Services.AddTransient<MessagesViewModel>();
        builder.Services.AddTransient<ConversationViewModel>();
        builder.Services.AddTransient<TeacherTextbooksViewModel>();
        builder.Services.AddTransient<TeacherTextbookReviewViewModel>();
        builder.Services.AddTransient<StudentClassViewModel>();
        builder.Services.AddTransient<StudentUnitViewModel>();
        builder.Services.AddTransient<ParentDashboardViewModel>();
        builder.Services.AddTransient<CoachDashboardViewModel>();
        builder.Services.AddTransient<TeacherMaterialsViewModel>();
        builder.Services.AddTransient<TeacherMarkingViewModel>();

        builder.Services.AddTransient<StartupPage>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<RegisterPage>();
        builder.Services.AddTransient<ForgotPasswordPage>();
        builder.Services.AddTransient<AccountPage>();
        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<AdminDashboardPage>();
        builder.Services.AddTransient<AdminUsersPage>();
        builder.Services.AddTransient<AdminUserDetailPage>();
        builder.Services.AddTransient<AdminInvitesPage>();
        builder.Services.AddTransient<AdminSettingsPage>();
        builder.Services.AddTransient<TeacherDashboardPage>();
        builder.Services.AddTransient<TeacherClassesPage>();
        builder.Services.AddTransient<TeacherClassDetailPage>();
        builder.Services.AddTransient<TeacherTimetablePage>();
        builder.Services.AddTransient<TeacherQuestionsPage>();
        builder.Services.AddTransient<StudentAssignmentPage>();
        builder.Services.AddTransient<SportsPage>();
        builder.Services.AddTransient<NewspaperPage>();
        builder.Services.AddTransient<ClassChatPage>();
        builder.Services.AddTransient<MessagesPage>();
        builder.Services.AddTransient<ConversationPage>();
        builder.Services.AddTransient<TeacherTextbooksPage>();
        builder.Services.AddTransient<TeacherTextbookReviewPage>();
        builder.Services.AddTransient<StudentClassPage>();
        builder.Services.AddTransient<StudentUnitPage>();
        builder.Services.AddTransient<ParentDashboardPage>();
        builder.Services.AddTransient<CoachDashboardPage>();
        builder.Services.AddTransient<TeacherMaterialsPage>();
        builder.Services.AddTransient<TeacherMarkingPage>();
        builder.Services.AddTransient<TeacherStudentMarksPage>();
        builder.Services.AddTransient<TeamDetailPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }

    /// <summary>
    /// Starts native Firebase for push notifications — Android and iOS only,
    /// and only when that platform's config file was in the build
    /// (PUSH_CONFIGURED, set in the csproj). Starting it without the file would
    /// throw at launch. A failure here is logged and push stays off; the rest of
    /// the app never depended on it.
    /// </summary>
    private static void RegisterPushStartup(ILifecycleBuilder events)
    {
#if IOS && PUSH_CONFIGURED
        events.AddiOS(ios => ios.WillFinishLaunching((_, _) =>
        {
            try
            {
                Plugin.Firebase.Core.Platforms.iOS.CrossFirebase.Initialize();
                FirebaseCloudMessagingImplementation.Initialize();
            }
            catch (Exception ex) { CrashLogger.Log("Push.Init.iOS", ex); }
            return false;
        }));
#elif ANDROID && PUSH_CONFIGURED
        events.AddAndroid(android => android.OnCreate((activity, _) =>
        {
            try
            {
                Plugin.Firebase.Core.Platforms.Android.CrossFirebase.Initialize(
                    activity, () => Microsoft.Maui.ApplicationModel.Platform.CurrentActivity!);
            }
            catch (Exception ex) { CrashLogger.Log("Push.Init.Android", ex); }
        }));
#endif
    }
}

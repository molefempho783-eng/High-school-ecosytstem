using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
#if PUSH_CONFIGURED
using Plugin.Firebase.CloudMessaging;
#endif

namespace HighSchoolEcosystem
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        /// <summary>
        /// The one channel every school notification uses. Must match the
        /// manifest's default_notification_channel_id and the channelId sent by
        /// functions/notifications.js.
        /// </summary>
        public const string NotificationChannelId = "school_updates";

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            CreateNotificationChannel();

#if PUSH_CONFIGURED
            FirebaseCloudMessagingImplementation.ChannelId = NotificationChannelId;
            // A notification tapped while the app was closed arrives here.
            if (Intent is not null) FirebaseCloudMessagingImplementation.OnNewIntent(Intent);
#endif
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
#if PUSH_CONFIGURED
            // ...and one tapped while it was already running arrives here
            // (SingleTop reuses this activity rather than starting another).
            if (intent is not null) FirebaseCloudMessagingImplementation.OnNewIntent(intent);
#endif
        }

        private static void CreateNotificationChannel()
        {
            // Channels exist from Android 8; before that there's nothing to create.
            if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;

            if (Android.App.Application.Context.GetSystemService(NotificationService) is not NotificationManager manager) return;

            var channel = new NotificationChannel(
                NotificationChannelId, "School updates", NotificationImportance.Default)
            {
                Description = "Announcements, homework, marks, the Newspaper and class chat."
            };
            manager.CreateNotificationChannel(channel);
        }
    }
}

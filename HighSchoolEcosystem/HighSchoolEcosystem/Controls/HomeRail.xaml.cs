using HighSchoolEcosystem.Services;

namespace HighSchoolEcosystem.Controls;

public partial class HomeRail : ContentView
{
    public HomeRail() => InitializeComponent();

    /// <summary>
    /// Goes home by popping the navigation stack to its root.
    ///
    /// PopToRoot rather than pushing a dashboard: the root IS the user's home
    /// page — HomePageResolver put it there at sign-in, for whichever role they
    /// hold. Pushing a new one would stack a second copy on top of the first
    /// and leave the back button walking through history the user thought they
    /// had left.
    /// </summary>
    private async void OnHomeTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            var navigation = Navigation;
            if (navigation is null) return;

            // Already home. Popping an empty stack throws on some platforms.
            if (navigation.NavigationStack.Count <= 1) return;

            await navigation.PopToRootAsync();
        }
        catch (Exception ex)
        {
            CrashLogger.Log("HomeRail.PopToRoot", ex);
        }
    }
}

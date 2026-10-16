using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace HighSchoolEcosystem.Views;

public partial class CoachDashboardPage : ContentPage
{
    private readonly CoachDashboardViewModel _viewModel;
    private readonly IServiceProvider _services;

    public CoachDashboardPage(CoachDashboardViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _services = services;

        // The view model owns WHICH rail items exist; this owns what they do.
        _viewModel.OnRailAction = async action => await HandleRailAsync(action);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async Task HandleRailAsync(string action)
    {
        switch (action)
        {
            // Scroll only. Account is the single exception.
            case "needs-result": await ScrollToAsync(ResultsNeededSection); break;
            case "teams": await ScrollToAsync(TeamsSection); break;
            case "sport": await ScrollToAsync(SportsSection); break;
            case "announcements": await ScrollToAsync(AnnouncementsSection); break;
            case "newspaper": await ScrollToAsync(NewspaperSection); break;
            case "roles": await ScrollToAsync(RolesSection); break;
            case "account": await PushAsync<AccountPage>(); break;
        }
    }

    private async Task ScrollToAsync(VisualElement? target)
    {
        if (target is null || !target.IsVisible) return;

        try
        {
            await ContentScroll.ScrollToAsync(target, ScrollToPosition.Start, animated: true);
        }
        catch (Exception ex)
        {
            // Scrolling to an element that hasn't been laid out yet throws.
            CrashLogger.Log("CoachDashboard.ScrollTo", ex);
        }
    }

    private async Task PushAsync<TPage>() where TPage : Page
    {
        try
        {
            await Navigation.PushAsync(_services.GetRequiredService<TPage>());
        }
        catch (Exception ex)
        {
            CrashLogger.Log($"Navigate:{typeof(TPage).Name}", ex);
        }
    }

    private async void OnSportsTapped(object? sender, EventArgs e)
        => await PushAsync<SportsPage>();

    /// <summary>
    /// Full details for one match or notice, with Edit and Delete for whoever
    /// can manage it.
    ///
    /// Edit pushes the sports page rather than editing here: changing a fixture
    /// needs sport, team, opponent, venue, date and time, which is a form, not
    /// a popup. Delete happens in place, because it needs no fields.
    /// </summary>
    private async void OnSportsItemTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not SportsRow row) return;

        try
        {
            var action = await DetailPopup.ShowAsync(
                this, row.Title, row.Source.DetailText,
                row.Source.SportAndTeam, Color.FromArgb("#8CC63E"), row.CanManage);

            switch (action)
            {
                case DetailAction.Edit:
                    await PushAsync<SportsPage>();
                    break;

                case DetailAction.Delete:
                    var sure = await DisplayAlertAsync(
                        "Delete this post?",
                        $"\"{row.Title}\" will be removed for everyone. This can't be undone.",
                        "Delete", "Keep it");

                    if (sure) await _viewModel.DeleteSportsAsync(row);
                    break;
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Log("CoachDashboard.SportsDetail", ex);
        }
    }

    /// <summary>
    /// Asks for the score and records it, turning the fixture into a result.
    ///
    /// A prompt rather than a field on the tile: the tile is 132 wide, and an
    /// entry box in there would be unusable. The prompt is also the only place
    /// to explain the format, which matters when "24 - 17", "2 - 1 (aet)" and
    /// "3rd of 12" are all valid answers.
    /// </summary>
    private async void OnRecordResultClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not SportsRow row) return;

        try
        {
            var score = await DisplayPromptAsync(
                "Record the result",
                $"{row.Title}\n\nEnter the score as you'd want it read.",
                accept: "Save",
                cancel: "Cancel",
                placeholder: "e.g. 24 - 17",
                maxLength: 60);

            // Null means Cancel. An empty string means they pressed Save on an
            // empty box, which the view model turns into a message rather than
            // silently writing a blank score over the fixture.
            if (score is null) return;

            await _viewModel.RecordResultAsync(row, score);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("CoachDashboard.RecordResult", ex);
        }
    }

    private async void OnAccountClicked(object? sender, EventArgs e)
        => await PushAsync<AccountPage>();

    /// <summary>
    /// The whole notice plus who posted it. Read-only here: neither a coach nor
    /// a parent can edit school announcements, and offering Manage would be a
    /// button that always fails.
    /// </summary>
    private async void OnAnnouncementTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not AnnouncementRow row) return;

        try
        {
            await DetailPopup.ShowAsync(
                this, row.Title, row.Source.DetailText,
                row.Source.ScopeLabel, row.AccentColor, canManage: false);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Dashboard.AnnouncementDetail", ex);
        }
    }

    // Cross-role links. These are the only navigation left outside the Account
    // button, and they live in a widget rather than on the rail — see the
    // "Your other roles" card.
    private async void OnTeacherToolsClicked(object? sender, EventArgs e)
        => await PushAsync<TeacherDashboardPage>();

    private async void OnAdminToolsClicked(object? sender, EventArgs e)
        => await PushAsync<AdminDashboardPage>();

    /// <summary>
    /// Opens the sports page to post a fixture, result or article.
    ///
    /// Navigation, not a popup: a post needs sport, team, opponent, venue, date
    /// and time. The button is hidden unless the rules would accept the write,
    /// which is politeness rather than security — firestore.rules is the
    /// boundary either way.
    /// </summary>
    /// <summary>
    /// Opens one team: its squad and its notice board.
    ///
    /// The tile shows what a team IS; everything a coach does with it — picking
    /// the squad, posting a practice time — needs a page.
    /// </summary>
    private async void OnTeamTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not SportsTeam team) return;

        try
        {
            var page = _services.GetRequiredService<TeamDetailPage>();
            page.Load(team.Id, team.Name);
            await Navigation.PushAsync(page);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Navigate:TeamDetail", ex);
        }
    }

    private async void OnPostSportsClicked(object? sender, EventArgs e)
        => await PushAsync<SportsPage>();

    /// <summary>
    /// Opens the school dashboard, where school-wide announcements are written.
    ///
    /// There is deliberately ONE composer rather than one per dashboard: two
    /// would drift, and an announcement form has enough state (pinned, public,
    /// expiry, edit-in-place) that the drift would be silent.
    /// </summary>
    private async void OnPostAnnouncementClicked(object? sender, EventArgs e)
        => await PushAsync<DashboardPage>();
}

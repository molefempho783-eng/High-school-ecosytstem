using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.ViewModels;

namespace HighSchoolEcosystem.Views;

public partial class TeamDetailPage : ContentPage
{
    private readonly TeamDetailViewModel _viewModel;

    public TeamDetailPage(TeamDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        // The view model owns WHICH rail items exist; this owns what they do.
        _viewModel.OnRailAction = async action => await HandleRailAsync(action);
    }

    /// <summary>Called before pushing, to say which team is being opened.</summary>
    public void Load(string teamId, string teamName) => _viewModel.Load(teamId, teamName);

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async Task HandleRailAsync(string action)
    {
        switch (action)
        {
            // The only rail item that isn't a scroll. Pops to the root rather
            // than pushing a dashboard: the root already IS this coach's home.
            case "home": await GoHomeAsync(); break;

            case "notices": await ScrollToAsync(NoticesSection); break;
            case "squad": await ScrollToAsync(SquadSection); break;
            case "details": await ScrollToAsync(DetailsSection); break;
        }
    }

    private async Task GoHomeAsync()
    {
        try
        {
            if (Navigation.NavigationStack.Count > 1)
                await Navigation.PopToRootAsync();
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeamDetail.GoHome", ex);
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
            CrashLogger.Log("TeamDetail.ScrollTo", ex);
        }
    }

    /// <summary>
    /// Confirms before removing a notice.
    ///
    /// In the page rather than the view model because nothing in this app has
    /// undo, and a notice is gone for the whole squad the moment it goes.
    /// </summary>
    private async void OnDeleteNoticeClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not AnnouncementRow row) return;

        try
        {
            var sure = await DisplayAlertAsync(
                "Delete this notice?",
                $"\"{row.Title}\" will be removed for the whole squad. This can't be undone.",
                "Delete", "Keep it");

            if (sure) await _viewModel.DeleteNoticeCommand.ExecuteAsync(row);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeamDetail.DeleteNotice", ex);
        }
    }
}

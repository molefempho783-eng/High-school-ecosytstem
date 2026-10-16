using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace HighSchoolEcosystem.Views;

public partial class TeacherDashboardPage : ContentPage
{
    private readonly TeacherDashboardViewModel _viewModel;
    private readonly IServiceProvider _services;

    public TeacherDashboardPage(TeacherDashboardViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        // The view model owns WHICH rail items exist; this owns what they do.
        _viewModel.OnRailAction = async action => await HandleRailAsync(action);
        _services = services;
    }

    // Reloads on every appearance so class counts stay current after edits.
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async void OnClassesClicked(object? sender, EventArgs e) => await PushAsync<TeacherClassesPage>();
    private async void OnTimetableClicked(object? sender, EventArgs e) => await PushAsync<TeacherTimetablePage>();
    private async void OnAccountClicked(object? sender, EventArgs e) => await PushAsync<AccountPage>();

    // The same person may also be a student, parent or coach; that content
    // lives on the shared dashboard.
    private async void OnPersonalViewClicked(object? sender, EventArgs e) => await PushAsync<DashboardPage>();

    // Pushed rather than swapping the root, so the back button returns here.
    private async void OnAdminToolsClicked(object? sender, EventArgs e) => await PushAsync<AdminDashboardPage>();

    // The class list moved from CollectionView to BindableLayout when the page
    // was restyled — a CollectionView nested in a ScrollView collapses on
    // mobile. BindableLayout has no selection model, so the row carries a tap
    // recogniser and the item comes off the tapped element's BindingContext.
    private async void OnClassRowTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not TeacherClassRow row) return;
        await OpenClassAsync(row);
    }

    /// <summary>
    /// Straight into the marking screen for that assignment. The badge is only
    /// worth putting on the dashboard if tapping it lands on the work itself
    /// rather than on the class it belongs to.
    /// </summary>
    private async void OnMarkingRowTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not MarkingQueueItem item) return;

        try
        {
            var page = _services.GetRequiredService<TeacherMarkingPage>();
            page.Load(item.ClassId, item.AssignmentId, item.AssignmentTitle, item.ClassName);
            await Navigation.PushAsync(page);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Navigate:MarkingFromDashboard", ex);
        }
    }

    private async Task OpenClassAsync(TeacherClassRow row)
    {
        try
        {
            var page = _services.GetRequiredService<TeacherClassDetailPage>();
            page.Load(row.Id);
            await Navigation.PushAsync(page);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Navigate:TeacherClassDetail", ex);
        }
    }

    // `async void` handlers — an escaping exception has nowhere to propagate.
    private async Task HandleRailAsync(string action)
    {
        switch (action)
        {
            // Scroll only. Account is the single exception.
            case "tools": await ScrollToAsync(ToolsSection); break;
            case "roles": await ScrollToAsync(RolesSection); break;
            case "timetable": await ScrollToAsync(TimetableSection); break;
            case "marking": await ScrollToAsync(MarkingSection); break;
            case "classes": await ScrollToAsync(ClassesSection); break;
            case "messages": await ScrollToAsync(MessagesSection); break;
            case "announcements": await ScrollToAsync(AnnouncementsSection); break;
            case "sport": await ScrollToAsync(SportsSection); break;
            case "newspaper": await ScrollToAsync(NewspaperSection); break;
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
            CrashLogger.Log("TeacherDashboard.ScrollTo", ex);
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

    /// <summary>
    /// The whole notice plus who posted it. Read-only on this dashboard —
    /// school-wide notices are composed with the admin tools.
    /// </summary>
    private async void OnAnnouncementTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not AnnouncementRow row) return;

        try
        {
            // A teacher's own class notice opens the class it belongs to, where
            // it can be edited beside the rest of that class's settings. Reading
            // a notice is rarely the end of it — noticing a typo is.
            //
            // School-wide notices stay a popup: a teacher can't edit those, and
            // there is no page of theirs to open.
            if (row.IsClassAnnouncement && !string.IsNullOrWhiteSpace(row.Source.ClassId))
            {
                var page = _services.GetRequiredService<TeacherClassDetailPage>();
                page.Load(row.Source.ClassId);
                await Navigation.PushAsync(page);
                return;
            }

            await DetailPopup.ShowAsync(
                this, row.Title, row.Source.DetailText,
                row.Source.ScopeLabel, row.AccentColor, canManage: false);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Dashboard.AnnouncementDetail", ex);
        }
    }

    /// <summary>Full details for one fixture, result or article.</summary>
    private async void OnSportsItemTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not SportsRow row) return;

        try
        {
            await DetailPopup.ShowAsync(
                this, row.Title, row.Source.DetailText,
                row.Source.SportAndTeam, Color.FromArgb("#8CC63E"), canManage: false);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Dashboard.SportsDetail", ex);
        }
    }

    /// <summary>
    /// Opens the sports page to post a fixture, result or article.
    ///
    /// Navigation, not a popup: a post needs sport, team, opponent, venue, date
    /// and time. The button is hidden unless the rules would accept the write,
    /// which is politeness rather than security — firestore.rules is the
    /// boundary either way.
    /// </summary>
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

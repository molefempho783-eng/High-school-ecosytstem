using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace HighSchoolEcosystem.Views;

public partial class DashboardPage : ContentPage
{
    private readonly DashboardViewModel _viewModel;
    private readonly IServiceProvider _services;

    // Pages are resolved lazily rather than constructor-injected: AccountPage
    // and LoginPage both lead back to DashboardPage, so injecting either would
    // be a dependency cycle (and would build the whole graph up front).
    public DashboardPage(DashboardViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _services = services;

        // `async void` at the boundary: the rail command has nowhere to await.
        _viewModel.OnRailAction = async action => await HandleRailAsync(action);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    // Someone can hold Student and Teacher at once. Pushed rather than
    // swapping the root, so the back button returns to their own view.
    private async void OnTeacherToolsClicked(object? sender, EventArgs e) => await PushAsync<TeacherDashboardPage>();
    private async void OnAdminToolsClicked(object? sender, EventArgs e) => await PushAsync<AdminDashboardPage>();

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

    // BindableLayout has no SelectionChanged, so the row carries a tap
    // recogniser and we read the item off the tapped element's BindingContext.
    private async void OnWorkTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not StudentWorkRow row) return;

        try
        {
            // A test or exam has nothing to hand in, so opening a submission
            // page with no submit box would be a dead end. Show what it is and
            // when it is instead.
            if (row.IsAssessment)
            {
                await DetailPopup.ShowAsync(
                    this, row.Title, row.DetailText,
                    row.ClassName, row.AccentColor, canManage: false);
                return;
            }

            var page = _services.GetRequiredService<StudentAssignmentPage>();
            page.Load(row.ClassId, row.AssignmentId, row.Title, row.ClassName);
            await Navigation.PushAsync(page);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Navigate:StudentAssignment", ex);
        }
    }

    /// <summary>
    /// Tapping a mark opens the class it came from.
    ///
    /// A mark on its own says little; next to the rest of that subject's work
    /// and its upcoming assessments it says a lot. Falls back to doing nothing
    /// rather than guessing when the row has no class on it — grades recorded
    /// before ClassId existed won't have one.
    /// </summary>
    private async void OnGradeTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not GradeRow grade) return;
        if (string.IsNullOrWhiteSpace(grade.ClassId)) return;

        try
        {
            var page = _services.GetRequiredService<StudentClassPage>();
            page.Load(grade.ClassId, grade.ClassName);
            await Navigation.PushAsync(page);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Navigate:StudentClassFromGrade", ex);
        }
    }

    /// <summary>
    /// Opens the class page: materials, the student's grades for that class,
    /// and its upcoming tests and exams.
    /// </summary>
    private async void OnClassCardTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not ClassCardRow card) return;

        try
        {
            var page = _services.GetRequiredService<StudentClassPage>();
            page.Load(card.ClassId, card.Heading);
            await Navigation.PushAsync(page);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Navigate:StudentClass", ex);
        }
    }

    // ---- navigation rail ----

    // The rail is data-driven, so the view model says WHICH items exist and
    // this says what each DOES. Scrolling and navigation are view concerns; the
    // view model shouldn't know about either.
    private async Task HandleRailAsync(string action)
    {
        switch (action)
        {
            // EVERY case scrolls. Account is the only rail item that opens
            // another screen — cross-role links moved into the "Your other
            // roles" widget, which this scrolls to like any other section.
            case "roles": await ScrollToAsync(RolesSection); break;
            case "work": await ScrollToAsync(WorkSection); break;
            case "classes": await ScrollToAsync(ClassesSection); break;
            case "grades": await ScrollToAsync(GradesSection); break;
            case "timetable": await ScrollToAsync(TimetableSection); break;
            case "announcements": await ScrollToAsync(AnnouncementsSection); break;
            case "sport": await ScrollToAsync(SportsSection); break;
            case "newspaper": await ScrollToAsync(NewspaperSection); break;
            case "account": OnAccountClicked(this, EventArgs.Empty); break;
        }
    }

    // ScrollToAsync takes an element and scrolls it to the top of the view.
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
            // Not worth interrupting the user over — they can scroll by hand.
            CrashLogger.Log("Dashboard.ScrollTo", ex);
        }
    }

    // Sport reads inline but posting needs more fields than a widget can hold,
    // so the widget's buttons open the full page. Typed as EventArgs so it
    // serves both a Button's Clicked and a tap gesture.
    private async void OnSportsTapped(object? sender, EventArgs e)
        => await PushAsync<SportsPage>();

    /// <summary>
    /// Full details for one fixture, result or sports notice, in place.
    ///
    /// Read-only here whatever the reader's role: this is the student dashboard,
    /// and editing sport belongs on the sports page next to the fields it needs.
    /// </summary>
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
    /// The whole notice, plus who posted it. An admin gets Edit and Delete
    /// behind "Manage"; everyone else just gets Close.
    /// </summary>
    private async void OnAnnouncementTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not AnnouncementRow row) return;

        try
        {
            var action = await DetailPopup.ShowAsync(
                this, row.Title, row.Source.DetailText,
                row.Source.ScopeLabel, row.AccentColor, row.CanManage);

            switch (action)
            {
                case DetailAction.Edit:
                    _viewModel.StartEditAnnouncementCommand.Execute(row);
                    await ScrollToAsync(AnnouncementsSection);
                    break;

                case DetailAction.Delete:
                    // Confirmed here rather than in the view model: nothing in
                    // this app has undo, and a notice is gone for the whole
                    // school the moment it goes.
                    var sure = await DisplayAlertAsync(
                        "Delete this announcement?",
                        $"\"{row.Title}\" will be removed for everyone. This can't be undone.",
                        "Delete", "Keep it");

                    if (sure) _viewModel.DeleteAnnouncementCommand.Execute(row);
                    break;
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Dashboard.AnnouncementDetail", ex);
        }
    }

    private async void OnAccountClicked(object? sender, EventArgs e)
    {
        if (_viewModel.IsGuest)
        {
            // Guests get the sign-in screen instead of an account page. Replace
            // the root so signing in doesn't leave a stale guest dashboard
            // underneath in the back stack.
            if (Application.Current?.Windows.Count > 0)
            {
                Application.Current.Windows[0].Page =
                    new NavigationPage(_services.GetRequiredService<LoginPage>());
            }
            return;
        }

        // `async void` handler — an escaping exception has nowhere to go.
        try
        {
            await Navigation.PushAsync(_services.GetRequiredService<AccountPage>());
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Navigate:AccountPage", ex);
        }
    }
}

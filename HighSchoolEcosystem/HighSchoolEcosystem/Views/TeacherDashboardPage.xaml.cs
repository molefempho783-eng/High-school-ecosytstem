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
            // Both of these are sections on this page now, so scroll rather
            // than navigating away from what the teacher is looking at.
            case "marking": await ScrollToAsync(MarkingSection); break;
            case "classes": await ScrollToAsync(ClassesSection); break;
            case "timetable": await ScrollToAsync(TimetableSection); break;

            // Editing the timetable needs more room than a widget, so that
            // stays a page.
            case "edit-timetable": await PushAsync<TeacherTimetablePage>(); break;
            case "manage-classes": await PushAsync<TeacherClassesPage>(); break;
            case "announcements": await PushAsync<DashboardPage>(); break;
            case "sport": await PushAsync<SportsPage>(); break;
            case "admin": await PushAsync<AdminDashboardPage>(); break;
            case "personal": await PushAsync<DashboardPage>(); break;
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
}

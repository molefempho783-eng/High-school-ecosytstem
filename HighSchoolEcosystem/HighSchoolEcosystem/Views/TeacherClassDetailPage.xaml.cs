using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace HighSchoolEcosystem.Views;

public partial class TeacherClassDetailPage : ContentPage
{
    private readonly TeacherClassDetailViewModel _viewModel;
    private readonly IServiceProvider _services;
    private string? _classId;

    public TeacherClassDetailPage(TeacherClassDetailViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _services = services;

        // Creating a multiple-choice paper carries straight on to its questions.
        _viewModel.QuestionsRequested += OnQuestionsRequested;

        // The view model owns WHICH rail items exist; this owns what they do.
        // Every one of them scrolls — there is no Account item on a pushed
        // page, so nothing here navigates at all.
        _viewModel.OnRailAction = async action => await HandleRailAsync(action);
    }

    private async Task HandleRailAsync(string action)
    {
        switch (action)
        {
            // The one rail item on this page that isn't a scroll. Pops to the
            // root rather than pushing a dashboard: the root already IS this
            // user's home page, and pushing another would stack a duplicate.
            case "home": await GoHomeAsync(); break;

            case "content": await ScrollToAsync(ContentSection); break;
            case "notices": await ScrollToAsync(NoticesSection); break;
            case "work": await ScrollToAsync(WorkSection); break;
            case "students": await ScrollToAsync(StudentsSection); break;
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
            CrashLogger.Log("TeacherClassDetail.GoHome", ex);
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
            CrashLogger.Log("TeacherClassDetail.ScrollTo", ex);
        }
    }

    /// <summary>
    /// Opens the question editor for an assignment the teacher just created.
    ///
    /// The view model raises this rather than navigating itself; pushing happens
    /// here, where every other navigation on this page already lives.
    /// </summary>
    private async void OnQuestionsRequested(string assignmentId, string title)
    {
        if (_classId is null) return;

        try
        {
            var page = _services.GetRequiredService<TeacherQuestionsPage>();
            page.Load(_classId, assignmentId, title);
            await Navigation.PushAsync(page);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Navigate:NewQuestions", ex);
        }
    }

    /// <summary>Called before pushing, to say which class is being opened.</summary>
    public void Load(string classId) => _classId = classId;

    /// <summary>
    /// Opens one student's full mark history for this class.
    ///
    /// The student comes off the tapped button's own BindingContext — the
    /// enrolment list is a CollectionView used for ticking, so hijacking its
    /// selection for navigation would fight the checkboxes.
    /// </summary>
    private async void OnStudentMarksClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not StudentEnrolment student) return;
        if (_classId is null) return;

        try
        {
            var page = _services.GetRequiredService<TeacherStudentMarksPage>();
            page.Load(_classId, _viewModel.ClassName, student.Uid, student.Name);
            await Navigation.PushAsync(page);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Navigate:TeacherStudentMarks", ex);
        }
    }

    /// <summary>Opens the class's group chat.</summary>
    private async void OnClassChatTapped(object? sender, TappedEventArgs e)
    {
        if (_classId is null) return;

        try
        {
            var page = _services.GetRequiredService<ClassChatPage>();
            page.Load(_classId, _viewModel.EditName);
            await Navigation.PushAsync(page);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Navigate:ClassChat", ex);
        }
    }

    /// <summary>Opens the course material editor for this class.</summary>
    private async void OnMaterialsClicked(object? sender, EventArgs e)
    {
        if (_classId is null) return;

        try
        {
            var page = _services.GetRequiredService<TeacherMaterialsPage>();
            page.Load(_classId, _viewModel.EditName);
            await Navigation.PushAsync(page);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Navigate:TeacherMaterials", ex);
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_classId is not null) await _viewModel.LoadAsync(_classId);
    }

    /// <summary>
    /// Tapping an assignment offers both things a teacher does with one: set
    /// its questions, or mark what came back.
    ///
    /// An action sheet rather than two buttons per row — the list is already
    /// dense, and both actions are equally likely depending on where in the term
    /// they are.
    /// </summary>
    private async void OnAssignmentSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not AssignmentRow row) return;
        if (sender is CollectionView collectionView) collectionView.SelectedItem = null;
        if (_classId is null) return;

        const string mark = "Mark submissions";
        const string questions = "Set questions and answers";

        var choice = await DisplayActionSheetAsync(row.Title, "Cancel", null, mark, questions);
        if (choice is null || choice == "Cancel") return;

        try
        {
            if (choice == mark)
            {
                var marking = _services.GetRequiredService<TeacherMarkingPage>();
                marking.Load(_classId, row.Id, row.Title, _viewModel.ClassName);
                await Navigation.PushAsync(marking);
                return;
            }

            var page = _services.GetRequiredService<TeacherQuestionsPage>();
            page.Load(_classId, row.Id, row.Title);
            await Navigation.PushAsync(page);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Navigate:AssignmentAction", ex);
        }
    }
}

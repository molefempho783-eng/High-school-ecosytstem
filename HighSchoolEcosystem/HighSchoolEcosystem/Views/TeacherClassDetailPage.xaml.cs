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

        var choice = await DisplayActionSheet(row.Title, "Cancel", null, mark, questions);
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

using HighSchoolEcosystem.ViewModels;

namespace HighSchoolEcosystem.Views;

public partial class TeacherQuestionsPage : ContentPage
{
    private readonly TeacherQuestionsViewModel _viewModel;
    private string? _classId;
    private string? _assignmentId;
    private string _title = string.Empty;

    public TeacherQuestionsPage(TeacherQuestionsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    /// <summary>Called before pushing, to say which assignment is being authored.</summary>
    public void Load(string classId, string assignmentId, string assignmentTitle)
    {
        _classId = classId;
        _assignmentId = assignmentId;
        _title = assignmentTitle;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_classId is not null && _assignmentId is not null)
        {
            await _viewModel.LoadAsync(_classId, _assignmentId, _title);
        }
    }

    private void OnQuestionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not QuestionRow row) return;

        // Cleared immediately so the same row can be tapped again after saving.
        if (sender is CollectionView collectionView) collectionView.SelectedItem = null;

        _viewModel.BeginEdit(row);
    }
}

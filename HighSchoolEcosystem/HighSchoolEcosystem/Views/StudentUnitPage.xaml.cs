using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.ViewModels;

namespace HighSchoolEcosystem.Views;

public partial class StudentUnitPage : ContentPage
{
    private readonly StudentUnitViewModel _viewModel;

    public StudentUnitPage(StudentUnitViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    /// <summary>
    /// An empty <paramref name="unitId"/> is legitimate — it opens the loose
    /// "Module information" group, which renders like any other unit.
    /// </summary>
    public void Load(string classId, string className, string unitId)
        => _viewModel.Initialise(classId, className, unitId);

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    /// <summary>
    /// A unit row switches the content pane; an item row opens the material.
    /// Theme rows are headings and do nothing.
    /// </summary>
    private async void OnTocRowTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not TocRow row) return;

        if (row.IsUnit)
        {
            _viewModel.SelectUnitCommand.Execute(row.Id);
            return;
        }

        if (!row.IsItem) return;

        // The tree only carries labels, so find the full material by ID.
        var material = _viewModel.UnitMaterials.FirstOrDefault(m => m.Id == row.Id);
        if (material is null) return;

        await OpenMaterialAsync(material);
    }

    private async void OnMaterialTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not MaterialRow row) return;
        await OpenMaterialAsync(row);
    }

    /// <summary>
    /// A chapter opens in the in-page reader; a link or file opens in the
    /// system browser. Deliberately not a WebView — a Drive or OneDrive link
    /// needs the user's own signed-in browser session to authorise them, which
    /// an embedded view wouldn't have.
    /// </summary>
    private async Task OpenMaterialAsync(MaterialRow row)
    {
        if (row.IsChapter)
        {
            _viewModel.OpenChapterCommand.Execute(row);
            return;
        }

        if (!row.IsOpenable) return;

        try
        {
            await Launcher.Default.OpenAsync(new Uri(row.Url));
        }
        catch (UriFormatException)
        {
            // A teacher can paste anything into the link field, so a malformed
            // URL is a data problem rather than a bug — say so plainly.
            await DisplayAlertAsync("Can't open this",
                "That link doesn't look valid. Ask your teacher to check it.", "OK");
        }
        catch (Exception ex)
        {
            CrashLogger.Log("StudentUnit.OpenMaterial", ex);
            await DisplayAlertAsync("Can't open this",
                "We couldn't open that material. Check your connection and try again.", "OK");
        }
    }
}

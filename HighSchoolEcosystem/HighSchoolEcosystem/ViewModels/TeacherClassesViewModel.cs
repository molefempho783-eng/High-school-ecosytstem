using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

public partial class TeacherClassesViewModel : ObservableObject
{
    private readonly IClassService _classes;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClassCount))]
    private ObservableCollection<TeacherClassRow> classes = [];

    /// <summary>The number on the badge beside "Manage classes".</summary>
    public string ClassCount => Classes.Count.ToString();

    [ObservableProperty] private string newName = string.Empty;
    [ObservableProperty] private string newSubject = string.Empty;
    [ObservableProperty] private string newRoom = string.Empty;

    public TeacherClassesViewModel(IClassService classes) => _classes = classes;

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            await ReloadAsync();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "Teacher access is required to manage classes.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeacherClasses.Load", ex);
            StatusMessage = "We couldn't load your classes. Please try again.";
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Rebuilds the tile grid. One method rather than the same four lines in
    /// both LoadAsync and CreateAsync — they had drifted apart already, and a
    /// newly created class would have come back without its banner colour.
    ///
    /// Colours are assigned by ordering class IDs, the same rule the dashboards
    /// use, so a class is the same colour wherever a teacher sees it.
    /// </summary>
    private async Task ReloadAsync()
    {
        var mine = await _classes.GetMyClassesAsync();
        var palette = DashboardShellViewModel.BannerPalette;

        var colourByClassId = mine
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .Select((c, i) => (c.Id, Colour: palette[i % palette.Count]))
            .ToDictionary(x => x.Id, x => x.Colour, StringComparer.Ordinal);

        Classes = new ObservableCollection<TeacherClassRow>(
            mine.Select(c => new TeacherClassRow(c.Id, c.Name, c.Subject, c.StudentIds.Count)
            {
                BannerColor = colourByClassId.TryGetValue(c.Id, out var col) ? col : palette[0]
            }));
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try { await LoadAsync(); }
        finally { IsRefreshing = false; }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(NewName))
        {
            StatusMessage = "Give the class a name.";
            return;
        }
        if (IsBusy) return;

        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            var name = NewName.Trim();
            await _classes.CreateClassAsync(name, NewSubject, NewRoom);

            NewName = string.Empty;
            NewSubject = string.Empty;
            NewRoom = string.Empty;

            await ReloadAsync();

            StatusMessage = $"Created {name}. Open it to enrol students.";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "You don't have permission to create classes.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeacherClasses.Create", ex);
            StatusMessage = "We couldn't create that class. Please try again.";
        }
        finally { IsBusy = false; }
    }
}

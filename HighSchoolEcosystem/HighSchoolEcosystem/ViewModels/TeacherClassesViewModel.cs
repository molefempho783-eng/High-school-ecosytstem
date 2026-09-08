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
    [ObservableProperty] private ObservableCollection<TeacherClassRow> classes = [];

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
            var mine = await _classes.GetMyClassesAsync();
            Classes = new ObservableCollection<TeacherClassRow>(
                mine.Select(c => new TeacherClassRow(c.Id, c.Name, c.Subject, c.StudentIds.Count)));
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

            var mine = await _classes.GetMyClassesAsync();
            Classes = new ObservableCollection<TeacherClassRow>(
                mine.Select(c => new TeacherClassRow(c.Id, c.Name, c.Subject, c.StudentIds.Count)));

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

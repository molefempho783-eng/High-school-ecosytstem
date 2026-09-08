using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

public sealed record TimetableRow(string Id, string ClassName, DayOfWeek Day, string StartTime, string EndTime, string Room)
{
    public string Title => $"{Day} · {StartTime}–{EndTime}";
    public string Subtitle => string.IsNullOrWhiteSpace(Room) ? ClassName : $"{ClassName} · {Room}";
}

public partial class TeacherTimetableViewModel : ObservableObject
{
    private readonly IClassService _classes;
    private IReadOnlyList<SchoolClass> _myClasses = [];

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private ObservableCollection<TimetableRow> entries = [];

    // New entry form. The picker holds class objects rather than names: two
    // classes can share a name, and matching on the string would silently
    // schedule the wrong one.
    [ObservableProperty] private ObservableCollection<TeacherClassRow> availableClasses = [];
    [ObservableProperty] private TeacherClassRow? selectedClass;
    // Explicit array rather than a collection expression: `new([...])` has to
    // infer a target type for the expression *and* pick a constructor overload,
    // which is fragile. School week only — weekends aren't timetabled.
    [ObservableProperty] private ObservableCollection<DayOfWeek> days = new(new[]
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday
    });
    [ObservableProperty] private DayOfWeek selectedDay = DayOfWeek.Monday;
    [ObservableProperty] private TimeSpan startTime = new(8, 0, 0);
    [ObservableProperty] private TimeSpan endTime = new(9, 0, 0);
    [ObservableProperty] private string period = "1";
    [ObservableProperty] private string room = string.Empty;

    public TeacherTimetableViewModel(IClassService classes) => _classes = classes;

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            _myClasses = await _classes.GetMyClassesAsync();
            AvailableClasses = new ObservableCollection<TeacherClassRow>(
                _myClasses.Select(c => new TeacherClassRow(c.Id, c.Name, c.Subject, c.StudentIds.Count)));
            SelectedClass ??= AvailableClasses.FirstOrDefault();

            var all = await _classes.GetTimetableAsync();
            var mineIds = _myClasses.Select(c => c.Id).ToHashSet();

            // The timetable is school-wide; show only this teacher's periods.
            Entries = new ObservableCollection<TimetableRow>(
                all.Where(e => mineIds.Contains(e.ClassId))
                   .Select(e => new TimetableRow(e.Id, e.ClassName, e.DayOfWeek, e.StartTime, e.EndTime, e.Room)));

            if (_myClasses.Count == 0)
                StatusMessage = "Create a class first — timetable periods attach to a class.";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "Teacher access is required to manage the timetable.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeacherTimetable.Load", ex);
            StatusMessage = "We couldn't load the timetable. Please try again.";
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
    private async Task AddAsync()
    {
        if (IsBusy) return;

        var target = SelectedClass is null
            ? null
            : _myClasses.FirstOrDefault(c => c.Id == SelectedClass.Id);
        if (target is null)
        {
            StatusMessage = "Pick one of your classes first.";
            return;
        }
        if (EndTime <= StartTime)
        {
            StatusMessage = "The end time has to be after the start time.";
            return;
        }
        if (!int.TryParse(Period, out var periodNumber) || periodNumber < 1)
        {
            StatusMessage = "Period must be a whole number of 1 or more.";
            return;
        }

        var start = Format(StartTime);
        var end = Format(EndTime);

        // Clash check against this teacher's own periods. Note it can't see
        // other teachers' bookings for the same room — school-wide venue
        // conflict detection is a later milestone (see PLAN.md M4).
        var clash = Entries.FirstOrDefault(e => e.Day == SelectedDay
                                                && string.CompareOrdinal(start, e.EndTime) < 0
                                                && string.CompareOrdinal(end, e.StartTime) > 0);
        if (clash is not null)
        {
            StatusMessage = $"That overlaps your {clash.ClassName} period ({clash.StartTime}–{clash.EndTime}).";
            return;
        }

        IsBusy = true;
        try
        {
            await _classes.SaveTimetableEntryAsync(new TimetableEntry
            {
                ClassId = target.Id,
                ClassName = target.Name,
                DayOfWeek = SelectedDay,
                Period = periodNumber,
                StartTime = start,
                EndTime = end,
                Room = string.IsNullOrWhiteSpace(Room) ? target.Room : Room.Trim()
            });

            Room = string.Empty;
            await ReloadEntriesAsync();
            StatusMessage = $"Added {target.Name} on {SelectedDay} at {start}.";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "You can only schedule periods for classes you teach.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeacherTimetable.Add", ex);
            StatusMessage = "We couldn't add that period. Please try again.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task DeleteAsync(TimetableRow? row)
    {
        if (row is null || IsBusy) return;
        IsBusy = true;
        try
        {
            await _classes.DeleteTimetableEntryAsync(row.Id);
            await ReloadEntriesAsync();
            StatusMessage = "Period removed.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeacherTimetable.Delete", ex);
            StatusMessage = "We couldn't remove that period. Please try again.";
        }
        finally { IsBusy = false; }
    }

    private async Task ReloadEntriesAsync()
    {
        var all = await _classes.GetTimetableAsync();
        var mineIds = _myClasses.Select(c => c.Id).ToHashSet();
        Entries = new ObservableCollection<TimetableRow>(
            all.Where(e => mineIds.Contains(e.ClassId))
               .Select(e => new TimetableRow(e.Id, e.ClassName, e.DayOfWeek, e.StartTime, e.EndTime, e.Room)));
    }

    // "HH:mm", zero-padded so string ordering matches chronological ordering.
    private static string Format(TimeSpan time) => $"{time.Hours:D2}:{time.Minutes:D2}";
}

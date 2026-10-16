using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

/// <summary>A student with a tick box, for picking a squad.</summary>
public partial class SquadPick : ObservableObject
{
    public string Uid { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    [ObservableProperty] private bool isInSquad;
}

/// <summary>
/// One team as its coach runs it: the squad, and the notice board.
///
/// Deliberately the same shape as TeacherClassDetailViewModel — a coach who has
/// seen a class page can read this one without being taught it, and the two
/// stay in step because they're the same idea twice.
/// </summary>
public partial class TeamDetailViewModel : DashboardShellViewModel
{
    private readonly ITeamService _teams;
    private readonly IClassService _classes;

    private string _teamId = string.Empty;

    // Every student in the school, held separately from the filtered view.
    // Squad is a WINDOW onto these same instances, so ticks survive a search —
    // and saving reads from here rather than from what happens to be on screen.
    private List<SquadPick> _allStudents = [];
    private bool _studentsLoaded;

    public TeamDetailViewModel(ITeamService teams, IClassService classes)
    {
        _teams = teams;
        _classes = classes;
        BuildRail();
    }

    /// <summary>Set by the page; the view model doesn't know about scrolling.</summary>
    public Action<string>? OnRailAction { get; set; }

    /// <summary>
    /// Rail for this page. Same contract as everywhere: order matches the
    /// widgets top to bottom, and Home is the only item that doesn't scroll.
    /// </summary>
    private void BuildRail()
    {
        RailItems =
        [
            new(Icons.Home, "Home", new RelayCommand(() => OnRailAction?.Invoke("home"))),
            new(Icons.Announcement, "Team notices", new RelayCommand(() => OnRailAction?.Invoke("notices"))),
            new(Icons.Groups, "Squad", new RelayCommand(() => OnRailAction?.Invoke("squad"))),
            new(Icons.Settings, "Team details", new RelayCommand(() => OnRailAction?.Invoke("details")))
        ];
    }

    [ObservableProperty] private string teamName = string.Empty;
    [ObservableProperty] private string teamMeta = string.Empty;

    // ---- team details form ----

    [ObservableProperty] private string editName = string.Empty;
    [ObservableProperty] private string editSport = string.Empty;
    [ObservableProperty] private string editSeason = string.Empty;

    // ---- squad ----

    [ObservableProperty] private ObservableCollection<SquadPick> squad = [];

    /// <summary>Filters the picker by name or email.</summary>
    [ObservableProperty] private string studentSearch = string.Empty;

    /// <summary>"12 in the squad of 340 students" — so the count is visible while filtered.</summary>
    [ObservableProperty] private string squadSummary = string.Empty;

    partial void OnStudentSearchChanged(string value) => ApplySquadFilter();

    private void ApplySquadFilter()
    {
        var term = StudentSearch?.Trim();

        var matches = string.IsNullOrEmpty(term)
            ? _allStudents
            : _allStudents.Where(s =>
                s.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                s.Email.Contains(term, StringComparison.CurrentCultureIgnoreCase))
              .ToList();

        // Same objects, not copies — that's what keeps ticks intact across
        // searches, and what lets Save read the full list rather than the view.
        Squad = new ObservableCollection<SquadPick>(matches);
        UpdateSquadSummary();
    }

    private void UpdateSquadSummary()
    {
        var picked = _allStudents.Count(s => s.IsInSquad);
        var hidden = _allStudents.Count - Squad.Count;

        SquadSummary = hidden > 0
            ? $"{picked} in the squad of {_allStudents.Count} students · {hidden} hidden by search"
            : $"{picked} in the squad of {_allStudents.Count} students";
    }

    // ---- notices ----

    [ObservableProperty] private ObservableCollection<AnnouncementRow> notices = [];
    [ObservableProperty] private bool hasNotices;
    [ObservableProperty] private string noticesCount = "0";
    [ObservableProperty] private string noticesEmptyMessage = string.Empty;

    [ObservableProperty] private bool isNoticeFormVisible;
    [ObservableProperty] private string newNoticeTitle = string.Empty;
    [ObservableProperty] private string newNoticeBody = string.Empty;
    [ObservableProperty] private bool newNoticePinned;

    /// <summary>
    /// Whether this notice stops showing on a date. ON by default, with a
    /// fortnight filled in — same reasoning as school and class notices. A
    /// practice time is the most perishable thing in the app; leaving last
    /// week's fixture up is worse than an extra tap to make one permanent.
    /// </summary>
    [ObservableProperty] private bool newNoticeExpires = true;

    [ObservableProperty] private DateTime newNoticeExpiryDate = DateTime.Today.AddDays(14);

    private string _editingNoticeId = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoticeFormHeading))]
    [NotifyPropertyChangedFor(nameof(NoticeSaveText))]
    private bool isEditingNotice;

    public string NoticeFormHeading => IsEditingNotice ? "Edit notice" : "New team notice";
    public string NoticeSaveText => IsEditingNotice ? "Save changes" : "Post";

    /// <summary>
    /// The chosen expiry as an instant, or default for "never".
    ///
    /// End of the chosen day, not midnight at its start — "until Friday" means
    /// still up on Friday, which for a Friday fixture is the whole point.
    /// </summary>
    private DateTimeOffset ResolveExpiry()
        => NewNoticeExpires
            ? new DateTimeOffset(
                NewNoticeExpiryDate.Date.AddDays(1).AddSeconds(-1),
                DateTimeOffset.Now.Offset).ToUniversalTime()
            : default;

    public void Load(string teamId, string teamName)
    {
        _teamId = teamId;
        TeamName = teamName;
    }

    public async Task LoadAsync()
    {
        if (IsBusy || string.IsNullOrEmpty(_teamId)) return;

        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            var team = await _teams.GetTeamAsync(_teamId);
            if (team is null)
            {
                StatusMessage = "This team has been removed.";
                return;
            }

            TeamName = team.Name;
            TeamMeta = string.IsNullOrWhiteSpace(team.SportAndSeason)
                ? team.MemberLabel
                : $"{team.SportAndSeason} · {team.MemberLabel}";

            EditName = team.Name;
            EditSport = team.Sport;
            EditSeason = team.Season;

            await LoadSquadAsync(team);
            await ReloadNoticesAsync();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "You can only manage teams you coach.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "That took too long. Check your connection and try again.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeamDetail.Load", ex);
            StatusMessage = "We couldn't load this team. Please try again.";
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Builds the student picker.
    ///
    /// The roster is fetched once per visit, not once per reload: it's every
    /// student in the school, and refetching it on every save would be the most
    /// expensive thing on the page for no benefit.
    /// </summary>
    private async Task LoadSquadAsync(SportsTeam team)
    {
        if (!_studentsLoaded)
        {
            var students = await _classes.GetStudentsAsync();

            _allStudents = students
                .Select(s => new SquadPick
                {
                    Uid = s.Uid,
                    Name = string.IsNullOrWhiteSpace(s.DisplayName) ? s.Email : s.DisplayName,
                    Email = s.Email
                })
                .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            foreach (var student in _allStudents)
                student.PropertyChanged += OnSquadPickChanged;

            _studentsLoaded = true;
        }

        var inSquad = team.MemberIds.ToHashSet(StringComparer.Ordinal);
        foreach (var student in _allStudents)
            student.IsInSquad = inSquad.Contains(student.Uid);

        ApplySquadFilter();
    }

    private void OnSquadPickChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SquadPick.IsInSquad)) UpdateSquadSummary();
    }

    [RelayCommand]
    private async Task SaveSquadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            // _allStudents, NOT Squad: saving while a search is active would
            // otherwise drop every member the filter happens to be hiding.
            var picked = _allStudents.Where(s => s.IsInSquad).Select(s => s.Uid).ToList();

            await _teams.SetSquadAsync(_teamId, picked);

            StatusMessage = picked.Count == 1
                ? "1 student in the squad."
                : $"{picked.Count} students in the squad.";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "You can only change the squad of a team you coach.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeamDetail.SaveSquad", ex);
            StatusMessage = "We couldn't save the squad. Please try again.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SaveDetailsAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(EditName))
        {
            StatusMessage = "Give the team a name.";
            return;
        }

        IsBusy = true;
        try
        {
            await _teams.UpdateTeamAsync(_teamId, EditName, EditSport, EditSeason);
            TeamName = EditName.Trim();
            StatusMessage = "Team details saved.";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "You can only edit a team you coach.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeamDetail.SaveDetails", ex);
            StatusMessage = "We couldn't save the team details. Please try again.";
        }
        finally { IsBusy = false; }
    }

    // ---- notice commands ----

    [RelayCommand]
    private void StartCreateNotice()
    {
        ResetNoticeForm();
        IsNoticeFormVisible = true;
    }

    [RelayCommand]
    private void StartEditNotice(AnnouncementRow? row)
    {
        if (row is null) return;

        _editingNoticeId = row.Id;
        IsEditingNotice = true;
        NewNoticeTitle = row.Source.Title;
        NewNoticeBody = row.Source.Body;
        NewNoticePinned = row.Source.IsPinned;

        // Reflects what this notice actually has, so the opt-out default never
        // silently puts an end date on something that had none.
        NewNoticeExpires = row.Source.HasExpiry;
        if (row.Source.HasExpiry)
            NewNoticeExpiryDate = row.Source.ExpiresAt.ToLocalTime().Date;

        IsNoticeFormVisible = true;
    }

    [RelayCommand]
    private void CancelNoticeEdit() => ResetNoticeForm();

    [RelayCommand]
    private async Task SaveNoticeAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(NewNoticeTitle))
        {
            StatusMessage = "Give the notice a title.";
            return;
        }

        IsBusy = true;
        try
        {
            await _teams.SaveTeamNoticeAsync(_teamId, TeamName, new Announcement
            {
                Id = _editingNoticeId,
                Title = NewNoticeTitle,
                Body = NewNoticeBody,
                IsPinned = NewNoticePinned,
                ExpiresAt = ResolveExpiry()
            });

            ResetNoticeForm();
            await ReloadNoticesAsync();
            StatusMessage = "Notice posted to the squad.";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "You can only post notices to a team you coach.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeamDetail.SaveNotice", ex);
            StatusMessage = "We couldn't post that notice. Please try again.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task DeleteNoticeAsync(AnnouncementRow? row)
    {
        if (row is null || IsBusy) return;

        IsBusy = true;
        try
        {
            await _teams.DeleteTeamNoticeAsync(_teamId, row.Id);
            await ReloadNoticesAsync();
            StatusMessage = $"Removed {row.Title}.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeamDetail.DeleteNotice", ex);
            StatusMessage = "We couldn't remove that notice. Please try again.";
        }
        finally { IsBusy = false; }
    }

    private async Task ReloadNoticesAsync()
    {
        // includeExpired: on their own team page the coach is the AUTHOR, and
        // the point of showing an author a lapsed notice is so they can tell it
        // lapsed and extend it rather than wonder where it went.
        var items = await _teams.GetTeamNoticesAsync(_teamId, includeExpired: true);

        Notices = new ObservableCollection<AnnouncementRow>(
            items.Select(a => new AnnouncementRow(a, true)));

        HasNotices = items.Count > 0;
        NoticesCount = items.Count.ToString();
        NoticesEmptyMessage = HasNotices
            ? string.Empty
            : "No notices yet. Post practice times, kit and meeting points here — "
              + "the squad and their parents see them.";
    }

    private void ResetNoticeForm()
    {
        _editingNoticeId = string.Empty;
        IsEditingNotice = false;
        IsNoticeFormVisible = false;
        NewNoticeTitle = string.Empty;
        NewNoticeBody = string.Empty;
        NewNoticePinned = false;
        NewNoticeExpires = true;
        NewNoticeExpiryDate = DateTime.Today.AddDays(14);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try { await LoadAsync(); }
        finally { IsRefreshing = false; }
    }
}

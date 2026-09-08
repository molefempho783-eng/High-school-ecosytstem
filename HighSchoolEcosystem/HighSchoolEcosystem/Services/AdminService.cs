using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.Services;

public sealed class AdminService : IAdminService
{
    private readonly IFirestoreClient _firestore;

    public AdminService(IFirestoreClient firestore) => _firestore = firestore;

    public async Task<IReadOnlyList<UserProfile>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        var documents = await _firestore.ListDocumentsAsync<UserProfile>("users", cancellationToken);

        return documents
            // The document ID is the authoritative UID. The Uid field is written
            // by the client at bootstrap and could in principle be stale or
            // missing, so prefer the ID — admin writes are addressed by it.
            .Select(d => WithId(d.Value, d.Id))
            .OrderBy(u => string.IsNullOrWhiteSpace(u.DisplayName) ? u.Email : u.DisplayName,
                     StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static UserProfile WithId(UserProfile profile, string id) => new()
    {
        Uid = id,
        DisplayName = profile.DisplayName,
        Email = profile.Email,
        Role = profile.Role,
        Roles = profile.Roles,
        ClassIds = profile.ClassIds,
        LinkedStudentIds = profile.LinkedStudentIds
    };

    public Task SetUserRolesAsync(string uid, Role primaryRole, IReadOnlyList<Role> extraRoles, CancellationToken cancellationToken = default)
        => _firestore.PatchFieldsAsync($"users/{uid}", new Dictionary<string, object?>
        {
            ["Role"] = primaryRole,
            // Stored as strings to match how the converter reads arrays back,
            // and the primary role is never duplicated into the list.
            ["Roles"] = extraRoles.Where(r => r != primaryRole)
                                  .Select(r => r.ToString())
                                  .Distinct()
                                  .ToList()
        }, cancellationToken);

    public Task SetLinkedStudentsAsync(string parentUid, IReadOnlyList<string> studentUids, CancellationToken cancellationToken = default)
        => _firestore.PatchFieldsAsync($"users/{parentUid}",
            new Dictionary<string, object?> { ["LinkedStudentIds"] = studentUids.ToList() }, cancellationToken);

    public Task SetClassIdsAsync(string uid, IReadOnlyList<string> classIds, CancellationToken cancellationToken = default)
        => _firestore.PatchFieldsAsync($"users/{uid}",
            new Dictionary<string, object?> { ["ClassIds"] = classIds.ToList() }, cancellationToken);

    // ---- roster ----

    public async Task<IReadOnlyList<RosterEntry>> GetInvitesAsync(CancellationToken cancellationToken = default)
    {
        var documents = await _firestore.ListDocumentsAsync<Invite>("invites", cancellationToken);
        return documents
            .Select(d => new RosterEntry(d.Id, d.Value))
            .OrderBy(e => e.Email, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task SaveInviteAsync(string email, Invite invite, CancellationToken cancellationToken = default)
        // Lower-cased to match the rules, which look the document up by
        // request.auth.token.email.lower().
        => _firestore.SetDocumentAsync($"invites/{Uri.EscapeDataString(email.Trim().ToLowerInvariant())}", invite, cancellationToken);

    public Task DeleteInviteAsync(string email, CancellationToken cancellationToken = default)
        => _firestore.DeleteDocumentAsync($"invites/{Uri.EscapeDataString(email.Trim().ToLowerInvariant())}", cancellationToken);

    // ---- school settings ----

    public Task<SchoolInfo?> GetSchoolConfigAsync(CancellationToken cancellationToken = default)
        => _firestore.GetDocumentAsync<SchoolInfo>("meta/config", cancellationToken);

    public Task SaveSchoolConfigAsync(SchoolInfo info, CancellationToken cancellationToken = default)
        => _firestore.SetDocumentAsync("meta/config", info, cancellationToken);
}

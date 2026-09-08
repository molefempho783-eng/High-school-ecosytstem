using HighSchoolEcosystem.Models;

namespace HighSchoolEcosystem.Services.Interfaces;

/// <summary>
/// Admin-only operations. Every call here is also gated by the Firestore
/// security rules — this interface is a convenience for the admin UI, not the
/// security boundary. A non-admin calling these gets PERMISSION_DENIED.
/// </summary>
public interface IAdminService
{
    /// <summary>Everyone who has registered, ordered by display name.</summary>
    Task<IReadOnlyList<UserProfile>> GetUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a user's primary role and any additional roles held alongside it.
    /// Takes effect on their next profile load.
    /// </summary>
    Task SetUserRolesAsync(string uid, Role primaryRole, IReadOnlyList<Role> extraRoles, CancellationToken cancellationToken = default);

    /// <summary>Sets which students a parent may view. Replaces the existing list.</summary>
    Task SetLinkedStudentsAsync(string parentUid, IReadOnlyList<string> studentUids, CancellationToken cancellationToken = default);

    /// <summary>Sets which classes a user is enrolled in or teaches.</summary>
    Task SetClassIdsAsync(string uid, IReadOnlyList<string> classIds, CancellationToken cancellationToken = default);

    // ---- roster (pre-registration invites) ----

    Task<IReadOnlyList<RosterEntry>> GetInvitesAsync(CancellationToken cancellationToken = default);
    Task SaveInviteAsync(string email, Invite invite, CancellationToken cancellationToken = default);
    Task DeleteInviteAsync(string email, CancellationToken cancellationToken = default);

    // ---- school settings ----

    Task<SchoolInfo?> GetSchoolConfigAsync(CancellationToken cancellationToken = default);
    Task SaveSchoolConfigAsync(SchoolInfo info, CancellationToken cancellationToken = default);
}

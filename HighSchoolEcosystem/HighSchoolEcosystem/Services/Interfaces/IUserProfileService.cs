using HighSchoolEcosystem.Models;

namespace HighSchoolEcosystem.Services.Interfaces;

public interface IUserProfileService
{
    /// <summary>
    /// Loads the signed-in user's profile, creating a minimal default one on
    /// first sign-in. Returns null if nobody is signed in.
    /// </summary>
    Task<UserProfile?> GetOrCreateCurrentProfileAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames the signed-in user's profile document. The rules allow a user to
    /// edit their own DisplayName — only Role/Roles/ClassIds/LinkedStudentIds
    /// are pinned — so this needs no admin rights.
    /// </summary>
    Task UpdateDisplayNameAsync(string displayName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Another user's profile by UID, or null if it can't be read.
    ///
    /// Used by the parent dashboard to resolve children's names. The rules
    /// allow this for self, admins, staff and a linked parent — everyone else
    /// gets a denial, which surfaces here as null rather than an exception.
    /// </summary>
    Task<UserProfile?> GetProfileAsync(string uid, CancellationToken cancellationToken = default);
}

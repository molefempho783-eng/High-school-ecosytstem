namespace HighSchoolEcosystem.Models;

/// <summary>
/// Mirrors a document at /users/{uid}. Created on first sign-in (see
/// UserProfileService) and read on every subsequent launch to drive role-based
/// navigation and Firestore security rule checks.
/// </summary>
public sealed class UserProfile
{
    public string Uid { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// The primary role — decides which dashboard this user lands on. Always
    /// counts as held, whether or not it also appears in <see cref="Roles"/>.
    /// </summary>
    public Role Role { get; init; } = Role.Student;

    /// <summary>
    /// Additional roles held alongside <see cref="Role"/>, e.g. an Admin who
    /// also teaches, or a Student who also writes for the newspaper.
    ///
    /// Stored as strings because the Firestore converter reads every array back
    /// as List&lt;string&gt;; use <see cref="HasRole"/> rather than comparing by
    /// hand. Empty on older documents written before multi-role support, which
    /// the rules and <see cref="HasRole"/> both tolerate.
    /// </summary>
    public List<string> Roles { get; init; } = [];

    /// <summary>Class IDs this user is enrolled in (students) or teaches (teachers).</summary>
    public List<string> ClassIds { get; init; } = [];

    /// <summary>Student user IDs this parent is linked to. Empty for non-parents.</summary>
    public List<string> LinkedStudentIds { get; init; } = [];

    /// <summary>True if this user holds <paramref name="role"/>, primary or additional.</summary>
    /// <remarks>
    /// Ordinal, case-SENSITIVE on purpose. The Firestore rules compare role
    /// strings exactly ('Admin', not 'admin'), so matching loosely here would
    /// show a user admin UI that the server then refuses to honour.
    /// </remarks>
    public bool HasRole(Role role)
        => Role == role || Roles.Contains(role.ToString(), StringComparer.Ordinal);

    /// <summary>Every role held, primary first, without duplicates.</summary>
    public IReadOnlyList<Role> AllRoles()
    {
        var all = new List<Role> { Role };
        foreach (var name in Roles)
        {
            // ignoreCase: false, to match the rules exactly — see HasRole.
            if (Enum.TryParse<Role>(name, ignoreCase: false, out var parsed) && !all.Contains(parsed))
            {
                all.Add(parsed);
            }
        }
        return all;
    }
}

namespace HighSchoolEcosystem.Models;

/// <summary>
/// Mirrors a document at /invites/{email}, where {email} is the invitee's
/// address lower-cased. This is the school's roster: an admin adds an entry
/// ahead of time, and when that person signs up their profile is created with
/// the role recorded here.
///
/// Only admins can write these. The Firestore rules cross-check any
/// self-created /users/{uid} document against the matching invite, so the
/// client can't award itself a role that isn't on the roster.
/// </summary>
public sealed class Invite
{
    /// <summary>Primary role — decides which dashboard they land on.</summary>
    public Role Role { get; init; } = Role.Student;

    /// <summary>
    /// Additional roles granted alongside <see cref="Role"/> (e.g. Journalist).
    /// Copied verbatim onto the profile at sign-up, and checked by the rules.
    /// </summary>
    public List<string> Roles { get; init; } = [];

    /// <summary>Classes to enrol the user in (students) or assign them to teach (teachers).</summary>
    public List<string> ClassIds { get; init; } = [];

    /// <summary>For Parent invites: the student user IDs this parent may view.</summary>
    public List<string> LinkedStudentIds { get; init; } = [];
}

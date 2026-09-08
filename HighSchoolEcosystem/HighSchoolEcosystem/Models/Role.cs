namespace HighSchoolEcosystem.Models;

/// <summary>
/// A role within the school. A user has one <b>primary</b> role (which decides
/// which dashboard they land on) and may hold additional roles alongside it —
/// see <see cref="UserProfile.Roles"/>. So an admin can also be a teacher, and
/// a student can also be a journalist.
///
/// Roles are assigned from the admin-managed roster at /invites/{email} or by
/// an admin afterwards — never chosen by the client. The Firestore rules reject
/// any self-assigned role that doesn't match the roster.
///
/// People browsing without an account at all are "guests": they see the same
/// public content as <see cref="Visitor"/> but have no profile document.
/// </summary>
public enum Role
{
    /// <summary>Default for anyone signing up whose email isn't on the roster.</summary>
    Student,
    Teacher,
    Parent,
    Admin,

    /// <summary>
    /// Signed in, but only entitled to public content — announcements marked
    /// public, sports fixtures and sports news. Intended for prospective
    /// families, alumni, and the wider community.
    /// </summary>
    Visitor,

    /// <summary>
    /// Runs extracurricular teams. Sees the school community surface like a
    /// teacher does, and can publish sports fixtures, results and sports news —
    /// but has no gradebook or class ownership.
    /// </summary>
    Coach,

    /// <summary>
    /// Writes for the school newspaper. Intended as an <b>additional</b> role
    /// held alongside Student or Teacher rather than on its own — a student
    /// journalist is still a student, and keeps every student entitlement.
    /// Grants writing access to the newspaper, nothing else.
    /// </summary>
    Journalist
}

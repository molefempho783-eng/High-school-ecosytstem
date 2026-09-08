namespace HighSchoolEcosystem.Models;

/// <summary>
/// A roster entry paired with the email it's keyed by. The email lives in the
/// Firestore document ID rather than a field, so it has to be carried alongside
/// the <see cref="Invite"/> for the admin UI to display it.
/// </summary>
public sealed record RosterEntry(string Email, Invite Invite);

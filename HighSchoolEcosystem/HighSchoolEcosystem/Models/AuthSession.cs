namespace HighSchoolEcosystem.Models;

/// <summary>
/// The signed-in Firebase user. <paramref name="DisplayName"/> is the name set
/// on the Firebase Auth account itself, so it follows the user to any device —
/// unlike a name held only in the Firestore profile.
/// </summary>
public sealed record AuthSession(
    string UserId,
    string? Email,
    bool IsEmailVerified,
    string? DisplayName = null);

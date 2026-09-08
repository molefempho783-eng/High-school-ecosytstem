namespace HighSchoolEcosystem.Services.Firebase;

/// <summary>
/// Thrown when a user is on the school's roster for a privileged role
/// (Teacher, Parent or Admin) but hasn't verified their email address yet, so
/// the Firestore rules refuse to create their profile.
///
/// This is a normal, recoverable state — not a failure. The user needs to click
/// the link in their verification email and sign in again, at which point their
/// ID token carries email_verified and the profile is created with the role
/// their invite specifies.
/// </summary>
public sealed class ProfileVerificationRequiredException : Exception
{
    public string? Email { get; }

    public ProfileVerificationRequiredException(string? email)
        : base("Verify your email address to finish setting up your account.")
        => Email = email;
}

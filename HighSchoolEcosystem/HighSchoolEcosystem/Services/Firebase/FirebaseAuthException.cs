namespace HighSchoolEcosystem.Services.Firebase;

/// <summary>
/// Thrown when the Firebase Identity Toolkit REST API returns an error.
/// <see cref="ErrorCode"/> is Firebase's raw code (e.g. "EMAIL_EXISTS");
/// <see cref="Message"/> is already user-facing, so view models can surface it
/// directly instead of showing a generic failure string.
/// </summary>
public sealed class FirebaseAuthException : Exception
{
    public string ErrorCode { get; }

    public FirebaseAuthException(string errorCode, string message) : base(message)
        => ErrorCode = errorCode;

    public static FirebaseAuthException FromErrorCode(string errorCode)
        => new(errorCode, ToFriendlyMessage(errorCode));

    private static string ToFriendlyMessage(string errorCode) => errorCode switch
    {
        "EMAIL_EXISTS" => "That email address already has an account. Try signing in instead.",
        "OPERATION_NOT_ALLOWED" => "Email and password sign-in is disabled for this project. Enable it in the Firebase console.",
        "TOO_MANY_ATTEMPTS_TRY_LATER" => "Too many attempts. Please wait a moment and try again.",
        // The client asks for 8+, so this only fires if Firebase's own policy
        // is stricter still. Keep it generic rather than naming a length that
        // contradicts the sign-up form.
        "WEAK_PASSWORD" => "That password is too weak. Try a longer one with a mix of characters.",
        "EMAIL_NOT_FOUND" => "We couldn't find an account with that email address.",
        "INVALID_PASSWORD" => "That password is incorrect.",
        "INVALID_LOGIN_CREDENTIALS" => "That email address or password is incorrect.",
        "USER_DISABLED" => "That account has been disabled. Contact your school administrator.",
        "INVALID_EMAIL" => "That doesn't look like a valid email address.",
        "MISSING_PASSWORD" => "Enter your password.",
        "TOKEN_EXPIRED" or "USER_NOT_FOUND" or "INVALID_REFRESH_TOKEN"
            => "Your session has expired. Please sign in again.",
        _ => $"Authentication failed ({errorCode})."
    };
}

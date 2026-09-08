namespace HighSchoolEcosystem.Models;

/// <summary>
/// Branding for the one school this build serves. Each client gets their own
/// build: their own Firebase project (set Firebase:ProjectId and
/// Firebase:WebApiKey) and their own branding (the "School" section), both in
/// Configuration/appsettings.json. There is no runtime tenant switching —
/// one deployment, one school, one database.
/// </summary>
public sealed class SchoolInfo
{
    public string DisplayName { get; init; } = "My School";
    public string PrimaryColor { get; init; } = "#156B76";
    public string SecondaryColor { get; init; } = "#123A5A";
    public string AccentColor { get; init; } = "#E98D52";
    public string LogoUrl { get; init; } = string.Empty;
}

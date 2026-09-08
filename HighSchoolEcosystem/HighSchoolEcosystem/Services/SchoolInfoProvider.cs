using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services.Interfaces;
using Microsoft.Extensions.Configuration;

namespace HighSchoolEcosystem.Services;

public sealed class SchoolInfoProvider : ISchoolInfoProvider
{
    public SchoolInfo Current { get; }

    // Reads the "School" section by indexer rather than IConfiguration.Bind()
    // to avoid adding a Microsoft.Extensions.Configuration.Binder package
    // reference for four string lookups.
    public SchoolInfoProvider(IConfiguration configuration)
    {
        var defaults = new SchoolInfo();
        Current = new SchoolInfo
        {
            DisplayName = configuration["School:DisplayName"] ?? defaults.DisplayName,
            PrimaryColor = configuration["School:PrimaryColor"] ?? defaults.PrimaryColor,
            SecondaryColor = configuration["School:SecondaryColor"] ?? defaults.SecondaryColor,
            AccentColor = configuration["School:AccentColor"] ?? defaults.AccentColor,
            LogoUrl = configuration["School:LogoUrl"] ?? defaults.LogoUrl
        };
    }
}

using HighSchoolEcosystem.Models;

namespace HighSchoolEcosystem.Services.Interfaces;

public interface ISchoolInfoProvider
{
    /// <summary>Branding for this build's school, read from appsettings.json's "School" section.</summary>
    SchoolInfo Current { get; }
}

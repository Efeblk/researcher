using System.Text.RegularExpressions;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

public sealed class ResearcherIdentifierParser
{
    private const string TestOrcid = "0000-0003-2812-9917";
    private const string TestGoogleScholarId = "dYpPMQEAAAAJ";

    private static readonly Regex OrcidPattern = new(
        @"^\d{4}-\d{4}-\d{4}-\d{3}[\dX]$",
        RegexOptions.IgnoreCase);

    private static readonly Regex WebOfScienceResearcherIdPattern = new(
        @"^[A-Z]+-\d{4}-\d{4}$",
        RegexOptions.IgnoreCase);

    private static readonly Regex GoogleScholarIdPattern = new(
        @"^[A-Z0-9_-]{12}$",
        RegexOptions.IgnoreCase);

    public Researcher Create(ResearcherCollectRequest request)
    {
        Researcher? researcher = null;
        List<string>? identifiers = null;
        int index = 0;
        string? identifier = null;

        researcher = new Researcher();
        identifiers = request.Identifiers ?? [];

        if (identifiers.Count == 0 && request.UseTestIdentifiers)
        {
            researcher.Orcid = TestOrcid;
            researcher.GoogleScholarId = TestGoogleScholarId;
            return researcher;
        }

        for (index = 0; index < identifiers.Count; index++)
        {
            identifier = identifiers[index];

            if (TryAssignNamedIdentifier(researcher, identifiers, ref index))
            {
                continue;
            }

            if (TryAssignDetectedIdentifier(researcher, identifier))
            {
                continue;
            }

            throw new ArgumentException($"Bilinmeyen veya eksik kimlik: {identifier}");
        }

        return researcher;
    }

    private static bool TryAssignNamedIdentifier(
        Researcher researcher,
        List<string> identifiers,
        ref int index)
    {
        string? argumentName = null;
        string? identifier = null;

        argumentName = identifiers[index];

        if (argumentName != "--orcid" &&
            argumentName != "--scholar" &&
            argumentName != "--wos")
        {
            return false;
        }

        if (index + 1 >= identifiers.Count)
        {
            throw new ArgumentException($"{argumentName} için bir kimlik değeri verilmelidir.");
        }

        identifier = identifiers[index + 1];
        index++;

        if (argumentName == "--orcid")
        {
            EnsureIdentifierIsEmpty(researcher.Orcid, "ORCID");
            researcher.Orcid = identifier;
            return true;
        }

        if (argumentName == "--scholar")
        {
            EnsureIdentifierIsEmpty(researcher.GoogleScholarId, "Google Scholar ID");
            researcher.GoogleScholarId = identifier;
            return true;
        }

        EnsureIdentifierIsEmpty(
            researcher.WebOfScienceResearcherId,
            "Web of Science ResearcherID");
        researcher.WebOfScienceResearcherId = identifier;

        return true;
    }

    private static bool TryAssignDetectedIdentifier(Researcher researcher, string identifier)
    {
        if (OrcidPattern.IsMatch(identifier))
        {
            EnsureIdentifierIsEmpty(researcher.Orcid, "ORCID");
            researcher.Orcid = identifier;
            return true;
        }

        if (WebOfScienceResearcherIdPattern.IsMatch(identifier))
        {
            EnsureIdentifierIsEmpty(
                researcher.WebOfScienceResearcherId,
                "Web of Science ResearcherID");
            researcher.WebOfScienceResearcherId = identifier;
            return true;
        }

        if (GoogleScholarIdPattern.IsMatch(identifier))
        {
            EnsureIdentifierIsEmpty(researcher.GoogleScholarId, "Google Scholar ID");
            researcher.GoogleScholarId = identifier;
            return true;
        }

        return false;
    }

    private static void EnsureIdentifierIsEmpty(string? currentValue, string identifierName)
    {
        if (!string.IsNullOrWhiteSpace(currentValue))
        {
            throw new ArgumentException($"Birden fazla {identifierName} verildi.");
        }
    }
}

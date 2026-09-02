using System.Text.RegularExpressions;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;

public sealed class ResearcherIdentifierParser
{
    private static readonly Regex OrcidPattern = new(
        @"^\d{4}-\d{4}-\d{4}-\d{3}[\dX]$",
        RegexOptions.IgnoreCase);
    private static readonly Regex WebOfScienceResearcherIdPattern = new(
        @"^[A-Z]{1,3}-\d{4}-\d{4}$",
        RegexOptions.IgnoreCase);
    private static readonly Regex GoogleScholarIdPattern = new(
        @"^[A-Za-z0-9_-]{12}$",
        RegexOptions.CultureInvariant);

    public Researcher Create(ResearcherCollectRequest request)
    {
        Researcher? researcher = null;
        List<string>? identifiers = null;
        int index = 0;
        string? identifier = null;

        researcher = new Researcher();
        identifiers = request.Identifiers ?? [];

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

        if (string.IsNullOrWhiteSpace(researcher.Orcid) &&
            string.IsNullOrWhiteSpace(researcher.GoogleScholarId) &&
            string.IsNullOrWhiteSpace(researcher.WebOfScienceResearcherId))
        {
            throw new ArgumentException(
                "ORCID, Google Scholar ID veya Web of Science ResearcherID " +
                "verilmelidir.");
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
            argumentName != "--googlescholar" &&
            argumentName != "--researcherid" &&
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
        }
        else if (argumentName is "--scholar" or "--googlescholar")
        {
            EnsureIdentifierIsEmpty(researcher.GoogleScholarId, "Google Scholar ID");
            researcher.GoogleScholarId = NormalizeGoogleScholarId(identifier);
        }
        else
        {
            EnsureIdentifierIsEmpty(
                researcher.WebOfScienceResearcherId,
                "Web of Science ResearcherID");
            researcher.WebOfScienceResearcherId = identifier;
        }

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
            researcher.WebOfScienceResearcherId = identifier.ToUpperInvariant();
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

    private static string NormalizeGoogleScholarId(string identifier)
    {
        string normalized = identifier.Trim();

        if (!GoogleScholarIdPattern.IsMatch(normalized))
        {
            throw new ArgumentException("Google Scholar ID 12 karakter olmalıdır.");
        }

        return normalized;
    }

    private static void EnsureIdentifierIsEmpty(string? currentValue, string identifierName)
    {
        if (!string.IsNullOrWhiteSpace(currentValue))
        {
            throw new ArgumentException($"Birden fazla {identifierName} verildi.");
        }
    }
}

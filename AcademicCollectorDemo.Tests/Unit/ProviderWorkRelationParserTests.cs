using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class ProviderWorkRelationParserTests
{
    [Fact]
    public void FindOrcidSelfDoi_VersionTargetPrecedesSelf_ReturnsSelfDoi()
    {
        using JsonDocument document = JsonDocument.Parse(OrcidPayload(
            "10.1000/parent", "version-of", "10.1000/work", "self"));

        Assert.Equal("10.1000/work",
            ProviderWorkRelationParser.FindOrcidSelfDoi(document.RootElement));
    }

    [Fact]
    public void FindOrcidSelfDoi_SeveralSelfDois_ReturnsNull()
    {
        using JsonDocument document = JsonDocument.Parse(OrcidPayload(
            "10.1000/one", "self", "10.1000/two", "self"));

        Assert.Null(ProviderWorkRelationParser.FindOrcidSelfDoi(document.RootElement));
    }

    [Fact]
    public void Resolve_OrcidVersionOf_UsesStableEquivalenceIdentity()
    {
        AcademicWork first = Work(AcademicWorkProvider.Orcid, "10.1000/one",
            OrcidPayload("10.1000/one", "self", "10.1000/two", "version-of"));
        AcademicWork second = Work(AcademicWorkProvider.Orcid, "10.1000/two",
            OrcidPayload("10.1000/two", "self", "10.1000/one", "version-of"));

        WorkIdentity singleton = Assert.Single(CanonicalWorkIdentityResolver.Resolve([first]));
        List<WorkIdentity> identities = CanonicalWorkIdentityResolver.Resolve([first, second]);

        Assert.Equal(identities[0].DictionaryKey, identities[1].DictionaryKey);
        Assert.Equal(singleton.DictionaryKey, identities[0].DictionaryKey);
        Assert.Null(identities[0].NormalizedDoi);
        Assert.NotNull(identities[0].SourceScopedKey);
    }

    [Fact]
    public void Resolve_CrossrefSiblingsWithAbsentParent_UsesParentDoi()
    {
        AcademicWork first = Work(AcademicWorkProvider.Crossref, "10.1000/one",
            CrossrefPayload("10.1000/one", "is-version-of", "10.1000/concept"));
        AcademicWork second = Work(AcademicWorkProvider.Crossref, "10.1000/two",
            CrossrefPayload("10.1000/two", "is-version-of", "10.1000/concept"));

        List<WorkIdentity> identities = CanonicalWorkIdentityResolver.Resolve([first, second]);

        Assert.All(identities, identity => Assert.Equal("10.1000/concept", identity.NormalizedDoi));
    }

    [Fact]
    public void Resolve_CrossrefCycle_KeepsOriginalDoisSeparate()
    {
        AcademicWork first = Work(AcademicWorkProvider.Crossref, "10.1000/one",
            CrossrefPayload("10.1000/one", "is-version-of", "10.1000/two"));
        AcademicWork second = Work(AcademicWorkProvider.Crossref, "10.1000/two",
            CrossrefPayload("10.1000/two", "is-version-of", "10.1000/one"));
        AcademicWork equivalent = Work(AcademicWorkProvider.Orcid, "10.1000/extra",
            OrcidPayload("10.1000/extra", "self", "10.1000/one", "version-of"));

        List<WorkIdentity> identities = CanonicalWorkIdentityResolver.Resolve([first, second, equivalent]);

        Assert.Equal("10.1000/one", identities[0].NormalizedDoi);
        Assert.Equal("10.1000/two", identities[1].NormalizedDoi);
        Assert.Equal("10.1000/extra", identities[2].NormalizedDoi);
    }

    [Fact]
    public void Resolve_CrossrefPartOf_DoesNotMergeDois()
    {
        AcademicWork work = Work(AcademicWorkProvider.Crossref, "10.1000/one",
            CrossrefPayload("10.1000/one", "is-part-of", "10.1000/collection"));

        WorkIdentity identity = Assert.Single(CanonicalWorkIdentityResolver.Resolve([work]));

        Assert.Equal("10.1000/one", identity.NormalizedDoi);
    }

    [Fact]
    public void Resolve_DoiLessMetadataMatch_AttachesToSingleRelationIdentity()
    {
        AcademicWork related = Work(AcademicWorkProvider.Orcid, "10.1000/one",
            OrcidPayload("10.1000/one", "self", "10.1000/two", "version-of"));
        related.Title = "Shared work";
        related.PublicationYear = 2025;
        related.Authors = "Ada Lovelace";
        AcademicWork withoutDoi = Work(AcademicWorkProvider.OpenAlex, null, "{}");
        withoutDoi.Title = related.Title;
        withoutDoi.PublicationYear = related.PublicationYear;
        withoutDoi.Authors = related.Authors;
        AcademicWork unrelated = Work(AcademicWorkProvider.Crossref, "10.1000/unrelated", "{}");

        List<WorkIdentity> identities = CanonicalWorkIdentityResolver.Resolve(
            [related, withoutDoi, unrelated]);

        Assert.Equal(identities[0].DictionaryKey, identities[1].DictionaryKey);
        Assert.NotEqual(identities[0].DictionaryKey, identities[2].DictionaryKey);
    }

    [Fact]
    public void Resolve_RedundantOrcidAndCrossrefEvidence_UsesDirectedParent()
    {
        AcademicWork orcid = Work(AcademicWorkProvider.Orcid, "10.1000/one",
            OrcidPayload("10.1000/one", "self", "10.1000/two", "version-of"));
        AcademicWork crossref = Work(AcademicWorkProvider.Crossref, "10.1000/one",
            CrossrefPayload("10.1000/one", "is-version-of", "10.1000/two"));

        List<WorkIdentity> identities = CanonicalWorkIdentityResolver.Resolve([orcid, crossref]);

        Assert.All(identities, identity => Assert.Equal("10.1000/two", identity.NormalizedDoi));
    }

    [Fact]
    public void Resolve_OrcidEquivalentAttachedToCrossrefChain_UsesDirectedRoot()
    {
        AcademicWork equivalent = Work(AcademicWorkProvider.Orcid, "10.1000/extra",
            OrcidPayload("10.1000/extra", "self", "10.1000/one", "version-of"));
        AcademicWork version = Work(AcademicWorkProvider.Crossref, "10.1000/one",
            CrossrefPayload("10.1000/one", "is-version-of", "10.1000/root"));

        List<WorkIdentity> identities = CanonicalWorkIdentityResolver.Resolve([equivalent, version]);

        Assert.All(identities, identity => Assert.Equal("10.1000/root", identity.NormalizedDoi));
    }

    [Fact]
    public void Resolve_CrossrefHasVersion_UsesSourceAsParent()
    {
        AcademicWork parent = Work(AcademicWorkProvider.Crossref, "10.1000/root",
            CrossrefPayload("10.1000/root", "has-version", "10.1000/version"));
        AcademicWork version = Work(AcademicWorkProvider.Orcid, "10.1000/version", "{}");

        List<WorkIdentity> identities = CanonicalWorkIdentityResolver.Resolve([parent, version]);

        Assert.All(identities, identity => Assert.Equal("10.1000/root", identity.NormalizedDoi));
    }

    [Fact]
    public void Resolve_ConflictingCrossrefParents_KeepsOriginalDoi()
    {
        AcademicWork work = Work(AcademicWorkProvider.Crossref, "10.1000/one", """
            {"message":{"DOI":"10.1000/one","relation":{"is-version-of":[
              {"id-type":"doi","id":"10.1000/a"},{"id-type":"doi","id":"10.1000/b"}
            ]}}}
            """);

        WorkIdentity identity = Assert.Single(CanonicalWorkIdentityResolver.Resolve([work]));

        Assert.Equal("10.1000/one", identity.NormalizedDoi);
    }

    [Fact]
    public void Parse_MismatchedPayloadDoi_RejectsRelationEvidence()
    {
        AcademicWork work = Work(AcademicWorkProvider.Crossref, "10.1000/one",
            CrossrefPayload("10.1000/other", "is-version-of", "10.1000/root"));

        Assert.Empty(ProviderWorkRelationParser.Parse(work));
    }

    [Fact]
    public void FindOrcidSelfDoi_OnlyPartOfAndVersionOf_ReturnsNull()
    {
        using JsonDocument document = JsonDocument.Parse(OrcidPayload(
            "10.1000/parent", "part-of", "10.1000/version", "version-of"));

        Assert.Null(ProviderWorkRelationParser.FindOrcidSelfDoi(document.RootElement));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    public void Parse_MalformedOrNonObjectPayload_ReturnsNoEvidence(string payload)
    {
        Assert.Empty(ProviderWorkRelationParser.Parse(
            Work(AcademicWorkProvider.Crossref, "10.1000/one", payload)));
    }

    private static AcademicWork Work(AcademicWorkProvider provider, string? doi, string payload) => new()
    {
        PersonelId = "P-1", Provider = provider, Doi = doi, ProviderPayload = payload
    };

    private static string OrcidPayload(string firstDoi, string firstRelationship,
        string secondDoi, string secondRelationship) =>
        $"{{\"external-ids\":{{\"external-id\":[" +
        $"{{\"external-id-type\":\"doi\",\"external-id-value\":\"{firstDoi}\",\"external-id-relationship\":\"{firstRelationship}\"}}," +
        $"{{\"external-id-type\":\"doi\",\"external-id-value\":\"{secondDoi}\",\"external-id-relationship\":\"{secondRelationship}\"}}]}}}}";

    private static string CrossrefPayload(string doi, string relationship, string target) =>
        $"{{\"message\":{{\"DOI\":\"{doi}\",\"relation\":{{\"{relationship}\":[" +
        $"{{\"id-type\":\"doi\",\"id\":\"{target}\"}}]}}}}}}";
}

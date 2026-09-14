using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class OpenAlexResearchContextParserTests
{
    [Fact]
    public void Parse_OfficialTopicAndNormalizationShape_PreservesMeaningAndNullableFlags()
    {
        AcademicWork work = Work("W1", """
            {
              "id":"https://openalex.org/W1",
              "updated_date":"2026-04-03",
              "publication_year":2024,
              "type":"article",
              "primary_location":{"source":{"type":"journal"}},
              "fwci":0,
              "citation_normalized_percentile":{"value":0.999948,"is_in_top_1_percent":true,"is_in_top_10_percent":false},
              "primary_topic":{"id":"https://openalex.org/T100"},
              "topics":[
                {"id":"https://openalex.org/T100","display_name":"Primary","score":-2.5,
                 "subfield":{"id":2740,"display_name":"AI"},
                 "field":{"id":27,"display_name":"Computer Science"},
                 "domain":{"id":4,"display_name":"Physical Sciences"}},
                {"id":"https://openalex.org/T200","display_name":"Secondary","score":0.2}
              ]
            }
            """);

        AcademicWorkResearchContext result = OpenAlexResearchContextParser.Parse(work);

        Assert.Equal("Available", result.ParseQuality);
        Assert.Equal(0, result.Fwci);
        Assert.Equal(0.999948m, result.CitationNormalizedPercentile);
        Assert.True(result.IsInTopOnePercent);
        Assert.False(result.IsInTopTenPercent);
        Assert.Equal(DateTimeKind.Utc, result.ProviderUpdatedAt!.Value.Kind);
        Assert.Equal(2, result.Topics.Count);
        AcademicWorkTopic primary = result.Topics.Single(topic => topic.IsPrimary);
        Assert.Equal("2740", primary.SubfieldId);
        Assert.Equal("27", primary.FieldId);
        Assert.Equal("4", primary.DomainId);
        Assert.Equal(-2.5, primary.AssignmentScore);
        Assert.Equal("Available", primary.ScoreQuality);
        Assert.False(result.Topics.Single(topic => topic.TopicId.EndsWith("T200")).IsPrimary);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"id\":\"W1\",\"citation_normalized_percentile\":null,\"publication_year\":null}")]
    [InlineData("{\"id\":\"W1\",\"citation_normalized_percentile\":\"bad\",\"publication_year\":\"bad\"}")]
    public void Parse_BoundedMissingOrWrongTypedValues_DoesNotThrow(string payload)
    {
        AcademicWorkResearchContext result = OpenAlexResearchContextParser.Parse(Work("W1", payload));
        Assert.NotNull(result.ValueQualityJson);
    }

    [Fact]
    public void Parse_InvalidLeadingTopicAndMismatchedIdentity_DoNotPromoteContext()
    {
        AcademicWorkResearchContext topicConflict = OpenAlexResearchContextParser.Parse(Work("W1", """
            {"id":"W1","primary_topic":{"id":"T2"},"topics":[{}, {"id":"T2","score":0.7}]}
            """));
        Assert.Equal("Conflict", topicConflict.PrimaryTopicQuality);
        Assert.DoesNotContain(topicConflict.Topics, topic => topic.IsPrimary);

        AcademicWorkResearchContext hierarchyConflict = OpenAlexResearchContextParser.Parse(Work("W1", """
            {"id":"W1","primary_topic":{"id":"T1","subfield":{"id":1}},
             "topics":[{"id":"T1","subfield":{"id":2}}]}
            """));
        Assert.Equal("Conflict", hierarchyConflict.PrimaryTopicQuality);
        Assert.DoesNotContain(hierarchyConflict.Topics, topic => topic.IsPrimary);

        AcademicWorkResearchContext identityConflict = OpenAlexResearchContextParser.Parse(Work("W1", """
            {"id":"https://openalex.org/W2","fwci":4,"topics":[]}
            """));
        Assert.Equal("Conflict", identityConflict.ParseQuality);
        Assert.Null(identityConflict.Fwci);
    }

    [Fact]
    public void Parse_InvalidNormalization_NormalizesToNullWithQuality()
    {
        AcademicWorkResearchContext result = OpenAlexResearchContextParser.Parse(Work("W1", """
            {"id":"W1","fwci":-1,"citation_normalized_percentile":
              {"value":1.1,"is_in_top_1_percent":null,"is_in_top_10_percent":"bad"}}
            """));
        Assert.Null(result.Fwci);
        Assert.Null(result.CitationNormalizedPercentile);
        Assert.Null(result.IsInTopOnePercent);
        Assert.Null(result.IsInTopTenPercent);
        Assert.Contains("Invalid", result.ValueQualityJson);
    }

    private static AcademicWork Work(string providerWorkId, string payload) => new()
    {
        Id = 12,
        PersonelId = "person",
        Provider = AcademicWorkProvider.OpenAlex,
        ProviderWorkId = providerWorkId,
        ProviderPayload = payload,
        SyncedAt = new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc)
    };
}

using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.GraphProjection;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class AcademicGraphProjectionTests
{
    [Fact]
    public void Build_ValidEvidence_IsDeterministicPrivateFreeAndProjectionScoped()
    {
        AcademicGraphProjectionBundle first = Build();
        AcademicGraphProjectionBundle second = Build();

        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Equal(first.ProjectionId, second.ProjectionId);
        string json = System.Text.Json.JsonSerializer.Serialize(first);
        Assert.DoesNotContain("PersonelID", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("department", json, StringComparison.OrdinalIgnoreCase);
        Assert.All(first.RebuildCommands, command =>
            Assert.DoesNotContain("Researcher", command.Cypher, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(first.RebuildCommands, command => command.Cypher.Contains(
            "projectionId: $projectionId, id: row.id", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_EvidenceFromDifferentSource_RejectsAssociation()
    {
        Assert.Throws<InvalidDataException>(() => AcademicGraphProjectionService.Build(
            [new(1, "10.1000/test", false)],
            [new(10, 1, 20, "en", "policy", "prompt", false)],
            [new(20, 1, "hash", "Pdf", "v1"), new(21, 1, "other", "Pdf", "v1")],
            [new(30, 21, "s1", 0, 1, 0, 4, "text")],
            [new(40, 10, "findings", 0, "claim")],
            [new(50, 40, 30)]));
    }

    [Fact]
    public void QuarantineProposed_DoesNotAcceptEvidenceByIdMembership()
    {
        AcademicGraphProjectionBundle bundle = Build();
        AcademicGraphRelationshipDto proposed = AcademicGraphProjectionValidator.QuarantineProposed(
            new() { FromId = "claim:40", ToId = "work:1", Type = "REFUTES",
                EvidenceIds = ["span:30"] }, bundle);

        Assert.Equal("PROPOSED_RELATION", proposed.Type);
        Assert.Equal("PendingEvidenceValidation", proposed.Status);
    }

    [Fact]
    public void Evaluate_GraphAgainstSql_ReportsMissingAndUnexpectedEvidence()
    {
        GraphRetrievalEvaluationDto result = AcademicGraphProjectionService.Evaluate(
        [
            (new[] { "span:1", "span:2" }, (IReadOnlyCollection<string>)new[] { "span:1" }),
            (new[] { "span:3" }, (IReadOnlyCollection<string>)new[] { "span:3", "span:4" })
        ]);

        Assert.Equal(1, result.MissingFromGraphCount);
        Assert.Equal(1, result.UnexpectedGraphCount);
        Assert.Equal(0m, result.ExactSetAgreementProportion);
    }

    private static AcademicGraphProjectionBundle Build() => AcademicGraphProjectionService.Build(
        [new(1, "10.1000/test", false)],
        [new(10, 1, 20, "en", "policy", "prompt", false)],
        [new(20, 1, "hash", "Pdf", "v1")],
        [new(30, 20, "s1", 0, 1, 0, 4, "text")],
        [new(40, 10, "findings", 0, "claim")],
        [new(50, 40, 30)]);
}

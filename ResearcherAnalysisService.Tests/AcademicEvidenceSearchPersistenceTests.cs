using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.ArticleSummaries;
using ResearcherAnalysisService.Products.Data;
using ResearcherAnalysisService.Products.FacultyAssistant;
using ResearcherAnalysisService.Products.Knowledge;
using ResearcherAnalysisService.Products.ProductAccess;
using ResearcherAnalysisService.SourceData.Researchers;
using ResearcherAnalysisService.SourceData.Works;
using ResearcherAnalysisService.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ResearcherAnalysisService.Tests;

[Collection("Analysis Product SQL Server")]
public sealed class AcademicEvidenceSearchPersistenceTests(AnalysisProductSqlServerFixture fixture)
{
    [Fact]
    public async Task SearchAsync_LabelledFixture_PreservesIdentityOwnershipAndRetrievalDenominators()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        string personelId = "knowledge-" + Guid.NewGuid().ToString("N");
        Researcher researcher = new() { PersonelId = personelId };
        database.Researchers.Add(researcher);
        for (int index = 0; index < 2; index++)
        {
            CanonicalWork work = new()
            {
                NormalizedDoi = $"10.9100/{Guid.NewGuid():N}",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            database.CanonicalWorks.Add(work);
            database.CanonicalResearcherWorks.Add(new()
            {
                CanonicalWork = work,
                Researcher = researcher,
                PersonelId = personelId,
                LastObservedAt = DateTime.UtcNow
            });
            await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);
            SavedArticleSummary summary = new()
            {
                PersonelId = personelId,
                OriginalAcademicWorkId = index + 1,
                SavedAt = DateTimeOffset.UtcNow,
                SourceHash = new string((char)('a' + index), 64),
                SourceKind = "Pdf",
                ExtractionVersion = "test-v1",
                SnapshotJson = "{}",
                ReportJson = "{}"
            };
            ArticleSourceSnapshot source = new()
            {
                CanonicalWorkId = work.Id,
                ExtractedTextHash = new string((char)('c' + index), 64),
                SourceKind = "Pdf",
                ExtractionVersion = "test-v1",
                CreatedAt = DateTimeOffset.UtcNow,
                Spans = [new()
                {
                    SourceId = "p1-s1",
                    Ordinal = 0,
                    PageNumber = 1,
                    StartOffset = 0,
                    EndOffset = 36,
                    Text = "YÖNTEM ve identical evidence passage"
                }]
            };
            database.ArticleSummaries.Add(summary);
            database.ArticleSourceSnapshots.Add(source);
            database.CanonicalArticleAnalysisRuns.Add(new()
            {
                CanonicalWorkId = work.Id,
                ArticleSourceSnapshot = source,
                SavedArticleSummary = summary,
                AnalyzedAt = DateTimeOffset.UtcNow,
                SourceAcquiredAt = DateTimeOffset.UtcNow,
                SourceOrigin = "Test",
                Language = "en",
                PolicyVersion = "test-policy",
                Model = "synthetic",
                PromptVersion = "test-prompt",
                ExtractionMethod = "test",
                ProcessedChunks = 1,
                TotalChunks = 1,
                ProcessedPages = 1,
                TextBearingPages = 1,
                TotalPages = 1,
                OmissionReasonsJson = "[]",
                VerificationStatus = "synthetic",
                VerificationModel = "synthetic",
                VerificationPromptVersion = "synthetic"
            });
        }
        CanonicalWork unassociated = new()
        {
            NormalizedDoi = $"10.9100/{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Observations = []
        };
        database.CanonicalWorks.Add(unassociated);
        await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);
        ArticleSourceSnapshot unassociatedSource = new()
        {
            CanonicalWorkId = unassociated.Id,
            ExtractedTextHash = new string('z', 64),
            SourceKind = "Pdf",
            ExtractionVersion = "test-v1",
            CreatedAt = DateTimeOffset.UtcNow,
            Spans = [new() { SourceId = "p1-s1", Ordinal = 0, PageNumber = 1,
                StartOffset = 0, EndOffset = 36, Text = "YÖNTEM ve identical evidence passage" }]
        };
        SavedArticleSummary unassociatedSummary = new()
        {
            PersonelId = personelId, OriginalAcademicWorkId = 99, SavedAt = DateTimeOffset.UtcNow,
            SourceHash = new string('y', 64), SourceKind = "Pdf", ExtractionVersion = "test-v1",
            SnapshotJson = "{}", ReportJson = "{}"
        };
        database.ArticleSummaries.Add(unassociatedSummary);
        database.ArticleSourceSnapshots.Add(unassociatedSource);
        database.CanonicalArticleAnalysisRuns.Add(new()
        {
            CanonicalWorkId = unassociated.Id, ArticleSourceSnapshot = unassociatedSource,
            SavedArticleSummary = unassociatedSummary, AnalyzedAt = DateTimeOffset.UtcNow,
            SourceAcquiredAt = DateTimeOffset.UtcNow, SourceOrigin = "Test", Language = "en",
            PolicyVersion = "test-policy", Model = "synthetic", PromptVersion = "test-prompt",
            ExtractionMethod = "test", ProcessedChunks = 1, TotalChunks = 1, ProcessedPages = 1,
            TextBearingPages = 1, TotalPages = 1, OmissionReasonsJson = "[]",
            VerificationStatus = "synthetic", VerificationModel = "synthetic",
            VerificationPromptVersion = "synthetic"
        });
        CanonicalWork missingSource = new()
        {
            NormalizedDoi = $"10.9100/{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        database.CanonicalWorks.Add(missingSource);
        database.CanonicalResearcherWorks.Add(new()
        {
            CanonicalWork = missingSource,
            Researcher = researcher,
            PersonelId = personelId,
            LastObservedAt = DateTime.UtcNow
        });
        await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);

        AcademicEvidenceSearchService service = new(database,
            Options.Create(new ArticleSummaryAutomationOptions { PolicyVersion = "test-policy" }));
        AcademicEvidenceSearchResponse result = await service.SearchAsync(personelId,
            new() { Query = "Bu makaledeki identical evidence ders için nasıl açıklanabilir?", Take = 20 },
            CancellationToken.None);

        Assert.Equal(3, result.EligibleCanonicalWorkCount);
        Assert.Equal(2, result.CoveredCanonicalWorkCount);
        Assert.Equal(1, result.MissingSourceCanonicalWorkCount);
        Assert.Equal(2, result.Hits.Count);
        Assert.Equal(2, result.Hits.Select(value => value.EvidenceId)
            .Distinct(StringComparer.Ordinal).Count());
        Assert.All(result.Hits, value => Assert.Equal("p1-s1", value.SourceId));
        AcademicEvidenceSearchResponse turkish = await service.SearchAsync(personelId,
            new() { Query = "yöntem", Take = 20 }, CancellationToken.None);
        Assert.Equal(2, turkish.Hits.Count);

        AcademicEvidenceSearchResponse irrelevant = await service.SearchAsync(personelId,
            new() { Query = "term-that-does-not-exist", Take = 20 }, CancellationToken.None);
        string[] gold = await database.ArticleSourceSpans.AsNoTracking()
            .Where(value => value.Text == "YÖNTEM ve identical evidence passage" &&
                database.CanonicalResearcherWorks.Any(association =>
                    association.PersonelId == personelId &&
                    association.CanonicalWorkId == value.ArticleSourceSnapshot!.CanonicalWorkId))
            .OrderBy(value => value.Id)
            .Select(value => "work:" + value.ArticleSourceSnapshot!.CanonicalWorkId +
                ":snapshot:" + value.ArticleSourceSnapshotId + ":span:" + value.Id)
            .ToArrayAsync();
        var evaluation = AcademicEvidenceSearchService.Evaluate(
        [
            (result.Hits.Select(value => value.EvidenceId).ToArray(),
                (IReadOnlyCollection<string>)gold, true),
            (irrelevant.Hits.Select(value => value.EvidenceId).ToArray(),
                (IReadOnlyCollection<string>)Array.Empty<string>(), true),
            (Array.Empty<string>(), (IReadOnlyCollection<string>)Array.Empty<string>(), false)
        ], 20);
        Assert.Equal(1m, evaluation.RecallAtK);
        Assert.Equal(0.5m, evaluation.MeanReciprocalRank);
        Assert.Equal(1, evaluation.MissingSourceCaseCount);
    }

    [Fact]
    public async Task SearchAndFacultyEnqueue_VerifiedTurkishClaimsBridgeToExactEnglishSource()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        BridgeFixture seeded = await SeedBridgeFixtureAsync(database);
        AcademicEvidenceSearchService service = new(database,
            Options.Create(new ArticleSummaryAutomationOptions { PolicyVersion = "test-policy" }));

        AcademicEvidenceSearchResponse bridge = await service.SearchAsync(seeded.PersonelId,
            new() { Query = "ogrenme", CanonicalWorkIds = [seeded.WorkId], Take = 20 }, default);
        AcademicEvidenceSearchResponse repeat = await service.SearchAsync(seeded.PersonelId,
            new() { Query = "ogrenme", CanonicalWorkIds = [seeded.WorkId], Take = 20 }, default);

        AcademicEvidenceSearchHitDto bridged = Assert.Single(bridge.Hits);
        Assert.Equal(seeded.EnglishEvidence, bridged.ExactText);
        Assert.Equal(AcademicEvidenceSearchService.CatalogVersion, bridge.CatalogVersion);
        Assert.Equal(bridge.QueryHash, repeat.QueryHash);
        Assert.Equal(bridge.QueryPlanHash, repeat.QueryPlanHash);
        Assert.Equal(bridge.CorpusHash, repeat.CorpusHash);
        Assert.Equal(bridge.ClaimBridgeHash, repeat.ClaimBridgeHash);
        Assert.Equal(bridge.InputHash, repeat.InputHash);
        Assert.Equal(bridge.Hits.Select(value => value.EvidenceId),
            repeat.Hits.Select(value => value.EvidenceId));
        Assert.Equal(2, bridged.MatchProvenance!.Count(value =>
            value.Kind == "summary_claim" && value.AnalysisRunId == seeded.TurkishRunId &&
            value.Language == "tr"));
        Assert.DoesNotContain(bridged.MatchProvenance!, value => value.AnalysisRunId == seeded.OldRunId);
        Assert.Equal(2, bridge.ClaimBridgeEligibleRunCount);
        Assert.Equal(1, bridge.ClaimBridgeCoveredCanonicalWorkCount);
        Assert.Equal(["en", "tr"], bridge.ClaimBridgeLanguageCoverage!.Select(value => value.Value));
        Assert.False(bridge.IsClaimBridgeTruncated);

        AcademicEvidenceSearchResponse foldedSql = await service.SearchAsync(seeded.PersonelId,
            new() { Query = "fazladan terimler cig olcusu sogus",
                CanonicalWorkIds = [seeded.WorkId], Take = 20 }, default);
        Assert.Equal(seeded.EnglishEvidence, Assert.Single(foldedSql.Hits).ExactText);

        AcademicEvidenceSearchResponse section = await service.SearchAsync(seeded.PersonelId,
            new() { Query = "yöntemsel yaklaşımı dersimde açıklamak",
                CanonicalWorkIds = [seeded.WorkId], Take = 20 }, default);
        Assert.Contains(section.Hits, hit => hit.ArticleSourceSpanId == seeded.EnglishSpanId &&
            hit.MatchProvenance!.Any(value => value.Kind == "summary_section" &&
                value.AnalysisRunId == seeded.TurkishRunId && value.Section == "methods"));

        AcademicEvidenceSearchResponse direct = await service.SearchAsync(seeded.PersonelId,
            new() { Query = "direct exact phrase", CanonicalWorkIds = [seeded.WorkId], Take = 20 }, default);
        Assert.Equal(seeded.DirectEnglishEvidence, direct.Hits[0].ExactText);
        Assert.Contains(direct.Hits[0].MatchProvenance!, value => value.Kind == "source_text");

        foreach (string excluded in new[]
            { "eskimisbenzersiz", "dogrulanmamisbenzersiz", "baskasahipbenzersiz", "yanlisnapshotbenzersiz" })
        {
            AcademicEvidenceSearchResponse result = await service.SearchAsync(seeded.PersonelId,
                new() { Query = excluded, CanonicalWorkIds = [seeded.WorkId], Take = 20 }, default);
            Assert.Empty(result.Hits);
        }
        Assert.Empty((await service.SearchAsync(seeded.PersonelId,
            new() { Query = "verification", CanonicalWorkIds = [seeded.WorkId], Take = 20 }, default)).Hits);
        Assert.Empty((await service.SearchAsync(seeded.PersonelId,
            new() { Query = "tamamen ilgisiz", CanonicalWorkIds = [seeded.WorkId], Take = 20 }, default)).Hits);
        Assert.Empty((await service.SearchAsync(seeded.PersonelId,
            new() { Query = "Other owner evidence", Take = 20 }, default)).Hits);

        AcademicProductAccessGrant grant = new("grant", "actor", seeded.PersonelId,
            AcademicProductOperation.FacultyAssistantStart);
        FacultyAssistantScheduler scheduler = new(database, service,
            Options.Create(new FacultyAssistantOptions()),
            Options.Create(new ArticleSummaryAutomationOptions { PolicyVersion = "test-policy" }));
        StartFacultyAssistantRequest request = new()
        {
            PersonelId = seeded.PersonelId,
            ClientRequestId = Guid.NewGuid(),
            Mode = "TeachingHelp",
            Language = "tr",
            Query = "ogrenme",
            CanonicalWorkIds = [seeded.WorkId],
            Take = 5
        };
        FacultyAssistantRunResponse queued = await scheduler.EnqueueAsync(grant, request, default);
        FacultyAssistantRun stored = await database.FacultyAssistantRuns.AsNoTracking()
            .SingleAsync(value => value.RunId == queued.RunId);
        FacultyAssistantAnalysisRequest authorized = JsonSerializer.Deserialize<FacultyAssistantAnalysisRequest>(
            stored.AuthorizedInputJson!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(seeded.EnglishEvidence, Assert.Single(authorized.Evidence).ExactText);
        Assert.DoesNotContain("CLAIM_PARAPHRASE_SENTINEL", stored.AuthorizedInputJson!,
            StringComparison.Ordinal);
        Assert.Equal(seeded.TurkishRunId, Assert.Single(queued.Retrieval.Evidence)
            .MatchProvenance!.First(value => value.Kind == "summary_claim").AnalysisRunId);
        FacultyAssistantRunResponse replay = await scheduler.EnqueueAsync(grant, request, default);
        Assert.True(replay.Reused);
        Assert.Equal(queued.InputFingerprint, replay.InputFingerprint);
        Assert.Equal(queued.Retrieval.InputHash, replay.Retrieval.InputHash);
        Assert.Equal(queued.Retrieval.ClaimBridgeHash, replay.Retrieval.ClaimBridgeHash);
    }

    [Fact]
    public async Task SearchAsync_ClaimCandidateLimit_IsDeterministicAndReported()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        BridgeFixture seeded = await SeedBridgeFixtureAsync(database);
        CanonicalArticleAnalysisRun run = await database.CanonicalArticleAnalysisRuns
            .SingleAsync(value => value.Id == seeded.TurkishRunId);
        run.Claims.AddRange(Enumerable.Range(100, 1501).Select(index => new CanonicalArticleClaim
        {
            Section = "purpose", SectionOrder = 9, Ordinal = index,
            Text = $"sinirkelimesi {index}"
        }));
        await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);
        AcademicEvidenceSearchService service = new(database);

        AcademicEvidenceSearchResponse result = await service.SearchAsync(seeded.PersonelId,
            new() { Query = "sinirkelimesi", CanonicalWorkIds = [seeded.WorkId], Take = 20 }, default);
        AcademicEvidenceSearchResponse repeat = await service.SearchAsync(seeded.PersonelId,
            new() { Query = "sinirkelimesi", CanonicalWorkIds = [seeded.WorkId], Take = 20 }, default);

        Assert.Equal(1500, result.ClaimBridgeCandidateClaimCount);
        Assert.Equal(0, result.ClaimBridgeCandidateSpanCount);
        Assert.True(result.IsClaimBridgeTruncated);
        Assert.True(result.IsCorpusTruncated);
        Assert.False(result.IsDirectCorpusTruncated);
        Assert.Empty(result.Hits);
        Assert.Equal(result.InputHash, repeat.InputHash);
        Assert.Equal(result.ClaimBridgeHash, repeat.ClaimBridgeHash);
    }

    [Fact]
    public async Task SearchAsync_BilingualMultiIntentQuery_ReservesEnglishMethodScopeConditionAndFindingSpans()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        string personelId = "intent-" + Guid.NewGuid().ToString("N");
        Researcher researcher = new() { PersonelId = personelId };
        Researcher otherResearcher = new() { PersonelId = "other-" + Guid.NewGuid().ToString("N") };
        CanonicalWork adam = NewWork();
        CanonicalWork football = NewWork();
        CanonicalWork otherOwnerWork = NewWork();
        database.AddRange(researcher, otherResearcher, adam, football, otherOwnerWork);
        await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);
        database.CanonicalResearcherWorks.AddRange(
            new() { PersonelId = personelId, CanonicalWorkId = adam.Id, LastObservedAt = DateTime.UtcNow },
            new() { PersonelId = personelId, CanonicalWorkId = football.Id, LastObservedAt = DateTime.UtcNow });

        ArticleSourceSnapshot adamSource = NewSource(adam.Id,
            "The method applies online optimization with adaptive first-order moments.",
            "The method introduction repeats the optimization overview.",
            "The method introduction again describes the optimization overview.",
            "Empirically, the method is robust for a wide range of non-convex optimization problems.",
            "A limitation is that this convergence analysis does not apply to non-convex objectives.",
            "The theorem assumes bounded gradients, bounded distance between iterates, and gamma t equal to one over t.",
            "Under these conditions, the result provides a regret bound for Adam.");
        ArticleSourceSnapshot footballSource = NewSource(football.Id,
            "The football method reconstructs trajectories with graph imputation.",
            "The football results show improved performance against the comparison baseline.");
        ArticleSourceSnapshot otherSource = NewSource(otherOwnerWork.Id,
            "A limitation and bounded assumption belong only to another researcher.");
        database.ArticleSourceSnapshots.AddRange(adamSource, footballSource, otherSource);
        await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);

        CanonicalArticleAnalysisRun adamRun = NewRun(adam.Id, adamSource.Id,
            NewSummary(personelId, 101), "tr");
        adamRun.Claims.AddRange(
            IntroClaim("Adam çevrimiçi optimizasyon yaklaşımıdır.", 0, adamSource.Spans[0].Id),
            IntroClaim("Yöntem uyarlamalı momentler kullanır.", 1, adamSource.Spans[1].Id));
        CanonicalArticleAnalysisRun footballRun = NewRun(football.Id, footballSource.Id,
            NewSummary(personelId, 102), "tr");
        footballRun.Claims.Add(IntroClaim("Futbol yöntemi grafik tabanlıdır.", 0,
            footballSource.Spans[0].Id));
        CanonicalArticleAnalysisRun otherRun = NewRun(otherOwnerWork.Id, otherSource.Id,
            NewSummary(otherResearcher.PersonelId, 103), "en");
        database.CanonicalArticleAnalysisRuns.AddRange(adamRun, footballRun, otherRun);
        await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);

        AcademicEvidenceSearchService service = new(database,
            Options.Create(new ArticleSummaryAutomationOptions { PolicyVersion = "test-policy" }));
        AcademicEvidenceSearchRequest methodsRequest = new()
        {
            Query = "Bu makalenin yöntem ve sınırlılıklarını kaynaklarıyla açıkla; " +
                "kendi çalışmamda hangi koşulları kontrol etmeliyim?",
            CanonicalWorkIds = [adam.Id],
            Take = 4
        };
        AcademicEvidenceSearchResponse methods = await service.SearchAsync(personelId,
            methodsRequest, default);
        AcademicEvidenceSearchResponse repeat = await service.SearchAsync(personelId,
            methodsRequest, default);

        Assert.Equal(4, methods.Hits.Count);
        Assert.Contains(methods.Hits, hit => hit.ExactText.StartsWith("The method",
            StringComparison.Ordinal));
        Assert.Contains(methods.Hits, hit => hit.ExactText.StartsWith("A limitation",
            StringComparison.Ordinal));
        Assert.Contains(methods.Hits, hit => hit.ExactText.StartsWith("The theorem assumes",
            StringComparison.Ordinal));
        Assert.Contains(methods.Hits, hit => hit.ExactText.StartsWith("Under these conditions",
            StringComparison.Ordinal));
        Assert.All(methods.Hits, hit => Assert.Equal(adam.Id, hit.CanonicalWorkId));
        Assert.Contains(methods.Hits.SelectMany(hit => hit.MatchProvenance!), match =>
            match.Kind == "source_text_intent" && match.Section == "methods");
        Assert.Contains(methods.Hits.SelectMany(hit => hit.MatchProvenance!), match =>
            match.Kind == "source_text_intent" && match.Section == "limitations");
        Assert.Contains(methods.Hits.SelectMany(hit => hit.MatchProvenance!), match =>
            match.Kind == "source_text_intent" && match.Section == "conditions");
        Assert.Contains(methods.Hits.SelectMany(hit => hit.MatchProvenance!), match =>
            match.Kind == "source_text_intent" && match.Section == "findings");
        Assert.Equal(methods.QueryHash, repeat.QueryHash);
        Assert.Equal(methods.QueryPlanHash, repeat.QueryPlanHash);
        Assert.Equal(methods.InputHash, repeat.InputHash);
        Assert.Equal(methods.Hits.Select(hit => hit.EvidenceId), repeat.Hits.Select(hit => hit.EvidenceId));

        AcademicEvidenceSearchResponse teaching = await service.SearchAsync(personelId, new()
        {
            Query = "football yöntemini ve bulgularını karşılaştır",
            CanonicalWorkIds = [football.Id],
            Take = 2
        }, default);
        Assert.Equal(2, teaching.Hits.Count);
        Assert.Contains(teaching.Hits, hit => hit.ExactText.Contains("football method",
            StringComparison.Ordinal));
        Assert.Contains(teaching.Hits, hit => hit.ExactText.Contains("football results",
            StringComparison.Ordinal));

        static CanonicalWork NewWork() => new()
        {
            NormalizedDoi = "10.9300/" + Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        static ArticleSourceSnapshot NewSource(int workId, params string[] texts) => new()
        {
            CanonicalWorkId = workId,
            ExtractedTextHash = Guid.NewGuid().ToString("N").PadRight(64, 'f'),
            SourceKind = "Pdf",
            ExtractionVersion = "test-v1",
            CreatedAt = DateTimeOffset.UtcNow,
            Spans = texts.Select((text, index) => new ArticleSourceSpanSnapshot
            {
                SourceId = $"intent-p1-s{index + 1}",
                Ordinal = index,
                PageNumber = 1,
                StartOffset = index * 1000,
                EndOffset = index * 1000 + text.Length,
                Text = text
            }).ToList()
        };
        static SavedArticleSummary NewSummary(string ownerId, int originalId) => new()
        {
            PersonelId = ownerId,
            OriginalAcademicWorkId = originalId,
            SavedAt = DateTimeOffset.UtcNow,
            SourceHash = Guid.NewGuid().ToString("N").PadRight(64, 's'),
            SourceKind = "Pdf",
            ExtractionVersion = "test-v1",
            SnapshotJson = "{}",
            ReportJson = "{}"
        };
        static CanonicalArticleAnalysisRun NewRun(int workId, long snapshotId,
            SavedArticleSummary summary, string language) => new()
        {
            CanonicalWorkId = workId,
            ArticleSourceSnapshotId = snapshotId,
            SavedArticleSummary = summary,
            AnalyzedAt = DateTimeOffset.UtcNow,
            SourceAcquiredAt = DateTimeOffset.UtcNow,
            SourceOrigin = "Test",
            Language = language,
            PolicyVersion = "test-policy",
            Model = "synthetic",
            PromptVersion = "test-prompt",
            ExtractionMethod = "test",
            ProcessedChunks = 1,
            TotalChunks = 1,
            ProcessedPages = 1,
            TextBearingPages = 1,
            TotalPages = 1,
            OmissionReasonsJson = "[]",
            VerificationStatus = "automatically_checked",
            VerificationModel = "synthetic",
            VerificationPromptVersion = "synthetic"
        };
        static CanonicalArticleClaim IntroClaim(string text, int ordinal, long spanId) => new()
        {
            Section = "methods",
            SectionOrder = 1,
            Ordinal = ordinal,
            Text = text,
            Evidence = [new() { ArticleSourceSpanId = spanId, Ordinal = 0 }]
        };
    }

    [Fact]
    public async Task SearchAsync_SelectedWorks_ReservesVerifiedMethodEvidencePerWorkBeforeIncidentalText()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        string personelId = "multiwork-" + Guid.NewGuid().ToString("N");
        Researcher owner = new() { PersonelId = personelId };
        Researcher otherOwner = new() { PersonelId = "other-" + Guid.NewGuid().ToString("N") };
        CanonicalWork fieldStudy = Work();
        CanonicalWork replication = Work();
        CanonicalWork foreign = Work();
        database.AddRange(owner, otherOwner, fieldStudy, replication, foreign);
        await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);
        database.CanonicalResearcherWorks.AddRange(
            new() { PersonelId = personelId, CanonicalWorkId = fieldStudy.Id, LastObservedAt = DateTime.UtcNow },
            new() { PersonelId = personelId, CanonicalWorkId = replication.Id, LastObservedAt = DateTime.UtcNow },
            new() { PersonelId = otherOwner.PersonelId, CanonicalWorkId = foreign.Id, LastObservedAt = DateTime.UtcNow });

        const string fieldMethod =
            "Parents were observed for four weeks; six centers then received a fine and four remained controls.";
        const string incidental = "References: An Approach to the economic analysis of sanctions.";
        const string exact = "The exact method priority marker is retained in this source.";
        const string replicationMethod =
            "The replication method sampled studies from three journals and compared several success measures.";
        ArticleSourceSnapshot staleSource = Source(fieldStudy.Id,
            "The stale-only method used an obsolete source snapshot.");
        ArticleSourceSnapshot fieldSource = Source(fieldStudy.Id, fieldMethod, incidental, exact);
        ArticleSourceSnapshot replicationSource = Source(replication.Id, replicationMethod);
        ArticleSourceSnapshot foreignSource = Source(foreign.Id,
            "The unrelated owner method must never be returned.");
        database.ArticleSourceSnapshots.AddRange(staleSource, fieldSource, replicationSource, foreignSource);
        await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);

        CanonicalArticleAnalysisRun stale = Run(fieldStudy.Id, staleSource.Id,
            Summary(personelId, 201), "tr");
        stale.Claims.Add(MethodsClaim("Eski yöntem kaydı.", staleSource.Spans[0].Id));
        database.CanonicalArticleAnalysisRuns.Add(stale);
        await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);
        CanonicalArticleAnalysisRun fieldRun = Run(fieldStudy.Id, fieldSource.Id,
            Summary(personelId, 202), "tr");
        fieldRun.Claims.Add(MethodsClaim(
            "Saha deneyi gözlem dönemi ile müdahale ve kontrol merkezlerini karşılaştırır.",
            fieldSource.Spans[0].Id));
        CanonicalArticleAnalysisRun replicationRun = Run(replication.Id, replicationSource.Id,
            Summary(personelId, 203), "tr");
        replicationRun.Claims.Add(MethodsClaim(
            "Tekrarlama yöntemi üç dergiden seçilen çalışmaları birden çok ölçütle değerlendirir.",
            replicationSource.Spans[0].Id));
        CanonicalArticleAnalysisRun foreignRun = Run(foreign.Id, foreignSource.Id,
            Summary(otherOwner.PersonelId, 204), "tr");
        foreignRun.Claims.Add(MethodsClaim("Yabancı yöntem kaydı.", foreignSource.Spans[0].Id));
        database.CanonicalArticleAnalysisRuns.AddRange(fieldRun, replicationRun, foreignRun);
        await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);

        AcademicEvidenceSearchService service = new(database,
            Options.Create(new ArticleSummaryAutomationOptions { PolicyVersion = "test-policy" }));
        AcademicEvidenceSearchResponse result = await service.SearchAsync(personelId, new()
        {
            Query = "Bu iki çalışmanın yöntem farkını kaynaklarıyla özetle.",
            CanonicalWorkIds = [fieldStudy.Id, replication.Id],
            Take = 2
        }, default);

        Assert.Equal("academic-evidence-search-v4", result.CatalogVersion);
        Assert.Equal([fieldStudy.Id, replication.Id], result.Hits.Select(hit => hit.CanonicalWorkId).Order());
        Assert.Contains(result.Hits, hit => hit.ExactText == fieldMethod && hit.MatchProvenance!.Any(
            match => match.Kind == "summary_section" && match.Section == "methods"));
        Assert.Contains(result.Hits, hit => hit.ExactText == replicationMethod && hit.MatchProvenance!.Any(
            match => match.Kind == "summary_section" && match.Section == "methods"));
        Assert.DoesNotContain(result.Hits, hit => hit.ExactText == incidental);

        AcademicEvidenceSearchResponse exactResult = await service.SearchAsync(personelId, new()
        {
            Query = "exact method priority marker",
            CanonicalWorkIds = [fieldStudy.Id, replication.Id],
            Take = 2
        }, default);
        Assert.Equal(exact, exactResult.Hits[0].ExactText);
        Assert.Equal([fieldStudy.Id, replication.Id], exactResult.Hits
            .Select(hit => hit.CanonicalWorkId).Order());
        Assert.Empty((await service.SearchAsync(personelId, new()
        {
            Query = "stale-only", CanonicalWorkIds = [fieldStudy.Id], Take = 5
        }, default)).Hits);
        Assert.Empty((await service.SearchAsync(personelId, new()
        {
            Query = "unrelated owner", Take = 5
        }, default)).Hits);

        static CanonicalWork Work() => new()
        {
            NormalizedDoi = "10.9400/" + Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        static ArticleSourceSnapshot Source(int workId, params string[] texts) => new()
        {
            CanonicalWorkId = workId,
            ExtractedTextHash = Guid.NewGuid().ToString("N").PadRight(64, 'e'),
            SourceKind = "Pdf",
            ExtractionVersion = "test-v1",
            CreatedAt = DateTimeOffset.UtcNow,
            Spans = texts.Select((text, index) => new ArticleSourceSpanSnapshot
            {
                SourceId = $"multi-p1-s{index + 1}",
                Ordinal = index,
                PageNumber = 1,
                StartOffset = index * 1000,
                EndOffset = index * 1000 + text.Length,
                Text = text
            }).ToList()
        };
        static SavedArticleSummary Summary(string ownerId, int originalId) => new()
        {
            PersonelId = ownerId,
            OriginalAcademicWorkId = originalId,
            SavedAt = DateTimeOffset.UtcNow,
            SourceHash = Guid.NewGuid().ToString("N").PadRight(64, 'h'),
            SourceKind = "Pdf",
            ExtractionVersion = "test-v1",
            SnapshotJson = "{}",
            ReportJson = "{}"
        };
        static CanonicalArticleAnalysisRun Run(int workId, long snapshotId,
            SavedArticleSummary summary, string language) => new()
        {
            CanonicalWorkId = workId,
            ArticleSourceSnapshotId = snapshotId,
            SavedArticleSummary = summary,
            AnalyzedAt = DateTimeOffset.UtcNow,
            SourceAcquiredAt = DateTimeOffset.UtcNow,
            SourceOrigin = "Test",
            Language = language,
            PolicyVersion = "test-policy",
            Model = "synthetic",
            PromptVersion = "test-prompt",
            ExtractionMethod = "test",
            ProcessedChunks = 1,
            TotalChunks = 1,
            ProcessedPages = 1,
            TextBearingPages = 1,
            TotalPages = 1,
            OmissionReasonsJson = "[]",
            VerificationStatus = "automatically_checked",
            VerificationModel = "synthetic",
            VerificationPromptVersion = "synthetic"
        };
        static CanonicalArticleClaim MethodsClaim(string text, long spanId) => new()
        {
            Section = "methods",
            SectionOrder = 1,
            Ordinal = 0,
            Text = text,
            Evidence = [new() { ArticleSourceSpanId = spanId, Ordinal = 0 }]
        };
    }

    private async Task<BridgeFixture> SeedBridgeFixtureAsync(AnalysisDbContext database)
    {
        string personelId = "bridge-" + Guid.NewGuid().ToString("N");
        Researcher owner = new() { PersonelId = personelId };
        Researcher otherOwner = new() { PersonelId = "other-" + Guid.NewGuid().ToString("N") };
        CanonicalWork work = Work();
        CanonicalWork otherWork = Work();
        CanonicalWork malformedWork = Work();
        database.AddRange(owner, otherOwner, work, otherWork, malformedWork);
        await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);
        database.CanonicalResearcherWorks.AddRange(
            new() { PersonelId = personelId, CanonicalWorkId = work.Id, LastObservedAt = DateTime.UtcNow },
            new() { PersonelId = personelId, CanonicalWorkId = malformedWork.Id, LastObservedAt = DateTime.UtcNow },
            new() { PersonelId = otherOwner.PersonelId, CanonicalWorkId = otherWork.Id, LastObservedAt = DateTime.UtcNow });
        database.AcademicWorks.AddRange(
            SourceWork(personelId, work),
            SourceWork(personelId, malformedWork),
            SourceWork(otherOwner.PersonelId, otherWork));
        await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);
        IReadOnlyDictionary<int, string> sourceIdentities = await CanonicalSourceIdentity.LoadAsync(
            database, [work.Id, malformedWork.Id, otherWork.Id], default);

        string englishEvidence = "The cohort followed a preregistered longitudinal protocol.";
        string directEvidence = "The direct exact phrase appears in this English source.";
        ArticleSourceSnapshot source = Source(work.Id, englishEvidence, directEvidence);
        ArticleSourceSnapshot sameWorkOtherSnapshot = Source(work.Id, "Wrong snapshot evidence.");
        ArticleSourceSnapshot otherSource = Source(otherWork.Id, "Other owner evidence.");
        database.ArticleSourceSnapshots.AddRange(source, sameWorkOtherSnapshot, otherSource);
        await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);

        CanonicalArticleAnalysisRun old = Run(work.Id, source.Id, Summary(personelId, 1), "tr",
            "automatically_checked", sourceIdentities[work.Id]);
        old.Claims.Add(new()
        {
            Section = "methods", SectionOrder = 1, Ordinal = 0, Text = "eskimisbenzersiz",
            Evidence = [new() { ArticleSourceSpanId = source.Spans[0].Id, Ordinal = 0 }]
        });
        database.CanonicalArticleAnalysisRuns.Add(old);
        await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);

        CanonicalArticleAnalysisRun english = Run(work.Id, source.Id, Summary(personelId, 2), "en",
            "automatically_checked", sourceIdentities[work.Id]);
        english.Claims.Add(new()
        {
            Section = "purpose", SectionOrder = 0, Ordinal = 0, Text = "Unrelated retained claim.",
            Evidence = [new() { ArticleSourceSpanId = source.Spans[0].Id, Ordinal = 0 }]
        });
        CanonicalArticleAnalysisRun turkish = Run(work.Id, source.Id, Summary(personelId, 3), "tr",
            "automatically_checked", sourceIdentities[work.Id]);
        turkish.Claims.AddRange(
            Claim("Öğrenme düzeni iki aşamada uygulanır. CLAIM_PARAPHRASE_SENTINEL", 0, source.Spans[0].Id),
            Claim("Öğrenme için yinelenen ipucu.", 1, source.Spans[0].Id),
            Claim("Katılımcılar iki gruba ayrıldı.", 2, source.Spans[0].Id),
            Claim("direct exact phrase summary hint", 3, source.Spans[0].Id),
            new() { Section = "methods", SectionOrder = 1, Ordinal = 4, Text = "kanıtsız öğrenme" },
            Claim("yanlisnapshotbenzersiz", 5, sameWorkOtherSnapshot.Spans[0].Id),
            Claim("baskasahipbenzersiz", 6, otherSource.Spans[0].Id),
            Claim("Çığ ölçüsü şöğüş örüntüsüdür.", 7, source.Spans[0].Id));
        CanonicalArticleAnalysisRun unverified = Run(work.Id, source.Id, Summary(personelId, 4), "de", "not_run");
        unverified.SourceIdentityHash = sourceIdentities[work.Id];
        unverified.Claims.Add(Claim("dogrulanmamisbenzersiz", 0, source.Spans[0].Id));
        CanonicalArticleAnalysisRun malformed = Run(malformedWork.Id, otherSource.Id,
            Summary(personelId, 5), "en", "automatically_checked",
            sourceIdentities[malformedWork.Id]);
        database.CanonicalArticleAnalysisRuns.AddRange(english, turkish, unverified, malformed);
        await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);
        return new(personelId, work.Id, source.Spans[0].Id, englishEvidence, directEvidence,
            old.Id, turkish.Id);

        static CanonicalWork Work() => new()
        {
            NormalizedDoi = "10.9200/" + Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        static AcademicWork SourceWork(string ownerId, CanonicalWork work) => new()
        {
            PersonelId = ownerId,
            Provider = AcademicWorkProvider.OpenAlex,
            ProviderWorkId = "https://openalex.org/W" + Guid.NewGuid().ToString("N"),
            Title = "Synthetic bridge article",
            Doi = work.NormalizedDoi,
            SyncedAt = DateTime.UtcNow,
            CanonicalObservation = new()
            {
                CanonicalWorkId = work.Id,
                PersonelId = ownerId,
                Provider = AcademicWorkProvider.OpenAlex,
                DoiObserved = work.NormalizedDoi,
                ObservedAt = DateTime.UtcNow
            }
        };
        static ArticleSourceSnapshot Source(int workId, params string[] texts) => new()
        {
            CanonicalWorkId = workId, ExtractedTextHash = Guid.NewGuid().ToString("N").PadRight(64, 'a'),
            SourceKind = "Pdf", ExtractionVersion = "test-v1", CreatedAt = DateTimeOffset.UtcNow,
            Spans = texts.Select((text, index) => new ArticleSourceSpanSnapshot
            {
                SourceId = $"p1-s{index + 1}", Ordinal = index, PageNumber = 1,
                StartOffset = index * 100, EndOffset = index * 100 + text.Length, Text = text
            }).ToList()
        };
        static SavedArticleSummary Summary(string ownerId, int originalId) => new()
        {
            PersonelId = ownerId, OriginalAcademicWorkId = originalId, SavedAt = DateTimeOffset.UtcNow,
            SourceHash = new string((char)('a' + originalId), 64), SourceKind = "Pdf",
            ExtractionVersion = "test-v1", SnapshotJson = "{}", ReportJson = "{}"
        };
        static CanonicalArticleAnalysisRun Run(int workId, long snapshotId,
            SavedArticleSummary summary, string language, string verificationStatus,
            string? sourceIdentityHash = null) => new()
        {
            CanonicalWorkId = workId, ArticleSourceSnapshotId = snapshotId, SavedArticleSummary = summary,
            AnalyzedAt = DateTimeOffset.UtcNow, SourceAcquiredAt = DateTimeOffset.UtcNow,
            SourceOrigin = "Test", Language = language, PolicyVersion = "test-policy", Model = "synthetic",
            SourceIdentityHash = sourceIdentityHash!,
            PromptVersion = "test-prompt", ExtractionMethod = "test", ProcessedChunks = 1,
            TotalChunks = 1, ProcessedPages = 1, TextBearingPages = 1, TotalPages = 1,
            OmissionReasonsJson = "[]", VerificationStatus = verificationStatus,
            VerificationModel = "synthetic", VerificationPromptVersion = "synthetic"
        };
        static CanonicalArticleClaim Claim(string text, int ordinal, long spanId) => new()
        {
            Section = "methods", SectionOrder = 1, Ordinal = ordinal, Text = text,
            Evidence = [new() { ArticleSourceSpanId = spanId, Ordinal = 0 }]
        };
    }

    private sealed record BridgeFixture(string PersonelId, int WorkId, long EnglishSpanId,
        string EnglishEvidence, string DirectEnglishEvidence, long OldRunId, long TurkishRunId);
}

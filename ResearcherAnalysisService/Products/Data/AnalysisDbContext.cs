using Microsoft.EntityFrameworkCore;
using ResearcherAnalysisService.SourceData.Integrations.Orcid;
using ResearcherAnalysisService.SourceData.Integrations.GoogleScholar;
using ResearcherAnalysisService.SourceData.Integrations.OpenAlex;
using ResearcherAnalysisService.SourceData.Integrations.WebOfScience;
using ResearcherAnalysisService.SourceData.Integrations.Yoksis.Persistence;
using ResearcherAnalysisService.SourceData.Integrations.TrDizin;
using ResearcherAnalysisService.SourceData.Integrations.Crossref;
using ResearcherAnalysisService.SourceData.Integrations.SemanticScholar;
using ResearcherAnalysisService.SourceData.Researchers;
using ResearcherAnalysisService.SourceData.Works;
using ResearcherAnalysisService.Products.Metrics;
using ResearcherAnalysisService.Products.ArticleReviews;
using ResearcherAnalysisService.Products.Evaluations;
using ResearcherAnalysisService.Products.HrDossiers;
using ResearcherAnalysisService.Products.FacultyAssistant;
using ResearcherAnalysisService.SourceData;

namespace ResearcherAnalysisService.Products.Data;

public sealed class AnalysisDbContext : DbContext
{
    public DbSet<CollectionChange> CollectionChanges { get; set; } = null!;
    public DbSet<CollectionChangeReceipt> CollectionChangeReceipts { get; set; } = null!;
    public DbSet<ArticleSummaries.SavedArticleSummary> ArticleSummaries { get; set; } = null!;
    public DbSet<ArticleSummaries.ArticleSourceSnapshot> ArticleSourceSnapshots { get; set; } = null!;
    public DbSet<ArticleSummaries.ArticleSourcePageSnapshot> ArticleSourcePages { get; set; } = null!;
    public DbSet<ArticleSummaries.ArticleSourceSpanSnapshot> ArticleSourceSpans { get; set; } = null!;
    public DbSet<ArticleSummaries.CanonicalArticleAnalysisRun> CanonicalArticleAnalysisRuns { get; set; } = null!;
    public DbSet<ArticleSummaries.CanonicalArticleClaim> CanonicalArticleClaims { get; set; } = null!;
    public DbSet<ArticleSummaries.CanonicalArticleClaimEvidence> CanonicalArticleClaimEvidence { get; set; } = null!;
    public DbSet<ArticleSummaries.ArticleSummaryAutomationJob> ArticleSummaryAutomationJobs { get; set; } = null!;
    public DbSet<CanonicalArticleReviewRun> CanonicalArticleReviewRuns { get; set; } = null!;
    public DbSet<CanonicalArticleReviewFinding> CanonicalArticleReviewFindings { get; set; } = null!;
    public DbSet<CanonicalArticleReviewEvidence> CanonicalArticleReviewEvidence { get; set; } = null!;
    public DbSet<ArticleReviewWorkItem> ArticleReviewWorkItems { get; set; } = null!;
    public DbSet<ArticleReviewStageCheckpoint> ArticleReviewStageCheckpoints { get; set; } = null!;
    public DbSet<ArticleEvaluationRun> ArticleEvaluationRuns { get; set; } = null!;
    public DbSet<ArticleEvaluationCase> ArticleEvaluationCases { get; set; } = null!;
    public DbSet<ArticleEvaluationWorkItem> ArticleEvaluationWorkItems { get; set; } = null!;
    public DbSet<ArticleEvaluationAttempt> ArticleEvaluationAttempts { get; set; } = null!;
    public DbSet<ArticleEvaluationResult> ArticleEvaluationResults { get; set; } = null!;
    public DbSet<PublicationMetricSnapshot> PublicationMetricSnapshots { get; set; } = null!;
    public DbSet<PublicationMetricProviderSnapshot> PublicationMetricProviderSnapshots { get; set; } = null!;
    public DbSet<PublicationMetricsRefreshState> PublicationMetricsRefreshStates { get; set; } = null!;
    public DbSet<ReferencePopulationManifest> ReferencePopulationManifests { get; set; } = null!;
    public DbSet<ReferencePopulationMember> ReferencePopulationMembers { get; set; } = null!;
    public DbSet<Analysis.SavedResearcherAnalysis> ResearcherAnalyses { get; set; } = null!;
    public DbSet<Researcher> Researchers { get; set; } = null!;
    public DbSet<OrcidProfile> OrcidProfiles { get; set; } = null!;
    public DbSet<OrcidWork> OrcidWorks { get; set; } = null!;
    public DbSet<GoogleScholarProfile> GoogleScholarProfiles { get; set; } = null!;
    public DbSet<GoogleScholarWork> GoogleScholarWorks { get; set; } = null!;
    public DbSet<OpenAlexProfile> OpenAlexProfiles { get; set; } = null!;
    public DbSet<OpenAlexWork> OpenAlexWorks { get; set; } = null!;
    public DbSet<WebOfScienceProfile> WebOfScienceProfiles { get; set; } = null!;
    public DbSet<WebOfScienceWork> WebOfScienceWorks { get; set; } = null!;
    public DbSet<WebOfSciencePeerReview> WebOfSciencePeerReviews { get; set; } = null!;
    public DbSet<YoksisRecord> YoksisRecords { get; set; } = null!;
    public DbSet<TrDizinProfile> TrDizinProfiles { get; set; } = null!;
    public DbSet<TrDizinWork> TrDizinWorks { get; set; } = null!;
    public DbSet<CrossrefWork> CrossrefWorks { get; set; } = null!;
    public DbSet<SemanticScholarPaper> SemanticScholarPapers { get; set; } = null!;
    public DbSet<SemanticScholarCitation> SemanticScholarCitations { get; set; } = null!;
    public DbSet<SemanticScholarCitationContext> SemanticScholarCitationContexts { get; set; } = null!;
    public DbSet<AcademicWork> AcademicWorks { get; set; } = null!;
    public DbSet<AcademicWorkSource> AcademicWorkSources { get; set; } = null!;
    public DbSet<AcademicWorkResearchContext> AcademicWorkResearchContexts { get; set; } = null!;
    public DbSet<AcademicWorkTopic> AcademicWorkTopics { get; set; } = null!;
    public DbSet<PublicationSummary> PublicationSummaries { get; set; } = null!;
    public DbSet<PublicationDisplayApproval> PublicationDisplayApprovals { get; set; } = null!;
    public DbSet<CanonicalWork> CanonicalWorks { get; set; } = null!;
    public DbSet<CanonicalWorkObservation> CanonicalWorkObservations { get; set; } = null!;
    public DbSet<CanonicalResearcherWork> CanonicalResearcherWorks { get; set; } = null!;
    public DbSet<HrEvidenceDossier> HrEvidenceDossiers { get; set; } = null!;
    public DbSet<HrDossierReviewAction> HrDossierReviewActions { get; set; } = null!;
    public DbSet<FacultyAssistantContextVersion> FacultyAssistantContextVersions { get; set; } = null!;
    public DbSet<FacultyAssistantRun> FacultyAssistantRuns { get; set; } = null!;

    public AnalysisDbContext(DbContextOptions<AnalysisDbContext> options)
        : base(options)
    {
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RejectSourceWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        RejectSourceWrites();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void RejectSourceWrites()
    {
        string[] attemptedTypes = ChangeTracker.Entries()
            .Where(entry => (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) &&
                entry.Metadata.GetSchema() is not ("analysis" or "hr" or "faculty"))
            .Select(entry => entry.Metadata.ClrType.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (attemptedTypes.Length != 0)
            throw new InvalidOperationException(
                $"Collector source projections are read-only: {string.Join(", ", attemptedTypes)}.");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CollectionChange>(entity =>
        {
            entity.ToTable("CollectionChanges", "core");
            entity.HasKey(value => value.EventId);
            entity.Property(value => value.EventId).ValueGeneratedNever();
            entity.Property(value => value.ChangeKind).HasMaxLength(40);
            entity.Property(value => value.PersonelId).HasColumnName("PersonelID").HasMaxLength(200);
        });
        modelBuilder.Entity<CollectionChangeReceipt>(entity =>
        {
            entity.ToTable("CollectionChangeReceipts", "analysis");
            entity.HasKey(value => value.EventId);
            entity.Property(value => value.EventId).ValueGeneratedNever();
        });
        modelBuilder.Entity<ReferencePopulationManifest>(entity =>
        {
            entity.ToTable("ReferencePopulationManifests", "analysis");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.ManifestVersion).HasMaxLength(100)
                .UseCollation("Latin1_General_100_BIN2");
            entity.Property(value => value.Fingerprint).HasMaxLength(64)
                .UseCollation("Latin1_General_100_BIN2");
            entity.Property(value => value.CohortDefinition).HasMaxLength(4000);
            entity.Property(value => value.EligibilityPolicyVersion).HasMaxLength(100);
            entity.Property(value => value.Provenance).HasMaxLength(4000);
            entity.Property(value => value.SamplingAndCoverage).HasMaxLength(4000);
            entity.Property(value => value.ImportedByActorAuditId).HasMaxLength(200);
            entity.Property(value => value.Reviewer).HasMaxLength(200);
            entity.Property(value => value.ReviewMethod).HasMaxLength(4000);
            entity.HasIndex(value => value.ManifestVersion).IsUnique();
        });
        modelBuilder.Entity<ReferencePopulationMember>(entity =>
        {
            entity.ToTable("ReferencePopulationMembers", "analysis");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.StableMemberId).HasMaxLength(200);
            entity.Property(value => value.ClassificationId).HasMaxLength(200);
            entity.Property(value => value.WorkType).HasMaxLength(100);
            entity.Property(value => value.Category).HasMaxLength(100);
            entity.HasIndex(value => new
            {
                value.ReferencePopulationManifestId,
                value.StableMemberId
            }).IsUnique();
            entity.HasOne(value => value.Manifest).WithMany(value => value.Members)
                .HasForeignKey(value => value.ReferencePopulationManifestId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<SemanticScholarPaper>(entity =>
        {
            entity.ToTable("SemanticScholarPapers", "semanticscholar"); entity.HasKey(x => x.Id);
            entity.Property(x => x.NormalizedDoi).HasMaxLength(500); entity.Property(x => x.PaperId).HasMaxLength(100);
            entity.Property(x => x.Title).HasMaxLength(2000); entity.Property(x => x.Venue).HasMaxLength(1000);
            entity.Property(x => x.Url).HasMaxLength(2000); entity.Property(x => x.TextAvailability).HasMaxLength(100);
            entity.Property(x => x.RefreshGeneration).HasMaxLength(32);
            entity.HasIndex(x => x.NormalizedDoi).IsUnique(); entity.HasIndex(x => x.PaperId).IsUnique().HasFilter("[PaperId] IS NOT NULL");
            entity.HasMany(x => x.Citations).WithOne(x => x.TargetPaper).HasForeignKey(x => x.TargetPaperId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<SemanticScholarCitation>(entity =>
        {
            entity.ToTable("SemanticScholarCitations", "semanticscholar"); entity.HasKey(x => x.Id);
            entity.Property(x => x.CitingPaperId).HasMaxLength(100); entity.Property(x => x.CitingDoi).HasMaxLength(500);
            entity.Property(x => x.CitingTitle).HasMaxLength(2000);
            entity.Property(x => x.RefreshGeneration).HasMaxLength(32);
            entity.HasIndex(x => new { x.TargetPaperId, x.CitingPaperId }).IsUnique();
            entity.HasMany(x => x.Contexts).WithOne(x => x.Citation).HasForeignKey(x => x.CitationId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<SemanticScholarCitationContext>(entity =>
        {
            entity.ToTable("SemanticScholarCitationContexts", "semanticscholar"); entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CitationId, x.Ordinal }).IsUnique();
        });
        modelBuilder.Entity<ArticleSummaries.SavedArticleSummary>(entity =>
        {
            entity.ToTable("ArticleSummaries", "analysis"); entity.HasKey(x => x.Id);
            entity.Property(x => x.PersonelId).HasColumnName("PersonelID").HasMaxLength(200);
            entity.Property(x => x.SourceUrl).HasMaxLength(2000); entity.Property(x => x.SourceHash).HasMaxLength(64);
            entity.Property(x => x.SourceKind).HasMaxLength(20); entity.Property(x => x.ExtractionVersion).HasMaxLength(100);
            entity.HasIndex(x => new { x.PersonelId, x.OriginalAcademicWorkId, x.Id }).IsDescending(false, false, true);
        });
        modelBuilder.Entity<ArticleSummaries.ArticleSourceSnapshot>(entity =>
        {
            entity.ToTable("ArticleSourceSnapshots", "analysis");
            entity.HasKey(snapshot => snapshot.Id);
            entity.Property(snapshot => snapshot.ExtractedTextHash).HasMaxLength(64)
                .UseCollation("Latin1_General_100_BIN2");
            entity.Property(snapshot => snapshot.SourceKind).HasMaxLength(20)
                .UseCollation("Latin1_General_100_BIN2");
            entity.Property(snapshot => snapshot.ExtractionVersion).HasMaxLength(100)
                .UseCollation("Latin1_General_100_BIN2");
            entity.HasIndex(snapshot => new
            {
                snapshot.CanonicalWorkId,
                snapshot.ExtractedTextHash,
                snapshot.SourceKind,
                snapshot.ExtractionVersion
            }).IsUnique().HasDatabaseName("UX_ArticleSourceSnapshots_Identity");
        });
        modelBuilder.Entity<ArticleSummaries.ArticleSourcePageSnapshot>(entity =>
        {
            entity.ToTable("ArticleSourcePages", "analysis");
            entity.HasKey(page => page.Id);
            entity.HasIndex(page => new { page.ArticleSourceSnapshotId, page.Ordinal }).IsUnique()
                .HasDatabaseName("UX_ArticleSourcePages_Snapshot_Ordinal");
            entity.HasOne(page => page.ArticleSourceSnapshot).WithMany(snapshot => snapshot.Pages)
                .HasForeignKey(page => page.ArticleSourceSnapshotId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ArticleSummaries.ArticleSourceSpanSnapshot>(entity =>
        {
            entity.ToTable("ArticleSourceSpans", "analysis");
            entity.HasKey(span => span.Id);
            entity.Property(span => span.SourceId).HasMaxLength(100);
            entity.Property(span => span.Text).HasMaxLength(550);
            entity.HasIndex(span => new { span.ArticleSourceSnapshotId, span.SourceId }).IsUnique()
                .HasDatabaseName("UX_ArticleSourceSpans_Snapshot_SourceId");
            entity.HasIndex(span => new { span.ArticleSourceSnapshotId, span.Ordinal }).IsUnique()
                .HasDatabaseName("UX_ArticleSourceSpans_Snapshot_Ordinal");
            entity.HasOne(span => span.ArticleSourceSnapshot).WithMany(snapshot => snapshot.Spans)
                .HasForeignKey(span => span.ArticleSourceSnapshotId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ArticleSummaries.CanonicalArticleAnalysisRun>(entity =>
        {
            entity.ToTable("CanonicalArticleAnalysisRuns", "analysis");
            entity.HasKey(run => run.Id);
            entity.Property(run => run.Language).HasMaxLength(20);
            entity.Property(run => run.PolicyVersion).HasMaxLength(100).IsRequired();
            entity.Property(run => run.SourceIdentityHash).HasMaxLength(64)
                .UseCollation("Latin1_General_100_BIN2").IsRequired();
            entity.Property(run => run.Model).HasMaxLength(200);
            entity.Property(run => run.PromptVersion).HasMaxLength(100);
            entity.Property(run => run.ExtractionMethod).HasMaxLength(50);
            entity.Property(run => run.ScopeReason).HasMaxLength(4000);
            entity.Property(run => run.SourceUrl).HasMaxLength(2000);
            entity.Property(run => run.SourceOrigin).HasMaxLength(200);
            entity.Property(run => run.VerificationStatus).HasMaxLength(50);
            entity.Property(run => run.VerificationModel).HasMaxLength(200);
            entity.Property(run => run.VerificationPromptVersion).HasMaxLength(100);
            entity.Property(run => run.VerificationLimitation).HasMaxLength(4000);
            entity.HasIndex(run => run.SavedArticleSummaryId).IsUnique()
                .HasDatabaseName("UX_CanonicalArticleAnalysisRuns_SavedArticleSummaryId");
            entity.HasIndex(run => new { run.CanonicalWorkId, run.Id }).IsDescending(false, true);
            entity.HasOne(run => run.ArticleSourceSnapshot).WithMany()
                .HasForeignKey(run => run.ArticleSourceSnapshotId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(run => run.SavedArticleSummary).WithMany()
                .HasForeignKey(run => run.SavedArticleSummaryId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<ArticleSummaries.ArticleSummaryAutomationJob>(entity =>
        {
            entity.ToTable("ArticleSummaryAutomationJobs", "analysis");
            entity.HasKey(job => job.Id);
            entity.Property(job => job.Language).HasMaxLength(20)
                .UseCollation("Latin1_General_100_BIN2");
            entity.Property(job => job.Status).HasMaxLength(20);
            entity.Property(job => job.DesiredInputHash).HasMaxLength(64);
            entity.Property(job => job.DesiredPolicyVersion).HasMaxLength(100);
            entity.Property(job => job.RunningInputHash).HasMaxLength(64);
            entity.Property(job => job.RunningPolicyVersion).HasMaxLength(100);
            entity.Property(job => job.ProcessedInputHash).HasMaxLength(64);
            entity.Property(job => job.ProcessedPolicyVersion).HasMaxLength(100);
            entity.Property(job => job.LastOutcomeCode).HasMaxLength(50);
            entity.Property(job => job.LastOutcomeMessage).HasMaxLength(500);
            entity.HasIndex(job => new { job.CanonicalWorkId, job.Language }).IsUnique()
                .HasDatabaseName("UX_ArticleSummaryAutomationJobs_Work_Language");
            entity.HasIndex(job => new { job.Status, job.NextAttemptAt, job.Id })
                .HasDatabaseName("IX_ArticleSummaryAutomationJobs_Status_NextAttemptAt");
            entity.HasOne(job => job.LastSuccessfulAnalysisRun).WithMany()
                .HasForeignKey(job => job.LastSuccessfulAnalysisRunId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<CanonicalArticleReviewRun>(entity =>
        {
            entity.ToTable("CanonicalArticleReviewRuns", "analysis");
            entity.HasKey(run => run.Id);
            entity.Property(run => run.Language).HasMaxLength(20).UseCollation("Latin1_General_100_BIN2");
            entity.Property(run => run.PolicyVersion).HasMaxLength(100).UseCollation("Latin1_General_100_BIN2");
            entity.Property(run => run.SettingsFingerprint).HasMaxLength(64)
                .UseCollation("Latin1_General_100_BIN2");
            entity.Property(run => run.Model).HasMaxLength(200);
            entity.Property(run => run.PromptVersion).HasMaxLength(100);
            entity.Property(run => run.Outcome).HasMaxLength(50);
            entity.Property(run => run.VerificationStatus).HasMaxLength(50);
            entity.Property(run => run.VerificationModel).HasMaxLength(200);
            entity.Property(run => run.VerificationPromptVersion).HasMaxLength(100);
            entity.Property(run => run.VerificationLimitation).HasMaxLength(1000);
            entity.Property(run => run.ScopeReason).HasMaxLength(4000);
            entity.HasIndex(run => new { run.BaseAnalysisRunId, run.Language, run.PolicyVersion, run.Id })
                .IsDescending(false, false, false, true).HasDatabaseName("IX_CanonicalArticleReviewRuns_Cache");
            entity.HasIndex(run => new { run.CanonicalWorkId, run.Language, run.Id })
                .IsDescending(false, false, true).HasDatabaseName("IX_CanonicalArticleReviewRuns_Work_Language_Id");
            entity.HasOne(run => run.BaseAnalysisRun).WithMany().HasForeignKey(run => run.BaseAnalysisRunId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(run => run.ArticleSourceSnapshot).WithMany().HasForeignKey(run => run.ArticleSourceSnapshotId)
                .OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<ArticleReviewWorkItem>(entity =>
        {
            entity.ToTable("ArticleReviewWorkItems", "analysis");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.WorkKey).HasMaxLength(64).UseCollation("Latin1_General_100_BIN2");
            entity.Property(value => value.Language).HasMaxLength(20).UseCollation("Latin1_General_100_BIN2");
            entity.Property(value => value.PolicyVersion).HasMaxLength(100).UseCollation("Latin1_General_100_BIN2");
            entity.Property(value => value.SettingsFingerprint).HasMaxLength(64).UseCollation("Latin1_General_100_BIN2");
            entity.Property(value => value.Status).HasMaxLength(40);
            entity.Property(value => value.MaximumSpendUsd).HasPrecision(19, 9);
            entity.HasIndex(value => value.WorkKey).IsUnique().HasDatabaseName("UX_ArticleReviewWorkItems_WorkKey");
            entity.HasIndex(value => new { value.BaseAnalysisRunId, value.Language, value.PolicyVersion,
                value.SettingsFingerprint, value.Id }).IsDescending(false, false, false, false, true)
                .HasDatabaseName("IX_ArticleReviewWorkItems_Resume");
            entity.HasOne(value => value.BaseAnalysisRun).WithMany().HasForeignKey(value => value.BaseAnalysisRunId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(value => value.ArticleSourceSnapshot).WithMany()
                .HasForeignKey(value => value.ArticleSourceSnapshotId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<ArticleReviewStageCheckpoint>(entity =>
        {
            entity.ToTable("ArticleReviewStageCheckpoints", "analysis");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Stage).HasMaxLength(30);
            entity.Property(value => value.Role).HasMaxLength(30);
            entity.Property(value => value.BatchKey).HasMaxLength(64).UseCollation("Latin1_General_100_BIN2");
            entity.Property(value => value.ParentBatchKey).HasMaxLength(64).UseCollation("Latin1_General_100_BIN2");
            entity.Property(value => value.Status).HasMaxLength(40);
            entity.Property(value => value.RequestFingerprint).HasMaxLength(64).UseCollation("Latin1_General_100_BIN2");
            entity.Property(value => value.ReservedCostUsd).HasPrecision(19, 9);
            entity.Property(value => value.ActualCostUsd).HasPrecision(19, 9);
            entity.Property(value => value.PricingVersion).HasMaxLength(100);
            entity.Property(value => value.ErrorCode).HasMaxLength(100);
            entity.HasIndex(value => new { value.ArticleReviewWorkItemId, value.Stage, value.Role, value.BatchKey })
                .IsUnique().HasDatabaseName("UX_ArticleReviewStageCheckpoints_Unit");
            entity.HasIndex(value => value.AttemptId).IsUnique()
                .HasFilter("[AttemptId] IS NOT NULL").HasDatabaseName("UX_ArticleReviewStageCheckpoints_AttemptId");
            entity.HasOne(value => value.ArticleReviewWorkItem).WithMany(value => value.Checkpoints)
                .HasForeignKey(value => value.ArticleReviewWorkItemId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<CanonicalArticleReviewFinding>(entity =>
        {
            entity.ToTable("CanonicalArticleReviewFindings", "analysis");
            entity.HasKey(finding => finding.Id);
            entity.Property(finding => finding.ExternalFindingId).HasMaxLength(120)
                .UseCollation("Latin1_General_100_BIN2");
            entity.Property(finding => finding.Role).HasMaxLength(30);
            entity.Property(finding => finding.Kind).HasMaxLength(30);
            entity.Property(finding => finding.Basis).HasMaxLength(1200);
            entity.Property(finding => finding.Suggestion).HasMaxLength(1200);
            entity.HasIndex(finding => new { finding.CanonicalArticleReviewRunId, finding.Ordinal }).IsUnique()
                .HasDatabaseName("UX_CanonicalArticleReviewFindings_Run_Ordinal");
            entity.HasIndex(finding => new { finding.CanonicalArticleReviewRunId, finding.ExternalFindingId }).IsUnique()
                .HasDatabaseName("UX_CanonicalArticleReviewFindings_Run_ExternalId");
            entity.HasOne(finding => finding.CanonicalArticleReviewRun).WithMany(run => run.Findings)
                .HasForeignKey(finding => finding.CanonicalArticleReviewRunId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<CanonicalArticleReviewEvidence>(entity =>
        {
            entity.ToTable("CanonicalArticleReviewEvidence", "analysis");
            entity.HasKey(evidence => evidence.Id);
            entity.HasIndex(evidence => new { evidence.CanonicalArticleReviewFindingId, evidence.Ordinal }).IsUnique()
                .HasDatabaseName("UX_CanonicalArticleReviewEvidence_Finding_Ordinal");
            entity.HasIndex(evidence => new { evidence.CanonicalArticleReviewFindingId, evidence.ArticleSourceSpanId }).IsUnique()
                .HasDatabaseName("UX_CanonicalArticleReviewEvidence_Finding_Span");
            entity.HasOne(evidence => evidence.CanonicalArticleReviewFinding).WithMany(finding => finding.Evidence)
                .HasForeignKey(evidence => evidence.CanonicalArticleReviewFindingId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(evidence => evidence.ArticleSourceSpan).WithMany()
                .HasForeignKey(evidence => evidence.ArticleSourceSpanId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<PublicationMetricSnapshot>(entity =>
        {
            entity.ToTable("PublicationMetricSnapshots", "analysis");
            entity.HasKey(snapshot => snapshot.Id);
            entity.Property(snapshot => snapshot.PersonelId).HasColumnName("PersonelID").HasMaxLength(200);
            entity.Property(snapshot => snapshot.CatalogVersion).HasMaxLength(100)
                .UseCollation("Latin1_General_100_BIN2");
            entity.HasIndex(snapshot => new
            {
                snapshot.PersonelId,
                snapshot.CatalogVersion,
                snapshot.SourceRevision,
                snapshot.ComputationYear
            }).IsUnique().HasDatabaseName("UX_PublicationMetricSnapshots_Identity");
            entity.HasIndex(snapshot => new { snapshot.PersonelId, snapshot.Id })
                .IsDescending(false, true)
                .HasDatabaseName("IX_PublicationMetricSnapshots_PersonelID_Id");
        });
        modelBuilder.Entity<PublicationMetricProviderSnapshot>(entity =>
        {
            entity.ToTable("PublicationMetricProviderSnapshots", "analysis");
            entity.HasKey(snapshot => snapshot.Id);
            entity.Property(snapshot => snapshot.Provider).HasMaxLength(50)
                .UseCollation("Latin1_General_100_BIN2");
            entity.Property(snapshot => snapshot.TwoYearMeanCitedness).HasPrecision(18, 4);
            entity.HasIndex(snapshot => new { snapshot.SnapshotId, snapshot.Provider })
                .IsUnique()
                .HasDatabaseName("UX_PublicationMetricProviderSnapshots_Snapshot_Provider");
            entity.HasOne(snapshot => snapshot.Snapshot)
                .WithMany(snapshot => snapshot.ProviderMetrics)
                .HasForeignKey(snapshot => snapshot.SnapshotId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<PublicationMetricsRefreshState>(entity =>
        {
            entity.ToTable("PublicationMetricsRefreshStates", "analysis");
            entity.HasKey(state => state.PersonelId);
            entity.Property(state => state.PersonelId).HasColumnName("PersonelID").HasMaxLength(200)
                .ValueGeneratedNever();
            entity.Property(state => state.RequestedCatalogVersion).HasMaxLength(100)
                .UseCollation("Latin1_General_100_BIN2");
            entity.Property(state => state.LastOutcomeCode).HasMaxLength(50);
            entity.Property(state => state.LastOutcomeMessage).HasMaxLength(500);
            entity.HasIndex(state => new { state.NextAttemptAt, state.PersonelId })
                .HasDatabaseName("IX_PublicationMetricsRefreshStates_NextAttemptAt");
            entity.HasOne(state => state.LastSuccessfulSnapshot).WithMany()
                .HasForeignKey(state => state.LastSuccessfulSnapshotId)
                .OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<ArticleSummaries.CanonicalArticleClaim>(entity =>
        {
            entity.ToTable("CanonicalArticleClaims", "analysis");
            entity.HasKey(claim => claim.Id);
            entity.Property(claim => claim.Section).HasMaxLength(20);
            entity.Property(claim => claim.ExternalClaimId).HasMaxLength(200);
            entity.Property(claim => claim.Text).HasMaxLength(1200);
            entity.HasIndex(claim => new
                { claim.CanonicalArticleAnalysisRunId, claim.SectionOrder, claim.Ordinal }).IsUnique()
                .HasDatabaseName("UX_CanonicalArticleClaims_Run_Section_Ordinal");
            entity.HasOne(claim => claim.CanonicalArticleAnalysisRun).WithMany(run => run.Claims)
                .HasForeignKey(claim => claim.CanonicalArticleAnalysisRunId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ArticleSummaries.CanonicalArticleClaimEvidence>(entity =>
        {
            entity.ToTable("CanonicalArticleClaimEvidence", "analysis");
            entity.HasKey(evidence => evidence.Id);
            entity.HasIndex(evidence => new { evidence.CanonicalArticleClaimId, evidence.Ordinal }).IsUnique()
                .HasDatabaseName("UX_CanonicalArticleClaimEvidence_Claim_Ordinal");
            entity.HasOne(evidence => evidence.CanonicalArticleClaim).WithMany(claim => claim.Evidence)
                .HasForeignKey(evidence => evidence.CanonicalArticleClaimId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(evidence => evidence.ArticleSourceSpan).WithMany()
                .HasForeignKey(evidence => evidence.ArticleSourceSpanId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Analysis.SavedResearcherAnalysis>(entity =>
        {
            entity.ToTable("ResearcherAnalyses", "analysis");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.PersonelId).HasColumnName("PersonelID").HasMaxLength(200);
            entity.HasIndex(value => new { value.PersonelId, value.Id }).IsDescending(false, true);
        });

        modelBuilder.Entity<Researcher>(entity =>
        {
            entity.ToTable("Researchers", "core");
            entity.HasKey(researcher => researcher.PersonelId);
            entity.Property(researcher => researcher.PersonelId).HasColumnName("PersonelID").HasMaxLength(200).ValueGeneratedNever();
            entity.Property(researcher => researcher.Orcid).HasColumnName("ORCID").HasMaxLength(19);
            entity.Property(researcher => researcher.ScopusId).HasColumnName("ScopusID");
            entity.Property(researcher => researcher.GoogleScholarId)
                .HasColumnName("ScholarID")
                .HasMaxLength(32);
            entity.Property(researcher => researcher.WebOfScienceResearcherId)
                .HasColumnName("ResearcherID")
                .HasMaxLength(20);
            entity.Property(researcher => researcher.TcKimlikNo)
                .HasMaxLength(11);
            entity.Property(researcher => researcher.OpenAlexTwoYearMeanCitedness)
                .HasPrecision(18, 4);

            entity.HasIndex(researcher => researcher.Orcid)
                .IsUnique()
                .HasFilter("[ORCID] IS NOT NULL");

            entity.HasIndex(researcher => researcher.GoogleScholarId)
                .IsUnique()
                .HasFilter("[ScholarID] IS NOT NULL");

            entity.HasIndex(researcher => researcher.WebOfScienceResearcherId)
                .IsUnique()
                .HasFilter("[ResearcherID] IS NOT NULL");

            entity.HasIndex(researcher => researcher.TcKimlikNo)
                .IsUnique()
                .HasFilter("[TcKimlikNo] IS NOT NULL");

            entity.HasMany(researcher => researcher.AcademicWorks)
                .WithOne(work => work.Researcher)
                .HasForeignKey(work => work.PersonelId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(researcher => researcher.OrcidProfile)
                .WithOne(profile => profile.Researcher)
                .HasForeignKey<OrcidProfile>(profile => profile.PersonelId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(researcher => researcher.GoogleScholarProfile)
                .WithOne(profile => profile.Researcher)
                .HasForeignKey<GoogleScholarProfile>(profile => profile.PersonelId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(researcher => researcher.OpenAlexProfile)
                .WithOne(profile => profile.Researcher)
                .HasForeignKey<OpenAlexProfile>(profile => profile.PersonelId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(researcher => researcher.WebOfScienceProfile)
                .WithOne(profile => profile.Researcher)
                .HasForeignKey<WebOfScienceProfile>(profile => profile.PersonelId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(researcher => researcher.TrDizinProfile).WithOne(profile => profile.Researcher)
                .HasForeignKey<TrDizinProfile>(profile => profile.PersonelId).OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(researcher => researcher.PublicationSummaries)
                .WithOne(summary => summary.Researcher)
                .HasForeignKey(summary => summary.PersonelId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(researcher => researcher.PublicationDisplayApprovals)
                .WithOne(approval => approval.Researcher)
                .HasForeignKey(approval => approval.PersonelId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasMany(researcher => researcher.YoksisRecords)
                .WithOne(record => record.Researcher)
                .HasForeignKey(record => record.PersonelId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<YoksisRecord>(entity =>
        {
            entity.ToTable("YoksisRecords", "yoksis");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.CategoryName).HasMaxLength(250);
            entity.Property(record => record.OperationName).HasMaxLength(250);
            entity.Property(record => record.ExternalRecordId).HasMaxLength(500);
            entity.Property(record => record.RecordJson);
            entity.Property(record => record.PersonelId).HasColumnName("PersonelID").HasMaxLength(200);
            entity.HasIndex(record => record.PersonelId);
            entity.HasIndex(record => new
            {
                record.PersonelId,
                record.OperationName
            });
        });
        modelBuilder.Entity<TrDizinProfile>(entity =>
        {
            entity.ToTable("TrDizinProfiles", "trdizin");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PersonelId).HasColumnName("PersonelID").HasMaxLength(200);
            entity.Property(x => x.Orcid).HasMaxLength(19);
            entity.Property(x => x.DisplayName).HasMaxLength(500);
            entity.HasIndex(x => x.PersonelId).IsUnique();
            entity.HasMany(x => x.Works)
                .WithOne(x => x.TrDizinProfile)
                .HasForeignKey(x => x.TrDizinProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<TrDizinWork>(entity =>
        {
            entity.ToTable("TrDizinWorks", "trdizin");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PublicationId).HasMaxLength(100);
            entity.Property(x => x.Title).HasMaxLength(2000);
            entity.Property(x => x.Doi).HasMaxLength(500);
            entity.Property(x => x.PublicationType).HasMaxLength(100);
            entity.Property(x => x.Authors);
            entity.Property(x => x.Journal).HasMaxLength(2000);
            entity.HasIndex(x => new { x.TrDizinProfileId, x.PublicationId }).IsUnique();
        });
        modelBuilder.Entity<CrossrefWork>(entity =>
        {
            entity.ToTable("CrossrefWorks", "crossref");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PersonelId).HasColumnName("PersonelID").HasMaxLength(200);
            entity.Property(x => x.Doi).HasMaxLength(500);
            entity.Property(x => x.Title).HasMaxLength(2000);
            entity.Property(x => x.Authors);
            entity.Property(x => x.ContainerTitle).HasMaxLength(2000);
            entity.Property(x => x.Type).HasMaxLength(100);
            entity.Property(x => x.Url).HasMaxLength(2000);
            entity.HasIndex(x => new { x.PersonelId, x.Doi }).IsUnique();
        });

        modelBuilder.Entity<OrcidProfile>(entity =>
        {
            entity.ToTable("OrcidProfiles", "orcid");
            entity.HasKey(profile => profile.Id);
            entity.Property(profile => profile.DisplayName).HasMaxLength(500);
            entity.Property(profile => profile.GivenNames).HasMaxLength(250);
            entity.Property(profile => profile.FamilyName).HasMaxLength(250);
            entity.Property(profile => profile.CreditName).HasMaxLength(500);
            entity.Property(profile => profile.CountryCodes).HasMaxLength(250);
            entity.Property(profile => profile.Keywords).HasMaxLength(4000);
            entity.Property(profile => profile.CurrentOrganization).HasMaxLength(1000);
            entity.Property(profile => profile.CurrentDepartment).HasMaxLength(1000);
            entity.Property(profile => profile.CurrentRoleTitle).HasMaxLength(500);
            entity.Property(profile => profile.PersonelId).HasColumnName("PersonelID").HasMaxLength(200);
            entity.HasIndex(profile => profile.PersonelId).IsUnique();

            entity.HasMany(profile => profile.Works)
                .WithOne(work => work.OrcidProfile)
                .HasForeignKey(work => work.OrcidProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrcidWork>(entity =>
        {
            entity.ToTable("OrcidWorks", "orcid");
            entity.HasKey(work => work.Id);
            entity.Property(work => work.Title).HasMaxLength(2000);
            entity.Property(work => work.Subtitle).HasMaxLength(2000);
            entity.Property(work => work.TranslatedTitle).HasMaxLength(2000);
            entity.Property(work => work.WorkType).HasMaxLength(100);
            entity.Property(work => work.JournalTitle).HasMaxLength(2000);
            entity.Property(work => work.Doi).HasMaxLength(500);
            entity.Property(work => work.Url).HasMaxLength(2000);
            entity.Property(work => work.Authors);
            entity.Property(work => work.LanguageCode).HasMaxLength(20);
            entity.Property(work => work.CountryCode).HasMaxLength(20);
            entity.Property(work => work.SourceName).HasMaxLength(500);
            entity.Property(work => work.Visibility).HasMaxLength(50);
            entity.Property(work => work.Category).HasConversion<string>().HasMaxLength(50);
            entity.Property(work => work.CategorySource).HasConversion<string>().HasMaxLength(50);
            entity.HasIndex(work => new { work.OrcidProfileId, work.PutCode }).IsUnique();
        });

        modelBuilder.Entity<GoogleScholarProfile>(entity =>
        {
            entity.ToTable("GoogleScholarProfiles", "googlescholar");
            entity.HasKey(profile => profile.Id);
            entity.Property(profile => profile.DisplayName).HasMaxLength(500);
            entity.Property(profile => profile.Affiliations).HasMaxLength(2000);
            entity.Property(profile => profile.University).HasMaxLength(1000);
            entity.Property(profile => profile.VerifiedEmail).HasMaxLength(500);
            entity.Property(profile => profile.ProfileUrl).HasMaxLength(2000);
            entity.Property(profile => profile.PersonelId).HasColumnName("PersonelID").HasMaxLength(200);
            entity.HasIndex(profile => profile.PersonelId).IsUnique();

            entity.HasMany(profile => profile.Works)
                .WithOne(work => work.GoogleScholarProfile)
                .HasForeignKey(work => work.GoogleScholarProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GoogleScholarWork>(entity =>
        {
            entity.ToTable("GoogleScholarWorks", "googlescholar");
            entity.HasKey(work => work.Id);
            entity.Property(work => work.CitationId).HasMaxLength(200);
            entity.Property(work => work.Title).HasMaxLength(2000);
            entity.Property(work => work.Authors).HasMaxLength(4000);
            entity.Property(work => work.Publication).HasMaxLength(2000);
            entity.Property(work => work.Url).HasMaxLength(2000);
            entity.HasIndex(work => new
            {
                work.GoogleScholarProfileId,
                work.CitationId
            }).IsUnique();
        });

        modelBuilder.Entity<OpenAlexProfile>(entity =>
        {
            entity.ToTable("OpenAlexProfiles", "openalex");
            entity.HasKey(profile => profile.Id);
            entity.Property(profile => profile.OpenAlexAuthorId).HasMaxLength(100);
            entity.Property(profile => profile.DisplayName).HasMaxLength(500);
            entity.Property(profile => profile.LastKnownInstitution).HasMaxLength(1000);
            entity.Property(profile => profile.TwoYearMeanCitedness)
                .HasPrecision(18, 4);
            entity.Property(profile => profile.PersonelId).HasColumnName("PersonelID").HasMaxLength(200);
            entity.HasIndex(profile => profile.PersonelId).IsUnique();
            entity.HasIndex(profile => profile.OpenAlexAuthorId).IsUnique();

            entity.HasMany(profile => profile.Works)
                .WithOne(work => work.OpenAlexProfile)
                .HasForeignKey(work => work.OpenAlexProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OpenAlexWork>(entity =>
        {
            entity.ToTable("OpenAlexWorks", "openalex");
            entity.HasKey(work => work.Id);
            entity.Property(work => work.OpenAlexWorkId).HasMaxLength(100);
            entity.Property(work => work.Title).HasMaxLength(2000);
            entity.Property(work => work.Doi).HasMaxLength(500);
            entity.Property(work => work.WorkType).HasMaxLength(100);
            entity.Property(work => work.Authors).HasMaxLength(4000);
            entity.Property(work => work.SourceName).HasMaxLength(2000);
            entity.Property(work => work.Url).HasMaxLength(2000);
            entity.Property(work => work.OpenAccessUrl).HasMaxLength(2000);
            entity.HasIndex(work => new
            {
                work.OpenAlexProfileId,
                work.OpenAlexWorkId
            }).IsUnique();
        });

        modelBuilder.Entity<WebOfScienceProfile>(entity =>
        {
            entity.ToTable("WebOfScienceProfiles", "wos");
            entity.HasKey(profile => profile.Id);
            entity.Property(profile => profile.DisplayName).HasMaxLength(500);
            entity.Property(profile => profile.FirstName).HasMaxLength(250);
            entity.Property(profile => profile.LastName).HasMaxLength(250);
            entity.Property(profile => profile.Orcid).HasMaxLength(19);
            entity.Property(profile => profile.PrimaryOrganization).HasMaxLength(1000);
            entity.Property(profile => profile.PrimaryAddress).HasMaxLength(2000);
            entity.Property(profile => profile.PrimaryCountry).HasMaxLength(250);
            entity.Property(profile => profile.Departments).HasMaxLength(2000);
            entity.Property(profile => profile.PersonelId).HasColumnName("PersonelID").HasMaxLength(200);
            entity.HasIndex(profile => profile.PersonelId).IsUnique();

            entity.HasMany(profile => profile.Works)
                .WithOne(work => work.WebOfScienceProfile)
                .HasForeignKey(work => work.WebOfScienceProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(profile => profile.PeerReviews)
                .WithOne(peerReview => peerReview.WebOfScienceProfile)
                .HasForeignKey(peerReview => peerReview.WebOfScienceProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WebOfScienceWork>(entity =>
        {
            entity.ToTable("WebOfScienceWorks", "wos");
            entity.HasKey(work => work.Id);
            entity.Property(work => work.Uid).HasMaxLength(100);
            entity.Property(work => work.Title).HasMaxLength(2000);
            entity.Property(work => work.WorkTypes).HasMaxLength(500);
            entity.Property(work => work.SourceTitle).HasMaxLength(2000);
            entity.Property(work => work.Volume).HasMaxLength(100);
            entity.Property(work => work.Issue).HasMaxLength(100);
            entity.Property(work => work.Collection).HasMaxLength(100);
            entity.Property(work => work.Doi).HasMaxLength(500);
            entity.Property(work => work.Category).HasConversion<string>().HasMaxLength(50);
            entity.Property(work => work.CategorySource).HasConversion<string>().HasMaxLength(50);
            entity.HasIndex(work => new
            {
                work.WebOfScienceProfileId,
                work.Uid
            })
                .IsUnique();
        });

        modelBuilder.Entity<WebOfSciencePeerReview>(entity =>
        {
            entity.ToTable("WebOfSciencePeerReviews", "wos");
            entity.HasKey(peerReview => peerReview.Id);
            entity.Property(peerReview => peerReview.Journal).HasMaxLength(2000);
            entity.Property(peerReview => peerReview.Publisher).HasMaxLength(2000);
            entity.Property(peerReview => peerReview.DateOfReview).HasMaxLength(100);
            entity.Property(peerReview => peerReview.Verified).HasMaxLength(20);
            entity.Property(peerReview => peerReview.ArticleTitle).HasMaxLength(2000);
            entity.Property(peerReview => peerReview.ArticleDoi).HasMaxLength(500);
            entity.HasIndex(peerReview => peerReview.WebOfScienceProfileId);
        });

        modelBuilder.Entity<AcademicWork>(entity =>
        {
            entity.ToTable("AcademicWorks", "core");
            entity.HasKey(work => work.Id);
            entity.Property(work => work.Provider)
                .HasConversion<string>()
                .HasMaxLength(50);
            entity.Property(work => work.ProviderWorkId).HasMaxLength(500);
            entity.Property(work => work.Title).HasMaxLength(2000);
            entity.Property(work => work.Doi).HasMaxLength(500);
            entity.Property(work => work.RawType).HasMaxLength(100);
            entity.Property(work => work.Category)
                .HasConversion<string>()
                .HasMaxLength(50);
            entity.Property(work => work.CategorySource)
                .HasConversion<string>()
                .HasMaxLength(50);
            entity.Property(work => work.Authors);
            entity.Property(work => work.Institutions).HasMaxLength(4000);
            entity.Property(work => work.Keywords).HasMaxLength(4000);
            entity.Property(work => work.Topics).HasMaxLength(4000);
            entity.Property(work => work.Language).HasMaxLength(20);
            entity.Property(work => work.Publication).HasMaxLength(2000);
            entity.Property(work => work.Volume).HasMaxLength(100);
            entity.Property(work => work.Issue).HasMaxLength(100);
            entity.Property(work => work.FirstPage).HasMaxLength(100);
            entity.Property(work => work.LastPage).HasMaxLength(100);
            entity.Property(work => work.Link).HasMaxLength(2000);
            entity.Property(work => work.SourceId).HasMaxLength(500);
            entity.Property(work => work.SourceName).HasMaxLength(2000);
            entity.Property(work => work.SourceType).HasMaxLength(100);
            entity.Property(work => work.OpenAccessStatus).HasMaxLength(50);
            entity.Property(work => work.FullTextUrl).HasMaxLength(2000);
            entity.Property(work => work.License).HasMaxLength(100);
            entity.Property(work => work.Version).HasMaxLength(100);

            entity.Property(work => work.PersonelId).HasColumnName("PersonelID").HasMaxLength(200);
            entity.HasIndex(work => work.PersonelId);
            entity.HasIndex(work => new { work.PersonelId, work.Provider });

            entity.HasMany(work => work.Sources)
                .WithOne(source => source.AcademicWork)
                .HasForeignKey(source => source.AcademicWorkId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(work => work.CanonicalObservation)
                .WithOne(observation => observation.AcademicWork)
                .HasForeignKey<CanonicalWorkObservation>(observation => observation.AcademicWorkId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(work => work.ResearchContext)
                .WithOne(context => context.AcademicWork)
                .HasForeignKey<AcademicWorkResearchContext>(context => context.AcademicWorkId)
                .OnDelete(DeleteBehavior.Cascade);

        });

        modelBuilder.Entity<AcademicWorkResearchContext>(entity =>
        {
            entity.ToTable("AcademicWorkResearchContexts", "core");
            entity.HasKey(context => context.AcademicWorkId);
            entity.Property(context => context.Provider).HasMaxLength(50);
            entity.Property(context => context.SourceWorkId).HasMaxLength(500);
            entity.Property(context => context.ParserVersion).HasMaxLength(100);
            entity.Property(context => context.PayloadFingerprint).HasMaxLength(64)
                .UseCollation("Latin1_General_100_BIN2");
            entity.Property(context => context.RawType).HasMaxLength(100);
            entity.Property(context => context.PrimarySourceType).HasMaxLength(100);
            entity.Property(context => context.Fwci).HasPrecision(18, 6);
            entity.Property(context => context.CitationNormalizedPercentile).HasPrecision(18, 9);
            entity.Property(context => context.ParseQuality).HasMaxLength(30);
            entity.Property(context => context.ParseQualityReason).HasMaxLength(500);
            entity.Property(context => context.PrimaryTopicQuality).HasMaxLength(30);
            entity.Property(context => context.PrimaryTopicQualityReason).HasMaxLength(500);
        });

        modelBuilder.Entity<AcademicWorkTopic>(entity =>
        {
            entity.ToTable("AcademicWorkTopics", "core");
            entity.HasKey(topic => topic.Id);
            entity.Property(topic => topic.TopicId).HasMaxLength(200)
                .UseCollation("Latin1_General_100_BIN2");
            entity.Property(topic => topic.TopicName).HasMaxLength(1000);
            entity.Property(topic => topic.SubfieldId).HasMaxLength(200);
            entity.Property(topic => topic.SubfieldName).HasMaxLength(1000);
            entity.Property(topic => topic.FieldId).HasMaxLength(200);
            entity.Property(topic => topic.FieldName).HasMaxLength(1000);
            entity.Property(topic => topic.DomainId).HasMaxLength(200);
            entity.Property(topic => topic.DomainName).HasMaxLength(1000);
            entity.Property(topic => topic.ScoreQuality).HasMaxLength(30);
            entity.Property(topic => topic.ScoreQualityReason).HasMaxLength(500);
            entity.HasIndex(topic => new { topic.AcademicWorkId, topic.TopicId }).IsUnique()
                .HasDatabaseName("UX_AcademicWorkTopics_Work_Topic");
            entity.HasIndex(topic => new { topic.AcademicWorkId, topic.IsPrimary })
                .HasDatabaseName("IX_AcademicWorkTopics_Work_Primary");
            entity.HasOne(topic => topic.ResearchContext).WithMany(context => context.Topics)
                .HasForeignKey(topic => topic.AcademicWorkId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AcademicWorkSource>(entity =>
        {
            entity.ToTable("AcademicWorkSources", "core");
            entity.HasKey(source => source.Id);
            entity.Property(source => source.Url).HasMaxLength(2000);
            entity.Property(source => source.Kind).HasMaxLength(20);
            entity.Property(source => source.Origin).HasMaxLength(100);
            entity.HasIndex(source => source.AcademicWorkId);
        });

        modelBuilder.Entity<PublicationSummary>(entity =>
        {
            entity.ToTable("PublicationSummaries", "core");
            entity.HasKey(summary => summary.Id);
            entity.Property(summary => summary.Fingerprint).HasMaxLength(64);
            entity.Property(summary => summary.Title).HasMaxLength(2000);
            entity.Property(summary => summary.Doi).HasMaxLength(500);
            entity.Property(summary => summary.Category)
                .HasConversion<string>()
                .HasMaxLength(50);
            entity.Property(summary => summary.Authors);
            entity.Property(summary => summary.Publication).HasMaxLength(2000);
            entity.Property(summary => summary.PublicationUrl).HasMaxLength(2000);
            entity.Property(summary => summary.Sources).HasMaxLength(200);

            entity.Property(summary => summary.PersonelId).HasColumnName("PersonelID").HasMaxLength(200);
            entity.HasIndex(summary => summary.PersonelId);
            entity.HasIndex(summary => new { summary.PersonelId, summary.CanonicalWorkId })
                .IsUnique()
                .HasFilter("[CanonicalWorkId] IS NOT NULL")
                .HasDatabaseName("UX_PublicationSummaries_PersonelID_CanonicalWorkId");
            entity.HasIndex(summary => new
            {
                summary.PersonelId,
                summary.Fingerprint
            })
                .IsUnique();

            entity.HasOne(summary => summary.DisplayApproval)
                .WithOne(approval => approval.PublicationSummary)
                .HasForeignKey<PublicationDisplayApproval>(
                    approval => approval.PublicationSummaryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PublicationDisplayApproval>(entity =>
        {
            entity.ToTable("PublicationDisplayApprovals", "core");
            entity.HasKey(approval => approval.Id);
            entity.Property(approval => approval.PersonelId).HasColumnName("PersonelID").HasMaxLength(200);
            entity.HasIndex(approval => approval.PersonelId);
            entity.HasIndex(approval => approval.PublicationSummaryId).IsUnique();
        });

        modelBuilder.Entity<CanonicalWork>(entity =>
        {
            entity.ToTable("CanonicalWorks", "core", table =>
                table.HasCheckConstraint("CK_CanonicalWorks_ExactlyOneIdentity",
                    "([NormalizedDoi] IS NOT NULL AND [SourceScopedKey] IS NULL) OR " +
                    "([NormalizedDoi] IS NULL AND [SourceScopedKey] IS NOT NULL)"));
            entity.HasKey(work => work.Id);
            entity.Property(work => work.NormalizedDoi).HasMaxLength(500)
                .UseCollation("Latin1_General_100_BIN2");
            entity.Property(work => work.SourceScopedKey).HasMaxLength(64);
            entity.HasIndex(work => work.NormalizedDoi).IsUnique()
                .HasFilter("[NormalizedDoi] IS NOT NULL")
                .HasDatabaseName("UX_CanonicalWorks_NormalizedDoi");
            entity.HasIndex(work => work.SourceScopedKey).IsUnique()
                .HasFilter("[SourceScopedKey] IS NOT NULL")
                .HasDatabaseName("UX_CanonicalWorks_SourceScopedKey");
        });

        modelBuilder.Entity<CanonicalWorkObservation>(entity =>
        {
            entity.ToTable("CanonicalWorkObservations", "core");
            entity.HasKey(observation => observation.Id);
            entity.Property(observation => observation.PersonelId).HasColumnName("PersonelID").HasMaxLength(200);
            entity.Property(observation => observation.Provider).HasConversion<string>().HasMaxLength(50);
            entity.Property(observation => observation.ProviderWorkId).HasMaxLength(500);
            entity.Property(observation => observation.TitleObserved).HasMaxLength(2000);
            entity.Property(observation => observation.DoiObserved).HasMaxLength(500);
            entity.Property(observation => observation.CategoryObserved).HasConversion<string>().HasMaxLength(50);
            entity.Property(observation => observation.AuthorsObserved);
            entity.Property(observation => observation.PublicationObserved).HasMaxLength(2000);
            entity.Property(observation => observation.SourceId).HasMaxLength(500);
            entity.Property(observation => observation.SourceName).HasMaxLength(2000);
            entity.Property(observation => observation.SourceType).HasMaxLength(100);
            entity.Property(observation => observation.Link).HasMaxLength(2000);
            entity.Property(observation => observation.FullTextUrl).HasMaxLength(2000);
            entity.Property(observation => observation.License).HasMaxLength(100);
            entity.Property(observation => observation.Version).HasMaxLength(100);
            entity.HasIndex(observation => observation.AcademicWorkId).IsUnique()
                .HasDatabaseName("UX_CanonicalWorkObservations_AcademicWorkId");
            entity.HasIndex(observation => new { observation.CanonicalWorkId, observation.PersonelId });
            entity.HasOne(observation => observation.CanonicalWork)
                .WithMany(work => work.Observations)
                .HasForeignKey(observation => observation.CanonicalWorkId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CanonicalResearcherWork>(entity =>
        {
            entity.ToTable("CanonicalResearcherWorks", "core");
            entity.HasKey(association => new { association.CanonicalWorkId, association.PersonelId });
            entity.Property(association => association.PersonelId).HasColumnName("PersonelID").HasMaxLength(200);
            entity.HasIndex(association => new { association.PersonelId, association.CanonicalWorkId });
            entity.HasOne(association => association.CanonicalWork)
                .WithMany(work => work.Researchers)
                .HasForeignKey(association => association.CanonicalWorkId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(association => association.Researcher)
                .WithMany(researcher => researcher.CanonicalWorks)
                .HasForeignKey(association => association.PersonelId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ArticleEvaluationRun>(entity =>
        {
            entity.ToTable("ArticleEvaluationRuns", "analysis");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.OwnerPersonelId).HasColumnName("OwnerPersonelID").HasMaxLength(200)
                .UseCollation("Latin1_General_100_BIN2");
            entity.Property(value => value.ActorAuditId).HasMaxLength(200);
            entity.Property(value => value.AuthorizationGrantId).HasMaxLength(400);
            entity.Property(value => value.Status).HasMaxLength(40);
            entity.Property(value => value.DatasetVersion).HasMaxLength(100).UseCollation("Latin1_General_100_BIN2");
            entity.Property(value => value.EvaluatorVersion).HasMaxLength(100).UseCollation("Latin1_General_100_BIN2");
            entity.Property(value => value.PolicyVersion).HasMaxLength(100).UseCollation("Latin1_General_100_BIN2");
            entity.HasIndex(value => value.RunId).IsUnique();
            entity.HasMany(value => value.Cases).WithOne(value => value.Run)
                .HasForeignKey(value => value.ArticleEvaluationRunId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ArticleEvaluationCase>(entity =>
        {
            entity.ToTable("ArticleEvaluationCases", "analysis");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.CaseId).HasMaxLength(120).UseCollation("Latin1_General_100_BIN2");
            entity.Property(value => value.Kind).HasMaxLength(30);
            entity.Property(value => value.Language).HasMaxLength(20);
            entity.Property(value => value.SourceHash).HasMaxLength(64).UseCollation("Latin1_General_100_BIN2");
            entity.HasIndex(value => new { value.ArticleEvaluationRunId, value.Ordinal }).IsUnique();
            entity.HasIndex(value => new { value.ArticleEvaluationRunId, value.CaseId }).IsUnique();
            entity.HasMany(value => value.WorkItems).WithOne(value => value.Case)
                .HasForeignKey(value => value.ArticleEvaluationCaseId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ArticleEvaluationWorkItem>(entity =>
        {
            entity.ToTable("ArticleEvaluationWorkItems", "analysis");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Phase).HasMaxLength(30);
            entity.Property(value => value.ProfileId).HasMaxLength(100).UseCollation("Latin1_General_100_BIN2");
            entity.Property(value => value.ProfileFingerprint).HasMaxLength(64).UseCollation("Latin1_General_100_BIN2");
            entity.Property(value => value.ProfileSnapshotJson).HasMaxLength(4000);
            entity.Property(value => value.Status).HasMaxLength(40);
            entity.Property(value => value.OutcomeCode).HasMaxLength(100);
            entity.Property(value => value.OutcomeMessage).HasMaxLength(1000);
            entity.HasIndex(value => new { value.ArticleEvaluationCaseId, value.Ordinal }).IsUnique();
            entity.HasOne(value => value.DependsOn).WithMany().HasForeignKey(value => value.DependsOnWorkItemId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasMany(value => value.Attempts).WithOne(value => value.WorkItem)
                .HasForeignKey(value => value.ArticleEvaluationWorkItemId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(value => value.Result).WithOne(value => value.WorkItem)
                .HasForeignKey<ArticleEvaluationResult>(value => value.ArticleEvaluationWorkItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ArticleEvaluationAttempt>(entity =>
        {
            entity.ToTable("ArticleEvaluationAttempts", "analysis");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Status).HasMaxLength(40);
            entity.Property(value => value.RequestHash).HasMaxLength(64);
            entity.Property(value => value.ReturnedModelIdentity).HasMaxLength(500);
            entity.Property(value => value.EstimatedCostUsd).HasPrecision(18, 8);
            entity.Property(value => value.CostStatus).HasMaxLength(30);
            entity.Property(value => value.ErrorCode).HasMaxLength(100);
            entity.Property(value => value.ErrorMessage).HasMaxLength(1000);
            entity.HasIndex(value => new { value.ArticleEvaluationWorkItemId, value.AttemptNumber }).IsUnique();
            entity.HasIndex(value => value.ExecutionToken).IsUnique();
        });
        modelBuilder.Entity<ArticleEvaluationResult>(entity =>
        {
            entity.ToTable("ArticleEvaluationResults", "analysis");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.ActualModelIdentity).HasMaxLength(500);
            entity.HasIndex(value => value.ArticleEvaluationWorkItemId).IsUnique();
        });

        modelBuilder.Entity<HrEvidenceDossier>(entity =>
        {
            entity.ToTable("EvidenceDossiers", "hr");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.PersonelId).HasColumnName("PersonelID").HasMaxLength(200);
            entity.Property(value => value.CreatedByActorId).HasMaxLength(200);
            entity.Property(value => value.PolicyVersion).HasMaxLength(100).UseCollation("Latin1_General_100_BIN2");
            entity.Property(value => value.InputFingerprint).HasMaxLength(64).UseCollation("Latin1_General_100_BIN2");
            entity.HasIndex(value => new { value.PersonelId, value.Id }).IsDescending(false, true);
            entity.HasOne<PublicationMetricSnapshot>().WithMany().HasForeignKey(value => value.PublicationMetricSnapshotId)
                .OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<HrDossierReviewAction>(entity =>
        {
            entity.ToTable("DossierReviewActions", "hr");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.ActorAuditId).HasMaxLength(200);
            entity.Property(value => value.ActionType).HasMaxLength(40).UseCollation("Latin1_General_100_BIN2");
            entity.Property(value => value.EvidenceReference).HasMaxLength(200);
            entity.Property(value => value.Note).HasMaxLength(4000);
            entity.HasIndex(value => new { value.DossierId, value.ActorAuditId, value.ClientRequestId }).IsUnique();
            entity.HasOne(value => value.Dossier).WithMany(value => value.ReviewActions)
                .HasForeignKey(value => value.DossierId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<FacultyAssistantContextVersion>(entity =>
        {
            entity.ToTable("AssistantContextVersions", "faculty");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.PersonelId).HasColumnName("PersonelID").HasMaxLength(200);
            entity.Property(value => value.ContextFingerprint).HasMaxLength(64).UseCollation("Latin1_General_100_BIN2");
            entity.Property(value => value.CreatedByActorId).HasMaxLength(200);
            entity.HasIndex(value => new { value.PersonelId, value.Version }).IsUnique();
        });
        modelBuilder.Entity<FacultyAssistantRun>(entity =>
        {
            entity.ToTable("AssistantRuns", "faculty");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.PersonelId).HasColumnName("PersonelID").HasMaxLength(200);
            entity.Property(value => value.ActorAuditId).HasMaxLength(200);
            entity.Property(value => value.AuthorizationGrantId).HasMaxLength(400).UseCollation("Latin1_General_100_BIN2");
            entity.Property(value => value.Mode).HasMaxLength(40).UseCollation("Latin1_General_100_BIN2");
            entity.Property(value => value.Language).HasMaxLength(2).UseCollation("Latin1_General_100_BIN2");
            entity.Property(value => value.RetrievalPolicyVersion).HasMaxLength(100).UseCollation("Latin1_General_100_BIN2");
            entity.Property(value => value.Status).HasMaxLength(20).UseCollation("Latin1_General_100_BIN2");
            entity.Property(value => value.InputFingerprint).HasMaxLength(64).UseCollation("Latin1_General_100_BIN2");
            entity.Property(value => value.ErrorCode).HasMaxLength(80);
            entity.Property(value => value.ErrorMessage).HasMaxLength(1000);
            entity.HasIndex(value => value.RunId).IsUnique();
            entity.HasIndex(value => new { value.PersonelId, value.ActorAuditId, value.ClientRequestId }).IsUnique();
            entity.HasIndex(value => new { value.Status, value.Id });
            entity.HasOne<FacultyAssistantContextVersion>().WithMany().HasForeignKey(value => value.ContextVersionId)
                .OnDelete(DeleteBehavior.NoAction);
        });

    }
}

using AcademicCollectorDemo.Modules.AcademicPerformance.Bulk.Models;
using Microsoft.EntityFrameworkCore;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Persistence;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.TrDizin;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Crossref;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.SemanticScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Scopus;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data;

public sealed class AcademicDbContext : DbContext
{
    public DbSet<CollectionChange> CollectionChanges { get; set; } = null!;
    public DbSet<BulkCollectionBatch> BulkCollectionBatches { get; set; } = null!;
    public DbSet<BulkCollectionJob> BulkCollectionJobs { get; set; } = null!;
    public DbSet<Researcher> Researchers { get; set; } = null!;
    public DbSet<OrcidProfile> OrcidProfiles { get; set; } = null!;
    public DbSet<OrcidWork> OrcidWorks { get; set; } = null!;
    public DbSet<GoogleScholarProfile> GoogleScholarProfiles { get; set; } = null!;
    public DbSet<GoogleScholarWork> GoogleScholarWorks { get; set; } = null!;
    public DbSet<OpenAlexProfile> OpenAlexProfiles { get; set; } = null!;
    public DbSet<OpenAlexWork> OpenAlexWorks { get; set; } = null!;
    public DbSet<ScopusProfile> ScopusProfiles { get; set; } = null!;
    public DbSet<ScopusWork> ScopusWorks { get; set; } = null!;
    public DbSet<WebOfScienceProfile> WebOfScienceProfiles { get; set; } = null!;
    public DbSet<WebOfScienceWork> WebOfScienceWorks { get; set; } = null!;
    public DbSet<WebOfSciencePeerReview> WebOfSciencePeerReviews { get; set; } = null!;
    public DbSet<YoksisRecord> YoksisRecords { get; set; } = null!;
    public DbSet<YoksisCollectionSnapshot> YoksisCollectionSnapshots { get; set; } = null!;
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

    public AcademicDbContext(DbContextOptions<AcademicDbContext> options)
        : base(options)
    {
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
            entity.HasIndex(value => new { value.OccurredAtUtc, value.EventId });
            entity.HasIndex(value => new { value.PersonelId, value.OccurredAtUtc });
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
        modelBuilder.Entity<BulkCollectionBatch>(entity =>
        {
            entity.ToTable("BulkCollectionBatches", "bulk");
            entity.HasKey(batch => batch.Id);
            entity.Property(batch => batch.InputHash).HasMaxLength(64);
        });
        modelBuilder.Entity<BulkCollectionJob>(entity =>
        {
            entity.ToTable("BulkCollectionJobs", "bulk");
            entity.HasKey(job => job.Id);
            entity.Property(job => job.PersonelId).HasColumnName("PersonelID").HasMaxLength(200);
            entity.Property(job => job.Status).HasMaxLength(20);
            entity.Property(job => job.ResultMessage).HasMaxLength(1000);
            entity.HasOne<BulkCollectionBatch>().WithMany().HasForeignKey(job => job.BatchId)
                .OnDelete(DeleteBehavior.NoAction);
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

            entity.HasOne(researcher => researcher.ScopusProfile)
                .WithOne(profile => profile.Researcher)
                .HasForeignKey<ScopusProfile>(profile => profile.PersonelId)
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
        modelBuilder.Entity<YoksisCollectionSnapshot>(entity =>
        {
            entity.ToTable("YoksisCollectionSnapshots", "yoksis");
            entity.HasKey(snapshot => snapshot.PersonelId);
            entity.Property(snapshot => snapshot.PersonelId)
                .HasColumnName("PersonelID")
                .HasMaxLength(200)
                .ValueGeneratedNever();
            entity.Property(snapshot => snapshot.TcKimlikNoHash).HasMaxLength(64);
            entity.Property(snapshot => snapshot.ResponseJson);
            entity.HasOne(snapshot => snapshot.Researcher)
                .WithOne()
                .HasForeignKey<YoksisCollectionSnapshot>(snapshot => snapshot.PersonelId)
                .OnDelete(DeleteBehavior.Cascade);
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
            entity.HasOne<Researcher>()
                .WithMany()
                .HasForeignKey(x => x.PersonelId)
                .OnDelete(DeleteBehavior.Cascade);
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
            entity.Property(profile => profile.ActivitiesDetailsJson);
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

        modelBuilder.Entity<ScopusProfile>(entity =>
        {
            entity.ToTable("ScopusProfiles", "scopus");
            entity.HasKey(profile => profile.Id);
            entity.Property(profile => profile.ScopusAuthorId).HasMaxLength(20);
            entity.Property(profile => profile.PersonelId).HasColumnName("PersonelID").HasMaxLength(200);
            entity.Property(profile => profile.DisplayName).HasMaxLength(500);
            entity.Property(profile => profile.CurrentAffiliation).HasMaxLength(1000);
            entity.Property(profile => profile.RawDataJson).HasColumnType("nvarchar(max)");
            entity.Property(profile => profile.SearchPagesJson).HasColumnType("nvarchar(max)");
            entity.HasIndex(profile => profile.PersonelId).IsUnique();
            entity.HasIndex(profile => profile.ScopusAuthorId).IsUnique();
            entity.HasMany(profile => profile.Works)
                .WithOne(work => work.ScopusProfile)
                .HasForeignKey(work => work.ScopusProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ScopusWork>(entity =>
        {
            entity.ToTable("ScopusWorks", "scopus");
            entity.HasKey(work => work.Id);
            entity.Property(work => work.ScopusWorkId).HasMaxLength(100);
            entity.Property(work => work.Eid).HasMaxLength(100);
            entity.Property(work => work.Title).HasMaxLength(2000);
            entity.Property(work => work.Doi).HasMaxLength(500);
            entity.Property(work => work.WorkType).HasMaxLength(200);
            entity.Property(work => work.Authors).HasColumnType("nvarchar(max)");
            entity.Property(work => work.SourceName).HasMaxLength(2000);
            entity.Property(work => work.Url).HasMaxLength(2000);
            entity.Property(work => work.RawDataJson).HasColumnType("nvarchar(max)");
            entity.HasIndex(work => new { work.ScopusProfileId, work.ScopusWorkId }).IsUnique();
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

            entity.HasOne(summary => summary.CanonicalWork)
                .WithMany(work => work.PublicationSummaries)
                .HasForeignKey(summary => summary.CanonicalWorkId)
                .OnDelete(DeleteBehavior.NoAction);

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
            entity.HasOne<Researcher>()
                .WithMany()
                .HasForeignKey(observation => observation.PersonelId)
                .OnDelete(DeleteBehavior.NoAction);
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



    }
}

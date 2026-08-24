using Microsoft.EntityFrameworkCore;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data;

public sealed class AcademicDbContext : DbContext
{
    public DbSet<Researcher> Researchers { get; set; } = null!;
    public DbSet<OrcidProfile> OrcidProfiles { get; set; } = null!;
    public DbSet<OrcidWork> OrcidWorks { get; set; } = null!;
    public DbSet<WebOfScienceProfile> WebOfScienceProfiles { get; set; } = null!;
    public DbSet<WebOfScienceWork> WebOfScienceWorks { get; set; } = null!;
    public DbSet<WebOfSciencePeerReview> WebOfSciencePeerReviews { get; set; } = null!;
    public DbSet<AcademicWork> AcademicWorks { get; set; } = null!;
    public DbSet<PublicationSummary> PublicationSummaries { get; set; } = null!;
    public DbSet<PublicationDisplayApproval> PublicationDisplayApprovals { get; set; } = null!;

    public AcademicDbContext(DbContextOptions<AcademicDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Researcher>(entity =>
        {
            entity.ToTable("Researchers");
            entity.HasKey(researcher => researcher.Id);
            entity.Property(researcher => researcher.Orcid).HasMaxLength(19);
            entity.Property(researcher => researcher.WebOfScienceResearcherId)
                .HasMaxLength(20);

            entity.HasIndex(researcher => researcher.Orcid)
                .IsUnique()
                .HasFilter("[Orcid] IS NOT NULL");

            entity.HasIndex(researcher => researcher.WebOfScienceResearcherId)
                .IsUnique()
                .HasFilter("[WebOfScienceResearcherId] IS NOT NULL");

            entity.HasMany(researcher => researcher.AcademicWorks)
                .WithOne(work => work.Researcher)
                .HasForeignKey(work => work.ResearcherId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(researcher => researcher.OrcidProfile)
                .WithOne(profile => profile.Researcher)
                .HasForeignKey<OrcidProfile>(profile => profile.ResearcherId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(researcher => researcher.WebOfScienceProfile)
                .WithOne(profile => profile.Researcher)
                .HasForeignKey<WebOfScienceProfile>(profile => profile.ResearcherId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(researcher => researcher.PublicationSummaries)
                .WithOne(summary => summary.Researcher)
                .HasForeignKey(summary => summary.ResearcherId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(researcher => researcher.PublicationDisplayApprovals)
                .WithOne(approval => approval.Researcher)
                .HasForeignKey(approval => approval.ResearcherId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<OrcidProfile>(entity =>
        {
            entity.ToTable("OrcidProfiles");
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
            entity.HasIndex(profile => profile.ResearcherId).IsUnique();

            entity.HasMany(profile => profile.Works)
                .WithOne(work => work.OrcidProfile)
                .HasForeignKey(work => work.OrcidProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrcidWork>(entity =>
        {
            entity.ToTable("OrcidWorks");
            entity.HasKey(work => work.Id);
            entity.Property(work => work.Title).HasMaxLength(2000);
            entity.Property(work => work.Subtitle).HasMaxLength(2000);
            entity.Property(work => work.TranslatedTitle).HasMaxLength(2000);
            entity.Property(work => work.WorkType).HasMaxLength(100);
            entity.Property(work => work.JournalTitle).HasMaxLength(2000);
            entity.Property(work => work.Doi).HasMaxLength(500);
            entity.Property(work => work.Url).HasMaxLength(2000);
            entity.Property(work => work.Authors).HasMaxLength(4000);
            entity.Property(work => work.LanguageCode).HasMaxLength(20);
            entity.Property(work => work.CountryCode).HasMaxLength(20);
            entity.Property(work => work.SourceName).HasMaxLength(500);
            entity.Property(work => work.Visibility).HasMaxLength(50);
            entity.Property(work => work.Category).HasConversion<string>().HasMaxLength(50);
            entity.Property(work => work.CategorySource).HasConversion<string>().HasMaxLength(50);
            entity.HasIndex(work => new { work.OrcidProfileId, work.PutCode }).IsUnique();
        });

        modelBuilder.Entity<WebOfScienceProfile>(entity =>
        {
            entity.ToTable("WebOfScienceProfiles");
            entity.HasKey(profile => profile.Id);
            entity.Property(profile => profile.DisplayName).HasMaxLength(500);
            entity.Property(profile => profile.FirstName).HasMaxLength(250);
            entity.Property(profile => profile.LastName).HasMaxLength(250);
            entity.Property(profile => profile.Orcid).HasMaxLength(19);
            entity.Property(profile => profile.PrimaryOrganization).HasMaxLength(1000);
            entity.Property(profile => profile.PrimaryAddress).HasMaxLength(2000);
            entity.Property(profile => profile.PrimaryCountry).HasMaxLength(250);
            entity.Property(profile => profile.Departments).HasMaxLength(2000);
            entity.HasIndex(profile => profile.ResearcherId).IsUnique();

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
            entity.ToTable("WebOfScienceWorks");
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
            entity.ToTable("WebOfSciencePeerReviews");
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
            entity.ToTable("AcademicWorks");
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
            entity.Property(work => work.Authors).HasMaxLength(4000);
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
            entity.Property(work => work.SourceUrl).HasMaxLength(2000);
            entity.Property(work => work.OpenAccessStatus).HasMaxLength(50);
            entity.Property(work => work.OpenAccessUrl).HasMaxLength(2000);
            entity.Property(work => work.FullTextUrl).HasMaxLength(2000);
            entity.Property(work => work.License).HasMaxLength(100);
            entity.Property(work => work.Version).HasMaxLength(100);

            entity.HasIndex(work => work.ResearcherId);
            entity.HasIndex(work => new { work.ResearcherId, work.Provider });

        });

        modelBuilder.Entity<PublicationSummary>(entity =>
        {
            entity.ToTable("PublicationSummaries");
            entity.HasKey(summary => summary.Id);
            entity.Property(summary => summary.Fingerprint).HasMaxLength(64);
            entity.Property(summary => summary.Title).HasMaxLength(2000);
            entity.Property(summary => summary.Doi).HasMaxLength(500);
            entity.Property(summary => summary.Category)
                .HasConversion<string>()
                .HasMaxLength(50);
            entity.Property(summary => summary.Authors).HasMaxLength(4000);
            entity.Property(summary => summary.Keywords).HasMaxLength(4000);
            entity.Property(summary => summary.Topics).HasMaxLength(4000);
            entity.Property(summary => summary.Language).HasMaxLength(20);
            entity.Property(summary => summary.Publication).HasMaxLength(2000);
            entity.Property(summary => summary.Volume).HasMaxLength(100);
            entity.Property(summary => summary.Issue).HasMaxLength(100);
            entity.Property(summary => summary.FirstPage).HasMaxLength(100);
            entity.Property(summary => summary.LastPage).HasMaxLength(100);
            entity.Property(summary => summary.PublicationUrl).HasMaxLength(2000);
            entity.Property(summary => summary.PdfUrl).HasMaxLength(2000);
            entity.Property(summary => summary.Sources).HasMaxLength(200);

            entity.HasIndex(summary => summary.ResearcherId);
            entity.HasIndex(summary => new
                {
                    summary.ResearcherId,
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
            entity.ToTable("PublicationDisplayApprovals");
            entity.HasKey(approval => approval.Id);
            entity.HasIndex(approval => approval.ResearcherId);
            entity.HasIndex(approval => approval.PublicationSummaryId).IsUnique();
        });
    }
}

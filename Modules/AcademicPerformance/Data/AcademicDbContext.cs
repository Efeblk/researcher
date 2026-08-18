using Microsoft.EntityFrameworkCore;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Files;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data;

public sealed class AcademicDbContext : DbContext
{
    public DbSet<Researcher> Researchers { get; set; } = null!;
    public DbSet<OpenAlexData> OpenAlexProfiles { get; set; } = null!;
    public DbSet<OpenAlexWork> OpenAlexWorks { get; set; } = null!;
    public DbSet<GoogleScholarData> GoogleScholarProfiles { get; set; } = null!;
    public DbSet<GoogleScholarWork> GoogleScholarWorks { get; set; } = null!;
    public DbSet<GoogleScholarInterest> GoogleScholarInterests { get; set; } = null!;
    public DbSet<WebOfScienceData> WebOfScienceProfiles { get; set; } = null!;
    public DbSet<AcademicWork> AcademicWorks { get; set; } = null!;
    public DbSet<AcademicWorkFile> AcademicWorkFiles { get; set; } = null!;

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
            entity.Property(researcher => researcher.GoogleScholarId).HasMaxLength(100);
            entity.Property(researcher => researcher.WebOfScienceResearcherId).HasMaxLength(100);

            entity.HasIndex(researcher => researcher.Orcid)
                .IsUnique()
                .HasFilter("[Orcid] IS NOT NULL");

            entity.HasIndex(researcher => researcher.GoogleScholarId)
                .IsUnique()
                .HasFilter("[GoogleScholarId] IS NOT NULL");

            entity.HasIndex(researcher => researcher.WebOfScienceResearcherId)
                .IsUnique()
                .HasFilter("[WebOfScienceResearcherId] IS NOT NULL");

            entity.HasOne(researcher => researcher.OpenAlex)
                .WithOne(openAlex => openAlex.Researcher)
                .HasForeignKey<OpenAlexData>(openAlex => openAlex.ResearcherId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(researcher => researcher.GoogleScholar)
                .WithOne(googleScholar => googleScholar.Researcher)
                .HasForeignKey<GoogleScholarData>(googleScholar => googleScholar.ResearcherId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(researcher => researcher.WebOfScience)
                .WithOne(webOfScience => webOfScience.Researcher)
                .HasForeignKey<WebOfScienceData>(webOfScience => webOfScience.ResearcherId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(researcher => researcher.AcademicWorks)
                .WithOne(work => work.Researcher)
                .HasForeignKey(work => work.ResearcherId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OpenAlexData>(entity =>
        {
            entity.ToTable("OpenAlexProfiles");
            entity.HasKey(openAlex => openAlex.Id);
            entity.Property(openAlex => openAlex.AuthorId).HasMaxLength(100);
            entity.Property(openAlex => openAlex.DisplayName).HasMaxLength(500);

            entity.HasMany(openAlex => openAlex.Works)
                .WithOne(work => work.OpenAlexData)
                .HasForeignKey(work => work.OpenAlexDataId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OpenAlexWork>(entity =>
        {
            entity.ToTable("OpenAlexWorks");
            entity.HasKey(work => work.Id);
            entity.Property(work => work.WorkId).HasMaxLength(100);
            entity.Property(work => work.Title).HasMaxLength(2000);
            entity.Property(work => work.Doi).HasMaxLength(500);
            entity.Property(work => work.Type).HasMaxLength(100);
            entity.Property(work => work.Language).HasMaxLength(20);
            entity.Property(work => work.Authors).HasMaxLength(4000);
            entity.Property(work => work.Institutions).HasMaxLength(4000);
            entity.Property(work => work.Keywords).HasMaxLength(4000);
            entity.Property(work => work.Topics).HasMaxLength(4000);
            entity.Property(work => work.OpenAccessStatus).HasMaxLength(50);
            entity.Property(work => work.OpenAccessUrl).HasMaxLength(2000);
            entity.Property(work => work.FullTextUrl).HasMaxLength(2000);
            entity.Property(work => work.License).HasMaxLength(100);
            entity.Property(work => work.Version).HasMaxLength(100);
            entity.Property(work => work.Volume).HasMaxLength(100);
            entity.Property(work => work.Issue).HasMaxLength(100);
            entity.Property(work => work.FirstPage).HasMaxLength(100);
            entity.Property(work => work.LastPage).HasMaxLength(100);
            entity.Property(work => work.Category)
                .HasConversion<string>()
                .HasMaxLength(50);
            entity.Property(work => work.CategorySource)
                .HasConversion<string>()
                .HasMaxLength(50);
            entity.Property(work => work.SourceId).HasMaxLength(100);
            entity.Property(work => work.SourceName).HasMaxLength(2000);
            entity.Property(work => work.SourceType).HasMaxLength(100);
            entity.Property(work => work.SourceUrl).HasMaxLength(2000);
        });

        modelBuilder.Entity<GoogleScholarData>(entity =>
        {
            entity.ToTable("GoogleScholarProfiles");
            entity.HasKey(googleScholar => googleScholar.Id);
            entity.Property(googleScholar => googleScholar.ScholarId).HasMaxLength(100);
            entity.Property(googleScholar => googleScholar.Name).HasMaxLength(500);
            entity.Property(googleScholar => googleScholar.Email).HasMaxLength(500);
            entity.Property(googleScholar => googleScholar.Affiliations).HasMaxLength(2000);

            entity.HasMany(googleScholar => googleScholar.Works)
                .WithOne(work => work.GoogleScholarData)
                .HasForeignKey(work => work.GoogleScholarDataId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(googleScholar => googleScholar.Interests)
                .WithOne(interest => interest.GoogleScholarData)
                .HasForeignKey(interest => interest.GoogleScholarDataId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GoogleScholarWork>(entity =>
        {
            entity.ToTable("GoogleScholarWorks");
            entity.HasKey(work => work.Id);
            entity.Property(work => work.Title).HasMaxLength(2000);
            entity.Property(work => work.Link).HasMaxLength(2000);
            entity.Property(work => work.CitationId).HasMaxLength(500);
            entity.Property(work => work.Authors).HasMaxLength(4000);
            entity.Property(work => work.Publication).HasMaxLength(2000);
            entity.Property(work => work.Year).HasMaxLength(10);
            entity.Property(work => work.CitedByUrl).HasMaxLength(2000);
            entity.Property(work => work.CitedBySerpApiUrl).HasMaxLength(2000);
            entity.Property(work => work.CitesId).HasMaxLength(2000);
            entity.Property(work => work.Category)
                .HasConversion<string>()
                .HasMaxLength(50);
            entity.Property(work => work.CategorySource)
                .HasConversion<string>()
                .HasMaxLength(50);
        });

        modelBuilder.Entity<GoogleScholarInterest>(entity =>
        {
            entity.ToTable("GoogleScholarInterests");
            entity.HasKey(interest => interest.Id);
            entity.Property(interest => interest.Title).HasMaxLength(500);
        });

        modelBuilder.Entity<WebOfScienceData>(entity =>
        {
            entity.ToTable("WebOfScienceProfiles");
            entity.HasKey(webOfScience => webOfScience.Id);
            entity.Property(webOfScience => webOfScience.Rid).HasMaxLength(100);
            entity.Property(webOfScience => webOfScience.FullName).HasMaxLength(500);
            entity.Property(webOfScience => webOfScience.FirstName).HasMaxLength(500);
            entity.Property(webOfScience => webOfScience.LastName).HasMaxLength(500);
            entity.Property(webOfScience => webOfScience.PrimaryAffiliation).HasMaxLength(2000);
            entity.Property(webOfScience => webOfScience.Address).HasMaxLength(2000);
            entity.Property(webOfScience => webOfScience.Country).HasMaxLength(500);
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
            entity.Property(work => work.CitedByUrl).HasMaxLength(2000);
            entity.Property(work => work.CitedBySerpApiUrl).HasMaxLength(2000);
            entity.Property(work => work.CitesId).HasMaxLength(2000);
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

            entity.HasOne(work => work.PdfFile)
                .WithOne(file => file.AcademicWork)
                .HasForeignKey<AcademicWorkFile>(file => file.AcademicWorkId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AcademicWorkFile>(entity =>
        {
            entity.ToTable("AcademicWorkFiles");
            entity.HasKey(file => file.Id);
            entity.Property(file => file.SourceUrl).HasMaxLength(2000);
            entity.Property(file => file.RelativePath).HasMaxLength(1000);
            entity.Property(file => file.FileName).HasMaxLength(500);
            entity.Property(file => file.MimeType).HasMaxLength(200);
            entity.Property(file => file.Sha256).HasMaxLength(64);
            entity.Property(file => file.Status)
                .HasConversion<string>()
                .HasMaxLength(50);
            entity.Property(file => file.ErrorMessage).HasMaxLength(2000);

            entity.HasIndex(file => file.AcademicWorkId).IsUnique();
        });
    }
}

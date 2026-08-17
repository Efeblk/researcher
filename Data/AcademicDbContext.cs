using Microsoft.EntityFrameworkCore;

public sealed class AcademicDbContext : DbContext
{
    public DbSet<Researcher> Researchers { get; set; } = null!;
    public DbSet<OpenAlexData> OpenAlexProfiles { get; set; } = null!;
    public DbSet<OpenAlexWork> OpenAlexWorks { get; set; } = null!;
    public DbSet<GoogleScholarData> GoogleScholarProfiles { get; set; } = null!;
    public DbSet<GoogleScholarWork> GoogleScholarWorks { get; set; } = null!;
    public DbSet<GoogleScholarInterest> GoogleScholarInterests { get; set; } = null!;

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
            entity.Property(researcher => researcher.ScopusAuthorId).HasMaxLength(100);

            entity.HasIndex(researcher => researcher.Orcid)
                .IsUnique()
                .HasFilter("[Orcid] IS NOT NULL");

            entity.HasIndex(researcher => researcher.GoogleScholarId)
                .IsUnique()
                .HasFilter("[GoogleScholarId] IS NOT NULL");

            entity.HasOne(researcher => researcher.OpenAlex)
                .WithOne(openAlex => openAlex.Researcher)
                .HasForeignKey<OpenAlexData>(openAlex => openAlex.ResearcherId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(researcher => researcher.GoogleScholar)
                .WithOne(googleScholar => googleScholar.Researcher)
                .HasForeignKey<GoogleScholarData>(googleScholar => googleScholar.ResearcherId)
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
            entity.Property(work => work.Title).HasMaxLength(2000);
            entity.Property(work => work.Doi).HasMaxLength(500);
            entity.Property(work => work.Type).HasMaxLength(100);
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
        });

        modelBuilder.Entity<GoogleScholarInterest>(entity =>
        {
            entity.ToTable("GoogleScholarInterests");
            entity.HasKey(interest => interest.Id);
            entity.Property(interest => interest.Title).HasMaxLength(500);
        });
    }
}

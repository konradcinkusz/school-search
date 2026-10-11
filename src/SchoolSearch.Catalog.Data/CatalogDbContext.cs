using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SchoolSearch.Catalog.Data.Entities;

namespace SchoolSearch.Catalog.Data;

/// <summary>
/// The catalog database (catalogdb). Owned by the Api service only (P3).
/// Schema changes go through provider-specific migrations (P4); reference data is
/// not seeded here (no HasData).
/// </summary>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<School> Schools => Set<School>();

    public DbSet<ClassCohort> ClassCohorts => Set<ClassCohort>();

    public DbSet<ExamResult> ExamResults => Set<ExamResult>();

    public DbSet<NationalBaselineRow> NationalBaselines => Set<NationalBaselineRow>();

    public DbSet<GoverningBodyType> GoverningBodyTypes => Set<GoverningBodyType>();

    public DbSet<ImportRun> ImportRuns => Set<ImportRun>();

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    public DbSet<CorrectionRequest> CorrectionRequests => Set<CorrectionRequest>();

    public DbSet<CatchmentCoverage> CatchmentCoverages => Set<CatchmentCoverage>();

    public DbSet<CatchmentStreet> CatchmentStreets => Set<CatchmentStreet>();

    public DbSet<Preschool> Preschools => Set<Preschool>();

    public DbSet<PreschoolFacts> PreschoolFacts => Set<PreschoolFacts>();

    /// <summary>
    /// The Npgsql migrations are hand-written and no model snapshot exists yet (see
    /// Migrations/Npgsql/README.md). EF 9+ would otherwise make MigrateAsync fail with
    /// PendingModelChangesWarning. Set here, not in the kernel, because the kernel's
    /// AddDatabaseContext has no configure callback. Remove once the snapshot is generated.
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(warnings =>
            warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<School>(entity =>
        {
            entity.ToTable("Schools");

            entity.HasKey(s => s.IdPodmiot);
            entity.Property(s => s.IdPodmiot).ValueGeneratedNever();

            entity.Property(s => s.Name).HasMaxLength(300).IsRequired();
            entity.Property(s => s.Slug).HasMaxLength(120).IsRequired();
            entity.HasIndex(s => s.Slug).IsUnique();

            entity.Property(s => s.GminaTeryt).HasMaxLength(7);
            entity.HasIndex(s => s.GminaTeryt);
            entity.Property(s => s.GminaName).HasMaxLength(120);

            // Same column as Phase A (TypeOfGoverningBody): existing data and the InitialCreate schema stay valid.
            entity.Property(s => s.GoverningBodyTypeCode).HasColumnName("TypeOfGoverningBody");
            entity.Ignore(s => s.TypeOfGoverningBody);
            entity.Property(s => s.IsPublic).HasDefaultValue(false);
            entity.Property(s => s.IsSpecial).HasDefaultValue(false);
            entity.Property(s => s.FaithAffiliationDerived).HasDefaultValue(false);
            entity.Property(s => s.FaithAffiliationDeclared);
            entity.Property(s => s.Latitude);
            entity.Property(s => s.Longitude);

            entity.OwnsOne(s => s.Provenance, provenance =>
            {
                provenance.Property(p => p.Source).HasMaxLength(200).IsRequired();
                provenance.Property(p => p.SourceUrl).HasMaxLength(500).IsRequired();
                provenance.Property(p => p.AsOf).IsRequired();
                provenance.Property(p => p.Method).HasMaxLength(500).IsRequired();
            });

            entity.OwnsOne(s => s.CoordinatesProvenance, provenance =>
            {
                provenance.Property(p => p.Source).HasMaxLength(200).IsRequired();
                provenance.Property(p => p.SourceUrl).HasMaxLength(500).IsRequired();
                provenance.Property(p => p.AsOf).IsRequired();
                provenance.Property(p => p.Method).HasMaxLength(500).IsRequired();
            });
        });

        modelBuilder.Entity<ClassCohort>(entity =>
        {
            entity.ToTable("ClassCohorts");

            entity.HasKey(c => new { c.SchoolId, c.SchoolYear, c.ClassLevel });
            entity.Property(c => c.ClassLevel).HasMaxLength(32).IsRequired();
            entity.Property(c => c.Pupils);
            entity.Property(c => c.Oddzialy);

            entity.OwnsOne(c => c.Provenance, provenance =>
            {
                provenance.Property(p => p.Source).HasMaxLength(200).IsRequired();
                provenance.Property(p => p.SourceUrl).HasMaxLength(500).IsRequired();
                provenance.Property(p => p.AsOf).IsRequired();
                provenance.Property(p => p.Method).HasMaxLength(500).IsRequired();
            });
        });

        modelBuilder.Entity<ExamResult>(entity =>
        {
            entity.ToTable("ExamResults");

            entity.HasKey(e => new { e.SchoolId, e.ExamYear, e.Subject });
            entity.Property(e => e.Subject).HasMaxLength(32).IsRequired();
            entity.Property(e => e.MeanPercent);
            entity.Property(e => e.PupilsSat);

            entity.OwnsOne(e => e.Provenance, provenance =>
            {
                provenance.Property(p => p.Source).HasMaxLength(200).IsRequired();
                provenance.Property(p => p.SourceUrl).HasMaxLength(500).IsRequired();
                provenance.Property(p => p.AsOf).IsRequired();
                provenance.Property(p => p.Method).HasMaxLength(500).IsRequired();
            });
        });

        modelBuilder.Entity<NationalBaselineRow>(entity =>
        {
            entity.ToTable("NationalBaselines");

            entity.HasKey(n => new { n.ExamYear, n.Subject });
            entity.Property(n => n.Subject).HasMaxLength(32).IsRequired();
            entity.Property(n => n.NationalMeanPercent);
            entity.Property(n => n.PupilStdDevPoints);

            entity.OwnsOne(n => n.Provenance, provenance =>
            {
                provenance.Property(p => p.Source).HasMaxLength(200).IsRequired();
                provenance.Property(p => p.SourceUrl).HasMaxLength(500).IsRequired();
                provenance.Property(p => p.AsOf).IsRequired();
                provenance.Property(p => p.Method).HasMaxLength(500).IsRequired();
            });
        });

        modelBuilder.Entity<GoverningBodyType>(entity =>
        {
            entity.ToTable("GoverningBodyTypes");

            entity.HasKey(g => g.Code);
            entity.Property(g => g.Code).ValueGeneratedNever();
            entity.Property(g => g.Label).HasMaxLength(200);
            entity.Property(g => g.LabelVerified).HasDefaultValue(false);
            entity.Property(g => g.IsNaturalPerson).HasDefaultValue(false);

            entity.OwnsOne(g => g.Provenance, provenance =>
            {
                provenance.Property(p => p.Source).HasMaxLength(200).IsRequired();
                provenance.Property(p => p.SourceUrl).HasMaxLength(500).IsRequired();
                provenance.Property(p => p.AsOf).IsRequired();
                provenance.Property(p => p.Method).HasMaxLength(500).IsRequired();
            });
        });

        modelBuilder.Entity<ImportRun>(entity =>
        {
            entity.ToTable("ImportRuns");

            entity.HasKey(r => r.Id);
            entity.Property(r => r.Id).ValueGeneratedNever();
            entity.Property(r => r.Kind).HasMaxLength(64).IsRequired();
            entity.Property(r => r.StartedAt).IsRequired();
            entity.Property(r => r.FinishedAt);
            entity.Property(r => r.RowsRead);
            entity.Property(r => r.RowsWritten);
            entity.Property(r => r.RowsRejected);
            entity.Property(r => r.SchemaVersion).HasMaxLength(32).IsRequired();
            entity.HasIndex(r => new { r.Kind, r.StartedAt });

            entity.OwnsOne(r => r.Provenance, provenance =>
            {
                provenance.Property(p => p.Source).HasMaxLength(200).IsRequired();
                provenance.Property(p => p.SourceUrl).HasMaxLength(500).IsRequired();
                provenance.Property(p => p.AsOf).IsRequired();
                provenance.Property(p => p.Method).HasMaxLength(500).IsRequired();
            });
        });

        modelBuilder.Entity<ApiKey>(entity =>
        {
            entity.ToTable("ApiKeys");

            entity.HasKey(k => k.Id);
            entity.Property(k => k.Id).ValueGeneratedNever();
            entity.Property(k => k.KeyHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(k => k.KeyHash).IsUnique();
            entity.Property(k => k.Label).HasMaxLength(80).IsRequired();
            entity.Property(k => k.Tier).HasMaxLength(16).IsRequired();
            entity.Property(k => k.CreatedAt).IsRequired();
            entity.Property(k => k.RevokedAt);
        });

        modelBuilder.Entity<CorrectionRequest>(entity =>
        {
            entity.ToTable("CorrectionRequests");

            entity.HasKey(c => c.Id);
            entity.Property(c => c.Id).ValueGeneratedNever();
            entity.Property(c => c.PublicToken).HasMaxLength(64).IsRequired();
            entity.HasIndex(c => c.PublicToken).IsUnique();
            entity.Property(c => c.SchoolId);
            entity.Property(c => c.Field).HasMaxLength(40).IsRequired();
            entity.Property(c => c.Message).HasMaxLength(2000).IsRequired();
            entity.Property(c => c.ContactEmail).HasMaxLength(200);
            entity.Property(c => c.Status).HasMaxLength(16).IsRequired();
            entity.Property(c => c.StatusReason).HasMaxLength(500);
            entity.Property(c => c.CreatedAt).IsRequired();
            entity.Property(c => c.UpdatedAt);
            entity.HasIndex(c => new { c.Status, c.CreatedAt });
        });

        modelBuilder.Entity<CatchmentCoverage>(entity =>
        {
            entity.ToTable("CatchmentCoverages");

            entity.HasKey(c => c.GminaTeryt);
            entity.Property(c => c.GminaTeryt).HasMaxLength(7).ValueGeneratedNever();
            entity.Property(c => c.State).HasMaxLength(16).IsRequired();
            entity.Property(c => c.ResolutionUrl).HasMaxLength(500);
            entity.Property(c => c.ResolutionDate);

            entity.OwnsOne(c => c.Provenance, provenance =>
            {
                provenance.Property(p => p.Source).HasMaxLength(200).IsRequired();
                provenance.Property(p => p.SourceUrl).HasMaxLength(500).IsRequired();
                provenance.Property(p => p.AsOf).IsRequired();
                provenance.Property(p => p.Method).HasMaxLength(500).IsRequired();
            });
        });

        modelBuilder.Entity<CatchmentStreet>(entity =>
        {
            entity.ToTable("CatchmentStreets");

            entity.HasKey(c => c.Id);
            entity.Property(c => c.Id).ValueGeneratedOnAdd();
            entity.Property(c => c.GminaTeryt).HasMaxLength(7).IsRequired();
            entity.Property(c => c.SchoolId);
            entity.Property(c => c.StreetNormalized).HasMaxLength(200).IsRequired();
            entity.Property(c => c.NumberFrom);
            entity.Property(c => c.NumberTo);
            entity.Property(c => c.Parity).HasMaxLength(8);
            entity.HasIndex(c => new { c.GminaTeryt, c.StreetNormalized });

            entity.OwnsOne(c => c.Provenance, provenance =>
            {
                provenance.Property(p => p.Source).HasMaxLength(200).IsRequired();
                provenance.Property(p => p.SourceUrl).HasMaxLength(500).IsRequired();
                provenance.Property(p => p.AsOf).IsRequired();
                provenance.Property(p => p.Method).HasMaxLength(500).IsRequired();
            });
        });

        modelBuilder.Entity<Preschool>(entity =>
        {
            entity.ToTable("Preschools");

            entity.HasKey(p => p.IdPodmiot);
            entity.Property(p => p.IdPodmiot).ValueGeneratedNever();

            entity.Property(p => p.Name).HasMaxLength(300).IsRequired();
            entity.Property(p => p.Slug).HasMaxLength(200).IsRequired();
            entity.HasIndex(p => p.Slug).IsUnique();

            entity.Property(p => p.GminaTeryt).HasMaxLength(7);
            entity.HasIndex(p => p.GminaTeryt);
            entity.Property(p => p.GminaName).HasMaxLength(120);
            entity.Property(p => p.GoverningBodyTypeCode);
            entity.Property(p => p.IsPublic).HasDefaultValue(false);
            entity.HasIndex(p => p.IsPublic);
            entity.Property(p => p.Street).HasMaxLength(200);
            entity.Property(p => p.PostalCode).HasMaxLength(10);
            entity.Property(p => p.City).HasMaxLength(120);
            entity.Property(p => p.Latitude);
            entity.Property(p => p.Longitude);

            entity.OwnsOne(p => p.Provenance, provenance =>
            {
                provenance.Property(p => p.Source).HasMaxLength(200).IsRequired();
                provenance.Property(p => p.SourceUrl).HasMaxLength(500).IsRequired();
                provenance.Property(p => p.AsOf).IsRequired();
                provenance.Property(p => p.Method).HasMaxLength(500).IsRequired();
            });

            entity.HasOne(p => p.Facts)
                .WithOne()
                .HasForeignKey<PreschoolFacts>(f => f.PreschoolId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PreschoolFacts>(entity =>
        {
            entity.ToTable("PreschoolFacts", table =>
            {
                table.HasCheckConstraint(
                    "CK_PreschoolFacts_MonthlyTuitionMin_NonNegative",
                    "\"MonthlyTuitionMinPln\" >= 0");
                table.HasCheckConstraint(
                    "CK_PreschoolFacts_MonthlyTuitionMax_NonNegative",
                    "\"MonthlyTuitionMaxPln\" >= 0");
                table.HasCheckConstraint(
                    "CK_PreschoolFacts_MonthlyTuition_MinNotAboveMax",
                    "\"MonthlyTuitionMinPln\" <= \"MonthlyTuitionMaxPln\"");
                table.HasCheckConstraint(
                    "CK_PreschoolFacts_MonthlyMeal_NonNegative",
                    "\"MonthlyMealPln\" >= 0");
                table.HasCheckConstraint(
                    "CK_PreschoolFacts_FreePlaces_NonNegative",
                    "\"FreePlaces\" >= 0");
                table.HasCheckConstraint(
                    "CK_PreschoolFacts_Confidence",
                    "\"Confidence\" IN ('reported', 'unverified')");
            });

            entity.HasKey(f => f.PreschoolId);
            entity.Property(f => f.PreschoolId).ValueGeneratedNever();

            entity.Property(f => f.MonthlyTuitionMinPln).HasPrecision(10, 2);
            entity.Property(f => f.MonthlyTuitionMaxPln).HasPrecision(10, 2);
            entity.Property(f => f.MonthlyMealPln).HasPrecision(10, 2);
            entity.Property(f => f.OpensAt);
            entity.Property(f => f.ClosesAt);
            entity.Property(f => f.FreePlaces);
            entity.Property(f => f.FreePlacesAsOf);
            entity.Property(f => f.Confidence).HasMaxLength(16).HasDefaultValue("reported").IsRequired();

            entity.OwnsOne(f => f.Provenance, provenance =>
            {
                provenance.Property(p => p.Source).HasMaxLength(200).IsRequired();
                provenance.Property(p => p.SourceUrl).HasMaxLength(500).IsRequired();
                provenance.Property(p => p.AsOf).IsRequired();
                provenance.Property(p => p.Method).HasMaxLength(500).IsRequired();
            });
        });
    }
}

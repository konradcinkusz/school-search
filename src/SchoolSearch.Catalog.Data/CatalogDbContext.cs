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
    }
}

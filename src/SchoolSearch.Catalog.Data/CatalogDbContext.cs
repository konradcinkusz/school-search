using Microsoft.EntityFrameworkCore;
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

            entity.Property(s => s.TypeOfGoverningBody);
            entity.Property(s => s.IsPublic).HasDefaultValue(false);

            entity.OwnsOne(s => s.Provenance, provenance =>
            {
                provenance.Property(p => p.Source).HasMaxLength(200).IsRequired();
                provenance.Property(p => p.SourceUrl).HasMaxLength(500).IsRequired();
                provenance.Property(p => p.AsOf).IsRequired();
                provenance.Property(p => p.Method).HasMaxLength(500).IsRequired();
            });
        });
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SchoolSearch.Billing.Data;

/// <summary>
/// The billing database (billingdb). Owned by the Billing service only (P3). Schema changes go through
/// provider-specific migrations (P4); no reference data is seeded here (plans come from configuration).
/// </summary>
public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public DbSet<SchoolClaim> SchoolClaims => Set<SchoolClaim>();

    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();

    public DbSet<PremiumProfile> PremiumProfiles => Set<PremiumProfile>();

    public DbSet<Lead> Leads => Set<Lead>();

    /// <summary>
    /// The Npgsql migration is hand-written and no model snapshot exists yet (EF 9+ would otherwise fail
    /// MigrateAsync with PendingModelChangesWarning). Remove once the snapshot is generated, as in Reviews.Data.
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(warnings =>
            warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SchoolClaim>(entity =>
        {
            entity.ToTable("SchoolClaims", table =>
            {
                table.HasCheckConstraint("CK_SchoolClaims_Status", "\"Status\" IN ('pending', 'approved', 'rejected', 'revoked')");
            });

            entity.HasKey(claim => claim.Id);
            entity.Property(claim => claim.SchoolSlug).HasMaxLength(200).IsRequired();
            entity.Property(claim => claim.OwnerHash).HasMaxLength(64).IsRequired();
            entity.Property(claim => claim.RoleDescription).HasMaxLength(120).IsRequired();
            entity.Property(claim => claim.Status).HasMaxLength(16).IsRequired().HasDefaultValue(ClaimStatuses.Pending);
            entity.Property(claim => claim.DecidedBy).HasMaxLength(64);
            entity.Property(claim => claim.Notes).HasMaxLength(500);

            // One claim row per owner and school. A rejected claim is re-opened in place.
            entity.HasIndex(claim => new { claim.SchoolSlug, claim.OwnerHash })
                .IsUnique()
                .HasDatabaseName("IX_SchoolClaims_School_Owner");
            entity.HasIndex(claim => claim.Status).HasDatabaseName("IX_SchoolClaims_Status");
        });

        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.ToTable("Subscriptions", table =>
            {
                table.HasCheckConstraint("CK_Subscriptions_Status", "\"Status\" IN ('pending', 'active', 'canceling', 'expired')");
            });

            entity.HasKey(subscription => subscription.Id);
            entity.Property(subscription => subscription.SchoolSlug).HasMaxLength(200).IsRequired();
            entity.Property(subscription => subscription.OwnerHash).HasMaxLength(64).IsRequired();
            entity.Property(subscription => subscription.PlanCode).HasMaxLength(50).IsRequired();
            entity.Property(subscription => subscription.Status).HasMaxLength(16).IsRequired().HasDefaultValue(SubscriptionStatuses.Pending);
            entity.Property(subscription => subscription.CancelAtPeriodEnd).HasDefaultValue(false);
            entity.Property(subscription => subscription.ProviderRef).HasMaxLength(200);

            // One subscription row per school: a re-purchase updates it.
            entity.HasIndex(subscription => subscription.SchoolSlug)
                .IsUnique()
                .HasDatabaseName("IX_Subscriptions_SchoolSlug");
            entity.HasIndex(subscription => subscription.CurrentPeriodEnd)
                .HasDatabaseName("IX_Subscriptions_CurrentPeriodEnd");
        });

        modelBuilder.Entity<WebhookEvent>(entity =>
        {
            entity.ToTable("WebhookEvents", table =>
            {
                table.HasCheckConstraint("CK_WebhookEvents_Outcome", "\"Outcome\" IN ('applied', 'ignored', 'unknown_type', 'order_synthesised')");
            });

            entity.HasKey(webhookEvent => webhookEvent.EventId);
            entity.Property(webhookEvent => webhookEvent.EventId).HasMaxLength(200);
            entity.Property(webhookEvent => webhookEvent.Type).HasMaxLength(100).IsRequired();
            entity.Property(webhookEvent => webhookEvent.Outcome).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<PremiumProfile>(entity =>
        {
            entity.ToTable("PremiumProfiles", table =>
            {
                table.HasCheckConstraint("CK_PremiumProfiles_WebsiteUrl", "\"WebsiteUrl\" IS NULL OR \"WebsiteUrl\" LIKE 'https://%'");
            });

            entity.HasKey(profile => profile.SchoolSlug);
            entity.Property(profile => profile.SchoolSlug).HasMaxLength(200);
            entity.Property(profile => profile.Headline).HasMaxLength(120).IsRequired();
            entity.Property(profile => profile.Description).HasMaxLength(1500).IsRequired();
            entity.Property(profile => profile.WebsiteUrl).HasMaxLength(300);
            entity.Property(profile => profile.IsLocked).HasDefaultValue(false);
            entity.Property(profile => profile.LockedReason).HasMaxLength(100);
        });

        modelBuilder.Entity<Lead>(entity =>
        {
            entity.ToTable("Leads", table =>
            {
                table.HasCheckConstraint("CK_Leads_Status", "\"Status\" IN ('new', 'read')");
            });

            entity.HasKey(lead => lead.Id);
            entity.Property(lead => lead.SchoolSlug).HasMaxLength(200).IsRequired();
            entity.Property(lead => lead.ParentEmail).HasMaxLength(200).IsRequired();
            entity.Property(lead => lead.Message).HasMaxLength(1000).IsRequired();
            entity.Property(lead => lead.ConsentVersion).HasMaxLength(50).IsRequired();
            entity.Property(lead => lead.Status).HasMaxLength(16).IsRequired().HasDefaultValue(LeadStatuses.New);

            // Purge pass (LeadPurgeService) scans by expiry; the school inbox reads by school and date.
            entity.HasIndex(lead => lead.ExpiresAt).HasDatabaseName("IX_Leads_ExpiresAt");
            entity.HasIndex(lead => new { lead.SchoolSlug, lead.CreatedAt }).HasDatabaseName("IX_Leads_School_CreatedAt");
        });
    }
}

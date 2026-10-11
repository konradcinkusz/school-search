using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SchoolSearch.Reviews.Data;

/// <summary>
/// The reviews database (reviewsdb). Owned by the Reviews service only (P3). Schema changes go through
/// provider-specific migrations (P4); no reference data is seeded here.
/// </summary>
public sealed class ReviewsDbContext(DbContextOptions<ReviewsDbContext> options) : DbContext(options)
{
    public DbSet<Review> Reviews => Set<Review>();

    public DbSet<ReviewReply> ReviewReplies => Set<ReviewReply>();

    public DbSet<Notice> Notices => Set<Notice>();

    public DbSet<ModerationDecision> ModerationDecisions => Set<ModerationDecision>();

    public DbSet<Complaint> Complaints => Set<Complaint>();

    public DbSet<ModerationTriage> ModerationTriages => Set<ModerationTriage>();

    /// <summary>
    /// The Npgsql migration is hand-written and no model snapshot exists yet (EF 9+ would otherwise fail
    /// MigrateAsync with PendingModelChangesWarning). Remove once the snapshot is generated, as in Catalog.Data.
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(warnings =>
            warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Review>(entity =>
        {
            entity.ToTable("Reviews", table =>
            {
                table.HasCheckConstraint("CK_Reviews_Rating", "\"Rating\" BETWEEN 1 AND 5");
                table.HasCheckConstraint("CK_Reviews_Status", "\"Status\" IN ('pending', 'published', 'rejected', 'removed')");
            });

            entity.HasKey(r => r.Id);
            entity.Property(r => r.SchoolSlug).HasMaxLength(200).IsRequired();
            entity.Property(r => r.AuthorHash).HasMaxLength(64).IsRequired();
            entity.Property(r => r.Rating);
            entity.Property(r => r.Title).HasMaxLength(120).IsRequired();
            entity.Property(r => r.Body).HasMaxLength(4000).IsRequired();
            entity.Property(r => r.SchoolYearFrom);
            entity.Property(r => r.Status).HasMaxLength(16).IsRequired().HasDefaultValue(ReviewStatuses.Pending);
            entity.Property(r => r.TermsVersion).HasMaxLength(32).IsRequired();

            // One review per author, school and school year.
            entity.HasIndex(r => new { r.SchoolSlug, r.AuthorHash, r.SchoolYearFrom })
                .IsUnique()
                .HasDatabaseName("IX_Reviews_School_Author_Year");
            entity.HasIndex(r => new { r.SchoolSlug, r.Status, r.PublishedAt })
                .HasDatabaseName("IX_Reviews_School_Status_PublishedAt");
            entity.HasIndex(r => r.AuthorHash).HasDatabaseName("IX_Reviews_AuthorHash");

            entity.HasOne(r => r.Reply)
                .WithOne()
                .HasForeignKey<ReviewReply>(reply => reply.ReviewId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReviewReply>(entity =>
        {
            entity.ToTable("ReviewReplies", table =>
            {
                table.HasCheckConstraint("CK_ReviewReplies_Status", "\"Status\" IN ('pending', 'published', 'rejected')");
                table.HasCheckConstraint("CK_ReviewReplies_VerificationState", "\"VerificationState\" IN ('unverified', 'verified_by_moderator')");
            });

            entity.HasKey(reply => reply.ReviewId);
            entity.Property(reply => reply.Body).HasMaxLength(2000).IsRequired();
            entity.Property(reply => reply.RepresentativeLabel).HasMaxLength(120).IsRequired();
            entity.Property(reply => reply.VerificationState).HasMaxLength(32).IsRequired().HasDefaultValue(VerificationStates.Unverified);
            entity.Property(reply => reply.AuthorHash).HasMaxLength(64).IsRequired();
            entity.Property(reply => reply.Status).HasMaxLength(16).IsRequired().HasDefaultValue(ReplyStatuses.Pending);
        });

        modelBuilder.Entity<Notice>(entity =>
        {
            entity.ToTable("Notices", table =>
            {
                table.HasCheckConstraint("CK_Notices_Category", "\"Category\" IN ('illegal_content', 'personal_data', 'defamation', 'other')");
                table.HasCheckConstraint("CK_Notices_Status", "\"Status\" IN ('received', 'decided')");
                table.HasCheckConstraint("CK_Notices_GoodFaith", "\"GoodFaithConfirmed\" = TRUE");
            });

            entity.HasKey(notice => notice.Id);
            entity.Property(notice => notice.ContentReference).HasMaxLength(500).IsRequired();
            entity.Property(notice => notice.Explanation).HasMaxLength(3000).IsRequired();
            entity.Property(notice => notice.Category).HasMaxLength(32).IsRequired();
            entity.Property(notice => notice.SubmitterName).HasMaxLength(120);
            entity.Property(notice => notice.SubmitterEmail).HasMaxLength(200);
            entity.Property(notice => notice.Status).HasMaxLength(16).IsRequired().HasDefaultValue(NoticeStatuses.Received);
            entity.Property(notice => notice.AckToken).HasMaxLength(64).IsRequired();
            entity.HasIndex(notice => notice.AckToken).IsUnique().HasDatabaseName("IX_Notices_AckToken");
            entity.HasIndex(notice => notice.ReviewId).HasDatabaseName("IX_Notices_ReviewId");

            entity.HasOne(notice => notice.Decision)
                .WithOne()
                .HasForeignKey<ModerationDecision>(decision => decision.NoticeId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ModerationDecision>(entity =>
        {
            entity.ToTable("ModerationDecisions", table =>
            {
                table.HasCheckConstraint("CK_ModerationDecisions_Action", "\"Action\" IN ('remove', 'restrict', 'keep', 'publish', 'reject')");
                table.HasCheckConstraint("CK_ModerationDecisions_NotAutomated", "\"Automated\" = FALSE");
            });

            entity.HasKey(decision => decision.Id);
            entity.Property(decision => decision.Action).HasMaxLength(16).IsRequired();
            entity.Property(decision => decision.LegalOrTermsGround).HasMaxLength(500).IsRequired();
            entity.Property(decision => decision.Facts).HasMaxLength(4000).IsRequired();
            entity.Property(decision => decision.Automated).HasDefaultValue(false);
            entity.Property(decision => decision.RedressInfo).HasMaxLength(1000).IsRequired();
            entity.Property(decision => decision.DecidedBy).HasMaxLength(64).IsRequired();
            entity.HasIndex(decision => decision.ReviewId).HasDatabaseName("IX_ModerationDecisions_ReviewId");
        });

        modelBuilder.Entity<Complaint>(entity =>
        {
            entity.ToTable("Complaints", table =>
            {
                table.HasCheckConstraint("CK_Complaints_Status", "\"Status\" IN ('received', 'upheld', 'reversed')");
            });

            entity.HasKey(complaint => complaint.Id);
            entity.Property(complaint => complaint.Explanation).HasMaxLength(3000).IsRequired();
            entity.Property(complaint => complaint.AckToken).HasMaxLength(64).IsRequired();
            entity.Property(complaint => complaint.Status).HasMaxLength(16).IsRequired().HasDefaultValue(ComplaintStatuses.Received);
            entity.HasIndex(complaint => complaint.AckToken).IsUnique().HasDatabaseName("IX_Complaints_AckToken");
            entity.HasIndex(complaint => complaint.DecisionId).HasDatabaseName("IX_Complaints_DecisionId");

            entity.HasOne<ModerationDecision>()
                .WithMany()
                .HasForeignKey(complaint => complaint.DecisionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ModerationTriage>(entity =>
        {
            entity.ToTable("ModerationTriages", table =>
            {
                table.HasCheckConstraint("CK_ModerationTriages_Label", "\"Label\" IN ('ok', 'needs_attention', 'likely_personal_data', 'likely_abusive', 'unclear')");
            });

            entity.HasKey(triage => triage.ReviewId);
            entity.Property(triage => triage.Label).HasMaxLength(32).IsRequired();
            entity.Property(triage => triage.Note).HasMaxLength(300).IsRequired();
            entity.Property(triage => triage.Model).HasMaxLength(100).IsRequired();

            entity.HasOne<Review>()
                .WithOne()
                .HasForeignKey<ModerationTriage>(triage => triage.ReviewId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

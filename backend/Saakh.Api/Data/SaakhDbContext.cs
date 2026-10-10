using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Saakh.Api.Domain;

namespace Saakh.Api.Data;

public class SaakhDbContext : IdentityDbContext<AppUser, AppRole, Guid>
{
    public SaakhDbContext(DbContextOptions<SaakhDbContext> options) : base(options)
    {
    }

    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<CategorySubType> CategorySubTypes => Set<CategorySubType>();
    public DbSet<Interest> Interests => Set<Interest>();
    public DbSet<Deal> Deals => Set<Deal>();
    public DbSet<DealStateHistory> DealStateHistories => Set<DealStateHistory>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Rating> Ratings => Set<Rating>();
    public DbSet<EvidenceDocument> EvidenceDocuments => Set<EvidenceDocument>();
    public DbSet<Admin> Admins => Set<Admin>();
    public DbSet<AdminActionLog> AdminActionLogs => Set<AdminActionLog>();
    public DbSet<ResumeRequest> ResumeRequests => Set<ResumeRequest>();
    public DbSet<DealProposal> DealProposals => Set<DealProposal>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<AppUser>(e =>
        {
            e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            e.HasOne(x => x.Profile).WithOne(p => p.User)
                .HasForeignKey<Profile>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Admin).WithOne(a => a.User)
                .HasForeignKey<Admin>(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.Property(x => x.Token).HasMaxLength(256).IsRequired();
            e.HasIndex(x => x.Token).IsUnique();
            e.Property(x => x.ReplacedByToken).HasMaxLength(256);
            e.HasOne(x => x.User).WithMany(u => u.RefreshTokens)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CategorySubType>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(64).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(128).IsRequired();
            e.Property(x => x.DefaultUnit).HasMaxLength(32).IsRequired();
            e.HasIndex(x => x.Key).IsUnique();
        });

        b.Entity<Profile>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Gstin).HasMaxLength(15);
            e.Property(x => x.GstinLegalName).HasMaxLength(200);
            e.Property(x => x.RejectionReason).HasMaxLength(1000);
            e.Property(x => x.Country).HasMaxLength(100).IsRequired();
            e.Property(x => x.State).HasMaxLength(100).IsRequired();
            e.Property(x => x.District).HasMaxLength(100).IsRequired();
            e.Property(x => x.CapacityUnit).HasMaxLength(32).IsRequired();
            e.Property(x => x.OwnTradeDescription).HasMaxLength(500);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.CapacityMin).HasPrecision(18, 2);
            e.Property(x => x.CapacityMax).HasPrecision(18, 2);

            // Two Profiles (one Lender, one Seeker) may share a GSTIN — that soft-link is how a
            // counterparty sees they belong to the same business (spec §4). So: indexed, not unique.
            e.HasIndex(x => x.Gstin);
            e.HasIndex(x => new { x.Role, x.VerificationStatus, x.AvailabilityStatus });
            e.HasIndex(x => new { x.State, x.District });

            e.HasOne(x => x.CategorySubType).WithMany()
                .HasForeignKey(x => x.CategorySubTypeId).OnDelete(DeleteBehavior.SetNull);

            e.Ignore(x => x.IsDiscoverable);
            e.Ignore(x => x.IsFrozen);
        });

        b.Entity<Interest>(e =>
        {
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasOne(x => x.FromProfile).WithMany(p => p.SentInterests)
                .HasForeignKey(x => x.FromProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ToProfile).WithMany(p => p.ReceivedInterests)
                .HasForeignKey(x => x.ToProfileId).OnDelete(DeleteBehavior.Restrict);

            // One live interest signal per direction per pair; re-sending reuses the row.
            e.HasIndex(x => new { x.FromProfileId, x.ToProfileId }).IsUnique();
            e.Ignore(x => x.ChatUnlocked);
        });

        b.Entity<Deal>(e =>
        {
            e.Property(x => x.Reference).HasMaxLength(32).IsRequired();
            e.HasIndex(x => x.Reference).IsUnique();
            e.Property(x => x.Description).HasMaxLength(2000).IsRequired();
            e.Property(x => x.MaterialDescription).HasMaxLength(500);
            e.Property(x => x.CapacityUnit).HasMaxLength(32).IsRequired();
            e.Property(x => x.Capacity).HasPrecision(18, 2);
            e.Property(x => x.Country).HasMaxLength(100).IsRequired();
            e.Property(x => x.LocationState).HasMaxLength(100).IsRequired();
            e.Property(x => x.District).HasMaxLength(100).IsRequired();
            e.Property(x => x.State).HasColumnName("DealState");

            e.HasOne(x => x.LenderProfile).WithMany(p => p.LenderDeals)
                .HasForeignKey(x => x.LenderProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.SeekerProfile).WithMany(p => p.SeekerDeals)
                .HasForeignKey(x => x.SeekerProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Interest).WithMany(i => i.Deals)
                .HasForeignKey(x => x.InterestId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.HaltedByProfile).WithMany()
                .HasForeignKey(x => x.HaltedByProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CategorySubType).WithMany()
                .HasForeignKey(x => x.CategorySubTypeId).OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(x => x.State);
            e.HasIndex(x => x.CreatedAt);

            e.Ignore(x => x.IsActiveDeal);
            e.Ignore(x => x.IsClosed);
        });

        b.Entity<DealStateHistory>(e =>
        {
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasOne(x => x.Deal).WithMany(d => d.StateHistory)
                .HasForeignKey(x => x.DealId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.TriggeredByProfile).WithMany()
                .HasForeignKey(x => x.TriggeredByProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.DealId, x.OccurredAt });
        });

        b.Entity<Message>(e =>
        {
            e.Property(x => x.Body).HasMaxLength(4000).IsRequired();
            e.HasOne(x => x.Deal).WithMany(d => d.Messages)
                .HasForeignKey(x => x.DealId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Interest).WithMany(i => i.Messages)
                .HasForeignKey(x => x.InterestId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.SenderProfile).WithMany()
                .HasForeignKey(x => x.SenderProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.DealId, x.SentAt });
            e.HasIndex(x => new { x.InterestId, x.SentAt });
        });

        b.Entity<Rating>(e =>
        {
            e.Property(x => x.Comment).HasMaxLength(1000);
            e.HasOne(x => x.Deal).WithMany(d => d.Ratings)
                .HasForeignKey(x => x.DealId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.RaterProfile).WithMany(p => p.RatingsGiven)
                .HasForeignKey(x => x.RaterProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RatedProfile).WithMany(p => p.RatingsReceived)
                .HasForeignKey(x => x.RatedProfileId).OnDelete(DeleteBehavior.Restrict);

            // One rating per party per Deal (spec §11 / tech-stack.md entity mapping).
            e.HasIndex(x => new { x.DealId, x.RaterProfileId }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CK_Rating_Stars", "[Stars] >= 1 AND [Stars] <= 5"));
        });

        b.Entity<EvidenceDocument>(e =>
        {
            e.Property(x => x.StorageKey).HasMaxLength(400).IsRequired();
            e.Property(x => x.FileName).HasMaxLength(260).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
            e.Property(x => x.DocumentType).HasMaxLength(100).IsRequired();
            e.Property(x => x.RejectionReason).HasMaxLength(1000);
            e.HasOne(x => x.Profile).WithMany(p => p.EvidenceDocuments)
                .HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
            // Restrict, not SetNull. Deleting a user cascades to both their Profile and
            // their Admin row, and both reach this table — SQL Server rejects the two
            // cascade paths outright (error 1785). Restrict is also the right rule for
            // an audit trail: who reviewed a document should not quietly become null,
            // and it matches how AdminActionLog already references Admin.
            e.HasOne(x => x.ReviewedByAdmin).WithMany()
                .HasForeignKey(x => x.ReviewedByAdminId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ProfileId, x.SubmittedAt });
        });

        b.Entity<Admin>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        });

        b.Entity<AdminActionLog>(e =>
        {
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasOne(x => x.Admin).WithMany(a => a.Actions)
                .HasForeignKey(x => x.AdminId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.TargetProfile).WithMany()
                .HasForeignKey(x => x.TargetProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TargetProfileId, x.OccurredAt });
        });

        b.Entity<ResumeRequest>(e =>
        {
            e.HasOne(x => x.Deal).WithMany(d => d.ResumeRequests)
                .HasForeignKey(x => x.DealId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.RequestedByProfile).WithMany()
                .HasForeignKey(x => x.RequestedByProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.DealId, x.Status });
        });

        b.Entity<DealProposal>(e =>
        {
            e.Property(x => x.Capacity).HasPrecision(18, 2);
            e.Property(x => x.CapacityUnit).HasMaxLength(32).IsRequired();
            e.Property(x => x.MaterialDescription).HasMaxLength(500);
            e.Property(x => x.Description).HasMaxLength(2000).IsRequired();

            e.HasOne(x => x.Interest).WithMany(i => i.Proposals)
                .HasForeignKey(x => x.InterestId).OnDelete(DeleteBehavior.Cascade);

            // Restrict, not Cascade: deleting a user already cascades into this table
            // through the interest, and SQL Server refuses two cascade paths to one
            // table outright (error 1785).
            e.HasOne(x => x.ProposedByProfile).WithMany()
                .HasForeignKey(x => x.ProposedByProfileId).OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.CategorySubType).WithMany()
                .HasForeignKey(x => x.CategorySubTypeId).OnDelete(DeleteBehavior.Restrict);

            // The deal these terms produced, and the counter that replaced them. Both are
            // Restrict for the same reason, and the self-reference must never cascade.
            e.HasOne(x => x.Deal).WithMany()
                .HasForeignKey(x => x.DealId).OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.SupersededByProposal).WithMany()
                .HasForeignKey(x => x.SupersededByProposalId).OnDelete(DeleteBehavior.Restrict);

            // One live proposal per interest is the rule the service enforces; this is
            // what makes finding it cheap.
            e.HasIndex(x => new { x.InterestId, x.Status });
        });

        b.Entity<Notification>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(64).IsRequired();
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Body).HasMaxLength(1000).IsRequired();
            e.Property(x => x.Link).HasMaxLength(300);
            e.HasOne(x => x.Profile).WithMany()
                .HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ProfileId, x.IsRead, x.CreatedAt });
        });

        foreach (var entity in b.Model.GetEntityTypes())
        {
            foreach (var prop in entity.GetProperties())
            {
                // Enums persist as ints so the schema stays readable and filterable in SQL.
                if (prop.ClrType.IsEnum || (Nullable.GetUnderlyingType(prop.ClrType)?.IsEnum ?? false))
                {
                    prop.SetProviderClrType(typeof(int));
                }
            }
        }

        SeedTaxonomy(b);
    }

    /// <summary>
    /// The shared category taxonomy from the design brief. Reference data, not sample trust
    /// signals — safe to ship in a migration.
    /// </summary>
    private static void SeedTaxonomy(ModelBuilder b)
    {
        b.Entity<CategorySubType>().HasData(
            new CategorySubType { Id = 1, Category = DealCategory.Money, Key = "working-capital", DisplayName = "Working capital", DefaultUnit = "INR", SortOrder = 1 },
            new CategorySubType { Id = 2, Category = DealCategory.Money, Key = "short-term-advance", DisplayName = "Short-term advance", DefaultUnit = "INR", SortOrder = 2 },
            new CategorySubType { Id = 3, Category = DealCategory.Money, Key = "stock-credit", DisplayName = "Stock credit line", DefaultUnit = "INR", SortOrder = 3 },
            new CategorySubType { Id = 10, Category = DealCategory.RawMaterial, Key = "fruits", DisplayName = "Fruits", DefaultUnit = "kg", SortOrder = 10 },
            new CategorySubType { Id = 11, Category = DealCategory.RawMaterial, Key = "dairy", DisplayName = "Dairy", DefaultUnit = "litres", SortOrder = 11 },
            new CategorySubType { Id = 12, Category = DealCategory.RawMaterial, Key = "medicine", DisplayName = "Medicine", DefaultUnit = "units", SortOrder = 12 },
            new CategorySubType { Id = 13, Category = DealCategory.RawMaterial, Key = "industrial-equipment", DisplayName = "Industrial equipment", DefaultUnit = "units", SortOrder = 13 },
            new CategorySubType { Id = 14, Category = DealCategory.RawMaterial, Key = "grains-pulses", DisplayName = "Grains & pulses", DefaultUnit = "quintal", SortOrder = 14 },
            new CategorySubType { Id = 15, Category = DealCategory.RawMaterial, Key = "textiles", DisplayName = "Textiles", DefaultUnit = "metres", SortOrder = 15 }
        );
    }
}

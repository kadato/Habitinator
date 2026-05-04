using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace App.Web.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<BoardItemEntity> BoardItems => Set<BoardItemEntity>();

    public DbSet<BoardRequestIdempotencyEntity> BoardRequestIdempotencies => Set<BoardRequestIdempotencyEntity>();

    public DbSet<UserActivityEventEntity> UserActivityEvents => Set<UserActivityEventEntity>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(x => x.NotificationSettingsJson).HasColumnType("text");
            entity.Property(x => x.UserPreferencesJson).HasColumnType("text");
        });

        builder.Entity<UserActivityEventEntity>(entity =>
        {
            entity.ToTable("UserActivityEvents");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EventType).IsRequired();
            entity.Property(x => x.OccurredAtUtc).IsRequired();
            entity.HasIndex(x => new { x.UserId, x.OccurredAtUtc });
            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<BoardItemEntity>(entity =>
        {
            entity.ToTable("BoardItems");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(4000);
            entity.Property(x => x.Tags).HasMaxLength(500);
            entity.Property(x => x.ChecklistJson).HasMaxLength(8000);
            entity.Property(x => x.DailyLastCompletedOn);
            entity.Property(x => x.TrackPlus).IsRequired();
            entity.Property(x => x.TrackMinus).IsRequired();
            entity.Property(x => x.ResetPeriod).IsRequired();
            entity.Property(x => x.NegativeCounter).IsRequired();
            entity.Property(x => x.Section).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
            entity.HasIndex(x => new { x.UserId, x.Section });
            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.DeletedAtUtc);
        });

        builder.Entity<BoardRequestIdempotencyEntity>(entity =>
        {
            entity.ToTable("BoardRequestIdempotencies");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
            entity.Property(x => x.RequestFingerprintHex).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ResponseBody).HasColumnType("text");
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.HasIndex(x => new { x.UserId, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => x.CreatedAtUtc);
            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

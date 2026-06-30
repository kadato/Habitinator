using Microsoft.EntityFrameworkCore;

namespace App.MAUI.Data;

public sealed partial class LocalBoardDbContext(DbContextOptions<LocalBoardDbContext> options) : DbContext(options)
{

    public DbSet<LocalBoardItemRow> BoardItems => Set<LocalBoardItemRow>();

    public DbSet<BoardOutboxRow> Outbox => Set<BoardOutboxRow>();

    public DbSet<LocalBoardStoreMetaRow> Meta => Set<LocalBoardStoreMetaRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LocalBoardStoreMetaRow>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.BoundUserKey).HasMaxLength(512);
            e.Property(x => x.LastSyncCursorUtc).HasMaxLength(64);
        });

        modelBuilder.Entity<LocalBoardItemRow>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UserKey).HasMaxLength(512);
            e.Property(x => x.Title).HasMaxLength(512);
        });

        modelBuilder.Entity<BoardOutboxRow>(e =>
        {
            e.HasKey(x => x.OperationId);
            e.Property(x => x.UserKey).HasMaxLength(512);
            e.Property(x => x.PayloadJson).HasMaxLength(16_384);
            e.Property(x => x.LastError).HasMaxLength(2048);
        });
    }
}

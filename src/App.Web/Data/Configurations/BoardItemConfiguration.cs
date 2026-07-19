using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Web.Data.Configurations;

public sealed class BoardItemConfiguration : IEntityTypeConfiguration<BoardItemEntity>
{
    public void Configure(EntityTypeBuilder<BoardItemEntity> builder)
    {
        builder.ToTable("BoardItems");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(4000);
        builder.Property(x => x.Tags).HasMaxLength(500);
        builder.Property(x => x.ChecklistJson).HasMaxLength(8000);
        builder.Property(x => x.DailyLastCompletedOn);
        builder.Property(x => x.TrackPlus).IsRequired();
        builder.Property(x => x.TrackMinus).IsRequired();
        builder.Property(x => x.ResetPeriod).IsRequired();
        builder.Property(x => x.NegativeCounter).IsRequired();
        builder.Property(x => x.Section).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.SortOrder).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();
        builder.HasIndex(x => new { x.UserId, x.Section });
        builder.HasIndex(x => new { x.UserId, x.DeletedAtUtc });
        builder.HasIndex(x => new { x.UserId, x.UpdatedAtUtc });
        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.DeletedAtUtc);
    }
}

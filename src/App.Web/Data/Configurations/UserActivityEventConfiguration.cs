using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Web.Data.Configurations;

public sealed class UserActivityEventConfiguration : IEntityTypeConfiguration<UserActivityEventEntity>
{
    public void Configure(EntityTypeBuilder<UserActivityEventEntity> builder)
    {
        builder.ToTable("UserActivityEvents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).IsRequired();
        builder.Property(x => x.OccurredAtUtc).IsRequired();
        builder.HasIndex(x => new { x.UserId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.UserId, x.BoardItemId, x.OccurredAtUtc });
        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

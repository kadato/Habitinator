using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Web.Data.Configurations;

public sealed class AiUsageDailyConfiguration : IEntityTypeConfiguration<AiUsageDailyEntity>
{
    public void Configure(EntityTypeBuilder<AiUsageDailyEntity> builder)
    {
        builder.ToTable("AiUsageDaily");
        builder.HasKey(x => new { x.UserId, x.Day });
        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

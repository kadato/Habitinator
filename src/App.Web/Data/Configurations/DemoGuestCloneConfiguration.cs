using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Web.Data.Configurations;

public sealed class DemoGuestCloneConfiguration : IEntityTypeConfiguration<DemoGuestCloneEntity>
{
    public void Configure(EntityTypeBuilder<DemoGuestCloneEntity> builder)
    {
        builder.ToTable("DemoGuestClones");
        builder.HasKey(x => x.UserId);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.HasIndex(x => x.CreatedAtUtc);
        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

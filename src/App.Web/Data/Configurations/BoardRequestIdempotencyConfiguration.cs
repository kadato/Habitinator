using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Web.Data.Configurations;

public sealed class BoardRequestIdempotencyConfiguration : IEntityTypeConfiguration<BoardRequestIdempotencyEntity>
{
    public void Configure(EntityTypeBuilder<BoardRequestIdempotencyEntity> builder)
    {
        builder.ToTable("BoardRequestIdempotencies");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(x => x.RequestFingerprintHex).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ResponseBody).HasColumnType("text");
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.HasIndex(x => new { x.UserId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => x.CreatedAtUtc);
        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

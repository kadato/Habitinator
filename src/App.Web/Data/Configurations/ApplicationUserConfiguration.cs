using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Web.Data.Configurations;

public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(x => x.NotificationSettings)
            .HasColumnName("NotificationSettingsJson")
            .HasColumnType("jsonb");

        builder.Property(x => x.UserPreferences)
            .HasColumnName("UserPreferencesJson")
            .HasColumnType("jsonb");
    }
}

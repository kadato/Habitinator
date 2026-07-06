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

        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}

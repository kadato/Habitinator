using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace App.Web.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    // A compiled model lives in Data/CompiledModels. EF discovers it through the
    // [DbContextModel] assembly attribute. Regenerate the model after an entity or
    // configuration change:
    //   dotnet ef dbcontext optimize --project src/App.Web --startup-project src/App.Web \
    //     --output-dir Data/CompiledModels --namespace App.Web.Data.CompiledModels
    public DbSet<BoardItemEntity> BoardItems => Set<BoardItemEntity>();

    public DbSet<BoardRequestIdempotencyEntity> BoardRequestIdempotencies => Set<BoardRequestIdempotencyEntity>();

    public DbSet<UserActivityEventEntity> UserActivityEvents => Set<UserActivityEventEntity>();

    public DbSet<McpTokenEntity> McpTokens => Set<McpTokenEntity>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasPostgresExtension("pg_trgm");
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}

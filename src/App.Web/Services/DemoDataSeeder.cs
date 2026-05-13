using App.Web.Data;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace App.Web.Services;

public static class DemoDataSeeder
{
    public static async Task SeedAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<DemoUserOptions>>();
        var boardPersistence = scope.ServiceProvider.GetRequiredService<BoardPersistenceService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DemoDataSeeder");

        var primaryCs = PostgresResilienceConnectionString.EnsureColdStartTimeouts(
            PostgresMigrationConnectionStrings.ResolvePrimary(configuration));
        var migrationCs = PostgresResilienceConnectionString.EnsureColdStartTimeouts(
            PostgresMigrationConnectionStrings.ResolveForMigrations(configuration));
        if (!string.Equals(migrationCs, primaryCs, StringComparison.Ordinal))
        {
            logger.LogInformation(
                "Applying EF migrations on a dedicated connection (not the primary app string). " +
                "This avoids Neon PgBouncer transaction-pooling issues during schema updates.");
        }

        await PostgresPollyRetry.ExecuteAsync(
            async ct =>
            {
                var migrationOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseNpgsql(migrationCs, PostgresDbContextOptions.ConfigureNpgsqlResilience)
                    .Options;
                await using var migrationContext = new ApplicationDbContext(migrationOptions);
                await migrationContext.Database.MigrateAsync(ct);
            },
            logger,
            cancellationToken);

        var demo = options.Value;
        var guest = await userManager.FindByEmailAsync(demo.Email);
        if (guest is null)
        {
            guest = new ApplicationUser
            {
                UserName = demo.Email,
                Email = demo.Email,
                EmailConfirmed = true,
                UserPreferencesJson = "{}"
            };

            var createResult = await userManager.CreateAsync(guest, demo.Password);
            if (!createResult.Succeeded)
            {
                var reasons = string.Join("; ", createResult.Errors.Select(x => x.Description));
                throw new InvalidOperationException($"Failed to create demo guest user: {reasons}");
            }
        }
        else if (demo.ForceReseed)
        {
            // Reset to configured password so the demo login always matches config after a reseed.
            var token = await userManager.GeneratePasswordResetTokenAsync(guest);
            var reset = await userManager.ResetPasswordAsync(guest, token, demo.Password);
            if (!reset.Succeeded)
            {
                var reasons = string.Join("; ", reset.Errors.Select(x => x.Description));
                throw new InvalidOperationException($"Failed to reset demo guest password: {reasons}");
            }

            await ClearGuestDataAsync(dbContext, guest.Id, cancellationToken);
            guest.NotificationSettingsJson = null;
            guest.UserPreferencesJson = "{}";
            await userManager.UpdateAsync(guest);

            await boardPersistence.InsertDemoBoardDataAsync(guest.Id, cancellationToken);
            try
            {
                await GuestActivityDemoSeeder.SeedIfMissingAsync(dbContext, guest.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Guest activity demo seed skipped after ForceReseed.");
            }

            logger.LogWarning(
                "Demo guest reseeded (ForceReseed). Turn off {Section}:{Flag} in configuration or environment when finished.",
                DemoUserOptions.SectionName, nameof(demo.ForceReseed));
            return;
        }

        await boardPersistence.SeedBoardDataIfMissingAsync(guest.Id, cancellationToken);
        try
        {
            await GuestActivityDemoSeeder.SeedIfMissingAsync(dbContext, guest.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Guest activity demo seed skipped.");
        }
    }

    private static async Task ClearGuestDataAsync(ApplicationDbContext db, Guid guestUserId,
        CancellationToken cancellationToken)
    {
        var events = await db.UserActivityEvents.Where(e => e.UserId == guestUserId).ToListAsync(cancellationToken);
        if (events.Count > 0) db.UserActivityEvents.RemoveRange(events);

        var items = await db.BoardItems.Where(b => b.UserId == guestUserId).ToListAsync(cancellationToken);
        if (items.Count > 0) db.BoardItems.RemoveRange(items);

        if (events.Count > 0 || items.Count > 0) await db.SaveChangesAsync(cancellationToken);
    }
}

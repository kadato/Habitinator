using App.Web.Data;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace App.Web.Services;

public static class DemoDataSeeder
{
    public static async Task SeedAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<DemoUserOptions>>();
        var boardPersistenceService = scope.ServiceProvider.GetRequiredService<BoardPersistenceService>();

        await dbContext.Database.MigrateAsync(cancellationToken);

        var demo = options.Value;
        var guest = await userManager.FindByEmailAsync(demo.Email);
        if (guest is null)
        {
            guest = new ApplicationUser
            {
                UserName = demo.Email,
                Email = demo.Email,
                Timezone = demo.Timezone,
                EmailConfirmed = true
            };

            var createResult = await userManager.CreateAsync(guest, demo.Password);
            if (!createResult.Succeeded)
            {
                var reasons = string.Join("; ", createResult.Errors.Select(x => x.Description));
                throw new InvalidOperationException($"Failed to create demo guest user: {reasons}");
            }
        }

        await boardPersistenceService.SeedBoardDataIfMissingAsync(guest.Id, cancellationToken);
        await GuestActivityDemoSeeder.SeedIfMissingAsync(dbContext, guest.Id, cancellationToken);
    }
}

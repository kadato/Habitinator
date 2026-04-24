using App.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace App.Web.Services;

public static class DemoDataSeeder
{
    public static async Task SeedAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = serviceProvider.CreateScope();
        ApplicationDbContext dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        IOptions<DemoUserOptions> options = scope.ServiceProvider.GetRequiredService<IOptions<DemoUserOptions>>();
        BoardPersistenceService boardPersistenceService = scope.ServiceProvider.GetRequiredService<BoardPersistenceService>();

        await dbContext.Database.MigrateAsync(cancellationToken);

        DemoUserOptions demo = options.Value;
        ApplicationUser? guest = await userManager.FindByEmailAsync(demo.Email);
        if (guest is null)
        {
            guest = new ApplicationUser
            {
                UserName = demo.Email,
                Email = demo.Email,
                Timezone = demo.Timezone,
                EmailConfirmed = true
            };

            IdentityResult createResult = await userManager.CreateAsync(guest, demo.Password);
            if (!createResult.Succeeded)
            {
                string reasons = string.Join("; ", createResult.Errors.Select(x => x.Description));
                throw new InvalidOperationException($"Failed to create demo guest user: {reasons}");
            }
        }

        await boardPersistenceService.SeedBoardDataIfMissingAsync(guest.Id, cancellationToken);
    }
}

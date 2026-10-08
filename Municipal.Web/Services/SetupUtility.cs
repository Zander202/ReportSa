using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Municipal.Web.Data;
using Municipal.Web.Models;
namespace Municipal.Web.Services;
public static class SetupUtility
{
    public static async Task RunAsync(IServiceProvider services, IConfiguration config, bool demo)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (db.Database.IsNpgsql()) await db.Database.EnsureCreatedAsync();
        else await db.Database.MigrateAsync();
        if (demo)
        {
            var service = scope.ServiceProvider.GetRequiredService<RequestService>();
            foreach (var category in Enum.GetValues<Category>()) {
                var location = $"[FICTIONAL DEMO] {(int)category + 1} Example Street";
                if (await db.Requests.AnyAsync(x => x.Location == location && x.Category == category)) continue;
                var r = await service.SubmitAsync(new() { Category = category, Location = location, Description = "Fictional student demonstration request.", ResidentPhoneNumber = "+27000000000" });
                Console.WriteLine($"Demo reference: {r.ReferenceCode}");
            }
            Console.WriteLine("Fictional demo seed complete. Existing seed requests were preserved.");
            return;
        }
        var email = config["Admin:Email"]; var password = config["Admin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) throw new InvalidOperationException("Set Admin:Email and Admin:Password with User Secrets or environment variables, then run --setup.");
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync("Administrator")) Ensure(await roles.CreateAsync(new("Administrator")));
        var admins = await users.GetUsersInRoleAsync("Administrator");
        if (admins.Any(x => x.Email != email)) throw new InvalidOperationException("An administrator already exists. This prototype allows one administrator.");
        var user = await users.FindByEmailAsync(email);
        if (user is null) { user = new() { UserName = email, Email = email, EmailConfirmed = true }; Ensure(await users.CreateAsync(user, password)); }
        if (!await users.IsInRoleAsync(user, "Administrator")) Ensure(await users.AddToRoleAsync(user, "Administrator"));
        Console.WriteLine("Database migrated and administrator provisioned. Existing passwords are never overwritten.");
    }
    private static void Ensure(IdentityResult result) { if (!result.Succeeded) throw new InvalidOperationException(string.Join(" ", result.Errors.Select(x => x.Description))); }
}

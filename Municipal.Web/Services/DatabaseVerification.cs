using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Municipal.Web.Data;
using Municipal.Web.Models;

namespace Municipal.Web.Services;

public static class DatabaseVerification
{
    public static async Task RunAsync(IServiceProvider services, IConfiguration configuration, bool verifyStorage = false)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.OpenConnectionAsync();
        try
        {
            // Query the actual tables, rather than just testing a network connection.
            var reports = await db.Requests.CountAsync();
            var history = await db.StatusHistory.CountAsync();
            var accounts = await db.Users.CountAsync();
            var email = configuration["Admin:Email"];
            var password = configuration["Admin:Password"];
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("Configure Admin:Email and Admin:Password to verify the existing administrator login.");

            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = await users.FindByEmailAsync(email);
            if (user?.PasswordHash is null || !await users.IsInRoleAsync(user, "Administrator"))
                throw new InvalidOperationException("The configured administrator is missing from this database. Existing accounts were not changed.");

            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<IdentityUser>>();
            if (hasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
                throw new InvalidOperationException("The configured password does not match the existing administrator. The password was not changed.");

            Console.WriteLine($"Database verified: {reports} reports, {history} history entries, {accounts} accounts.");
            Console.WriteLine("Existing administrator password and role verified. No records or passwords were changed.");
            if (verifyStorage)
                await VerifyStorageAsync(db, scope.ServiceProvider.GetRequiredService<RequestService>());
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task VerifyStorageAsync(ApplicationDbContext db, RequestService requests)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var created = await requests.SubmitAsync(new ReportForm
            {
                Category = Category.Pothole,
                MunicipalityCode = "CPT",
                Location = "[FICTIONAL DEMO] Supabase storage verification",
                Description = "Temporary report; verification transaction is rolled back.",
                ResidentEmail = "verification@example.invalid",
                Latitude = -33.9249,
                Longitude = 18.4241
            });
            var reference = created.ReferenceCode;
            db.ChangeTracker.Clear();
            var saved = await requests.TrackAsync(reference)
                ?? throw new InvalidOperationException("The saved report could not be read back.");
            if (saved.ResidentEmail != "verification@example.invalid" || saved.MunicipalityCode != "CPT"
                || saved.Latitude != -33.9249 || saved.Longitude != 18.4241 || saved.History.Count != 1)
                throw new InvalidOperationException("Report details or history did not persist correctly.");

            await requests.UpdateAsync(new UpdateForm
            {
                Id = saved.Id,
                RowVersion = Convert.ToBase64String(saved.RowVersion),
                Priority = Priority.Urgent,
                Status = RequestStatus.Assigned,
                AssignedRole = saved.AssignedRole,
                AssignedPerson = saved.AssignedPerson
            });
            db.ChangeTracker.Clear();
            var updated = await requests.TrackAsync(reference);
            if (updated?.Status != RequestStatus.Assigned || updated.Priority != Priority.Urgent
                || updated.History.Count != 2)
                throw new InvalidOperationException("Report updates or tracking history did not persist correctly.");
        }
        finally
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
        }
        Console.WriteLine("Storage verified: report details, GPS, assignments, priority, status, and tracking history. Temporary records rolled back.");
    }
}

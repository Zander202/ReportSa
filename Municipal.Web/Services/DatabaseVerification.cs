using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Municipal.Web.Data;
using Municipal.Web.Models;

namespace Municipal.Web.Services;

public static class DatabaseVerification
{
    public static async Task RunAsync(IServiceProvider services, IConfiguration configuration, bool verifyStorage = false,
        bool verifyLogin = true, bool verifyFeatures = false)
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
            Console.WriteLine($"Database verified: {reports} reports, {history} history entries, {accounts} accounts.");
            if (verifyFeatures) await VerifyCommunityFeaturesAsync(db, scope.ServiceProvider.GetRequiredService<RequestService>());
            if (!verifyLogin)
            {
                Console.WriteLine("Shared database connection verified. No records or accounts were changed.");
                return;
            }
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

            Console.WriteLine("Existing administrator password and role verified. No records or passwords were changed.");
            if (verifyStorage)
                await VerifyStorageAsync(db, scope.ServiceProvider.GetRequiredService<RequestService>());
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task VerifyCommunityFeaturesAsync(ApplicationDbContext db, RequestService requests)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jf1sAAAAASUVORK5CYII=");
            using var imageStream = new MemoryStream(image);
            var created = await requests.SubmitAsync(new ReportForm
            {
                Category = Category.Pothole, MunicipalityCode = "CPT", Location = "[TEMPORARY VERIFICATION] Photo storage",
                ResidentEmail = "verification@example.invalid", Latitude = -33.9249, Longitude = 18.4241,
                Photos = [new FormFile(imageStream, 0, image.Length, "Photos", "verification.png")]
            });
            var stored = await db.RequestPhotos.SingleAsync(photo => photo.ProblemRequestId == created.Id);
            if (stored.ContentType != "image/png" || !stored.Content.SequenceEqual(image))
                throw new InvalidOperationException("Photo storage verification failed.");
            var community = new CommunityService(db);
            if (!await community.SupportAsync(new SupportForm { RequestId = created.Id, Email = "neighbour@example.invalid" })
                || await community.SupportAsync(new SupportForm { RequestId = created.Id, Email = "NEIGHBOUR@example.invalid" }))
                throw new InvalidOperationException("Affected count verification failed.");
            foreach (var status in new[] { RequestStatus.Assigned, RequestStatus.InProgress, RequestStatus.Resolved })
            {
                var current = await requests.TrackAsync(created.ReferenceCode) ?? throw new InvalidOperationException();
                using var proofStream = new MemoryStream(image);
                await requests.UpdateAsync(new UpdateForm
                {
                    Id = current.Id, RowVersion = Convert.ToBase64String(current.RowVersion), Status = status,
                    Priority = current.Priority, AssignedPerson = current.AssignedPerson, AssignedRole = current.AssignedRole,
                    ProgressNote = "Temporary verification update.",
                    ResolutionNotes = status == RequestStatus.Resolved ? "Temporary verification repair." : null,
                    ResolutionPhotos = status == RequestStatus.Resolved
                        ? [new FormFile(proofStream, 0, image.Length, "ResolutionPhotos", "repair.png")] : []
                });
            }
            await community.ReviewAsync(new ReviewForm { ReferenceCode = created.ReferenceCode, Email = created.ResidentEmail, Confirmed = true });
            var tracked = await requests.TrackAsync(created.ReferenceCode);
            if (tracked?.Status != RequestStatus.Closed || tracked.Photos.Count != 2
                || tracked.Photos.Count(photo => photo.IsResolution) != 1)
                throw new InvalidOperationException("Repair evidence verification failed.");
            await community.NearbyAsync("CPT", null, -33.9249, 18.4241);
            await community.DashboardAsync("CPT");
        }
        finally
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
        }
        Console.WriteLine("Verified photos, repair evidence, affected counts, timeline, resident confirmation and dashboard. All temporary records rolled back.");
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

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Municipal.Web.Data;
using Municipal.Web.Models;
namespace Municipal.Web.Services;

public static class SetupUtility
{
    public static async Task UpgradeSchemaAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (db.Database.IsNpgsql()) await EnsurePostgresTablesAsync(db);
        else await db.Database.MigrateAsync();
        Console.WriteLine("ReportSa schema upgraded. Existing reports and accounts were preserved.");
    }
    public static async Task RunAsync(IServiceProvider services, IConfiguration config, bool demo)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (db.Database.IsNpgsql()) await EnsurePostgresTablesAsync(db);
        else await db.Database.MigrateAsync();
        await BackfillAssignmentsAsync(db, scope.ServiceProvider.GetRequiredService<AssignmentService>());

        if (demo)
        {
            var service = scope.ServiceProvider.GetRequiredService<RequestService>();
            foreach (var category in Enum.GetValues<Category>())
            {
                var location = $"[FICTIONAL DEMO] {(int)category + 1} Example Street";
                if (await db.Requests.AnyAsync(x => x.Location == location && x.Category == category)) continue;
                var r = await service.SubmitAsync(new()
                {
                    Category = category,
                    MunicipalityCode = "CPT",
                    Location = location,
                    Description = "Fictional student demonstration request.",
                    ResidentEmail = "demo@example.invalid"   // EmailService skips .invalid addresses
                });
                Console.WriteLine($"Demo reference: {r.ReferenceCode}");
            }
            Console.WriteLine("Fictional demo seed complete. Existing seed requests were preserved.");
            return;
        }

        var email = config["Admin:Email"];
        var password = config["Admin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Set Admin:Email and Admin:Password with User Secrets or environment variables, then run --setup.");

        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync("Administrator")) Ensure(await roles.CreateAsync(new("Administrator")));
        var admins = await users.GetUsersInRoleAsync("Administrator");
        if (admins.Any(x => x.Email != email))
            throw new InvalidOperationException("An administrator already exists. This prototype allows one administrator.");

        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new() { UserName = email, Email = email, EmailConfirmed = true };
            Ensure(await users.CreateAsync(user, password));
        }
        if (!await users.IsInRoleAsync(user, "Administrator")) Ensure(await users.AddToRoleAsync(user, "Administrator"));
        Console.WriteLine("Database migrated and administrator provisioned. Existing passwords are never overwritten.");
    }

    private static async Task EnsurePostgresTablesAsync(ApplicationDbContext db)
    {
        await db.Database.EnsureCreatedAsync();

        // Supabase already has service tables, so EnsureCreated can skip the app schema.
        var expectedTables = db.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName()!)
            .Where(name => name is not ("RequestPhotos" or "RequestSupporters"))
            .ToHashSet(StringComparer.Ordinal);
        var existingTables = new HashSet<string>(StringComparer.Ordinal);
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = current_schema()";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) existingTables.Add(reader.GetString(0));
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        if (!expectedTables.IsSubsetOf(existingTables) && expectedTables.Overlaps(existingTables))
            throw new InvalidOperationException("The application database schema is incomplete. Missing tables: " +
                string.Join(", ", expectedTables.Except(existingTables)) + ". Existing tables were preserved.");

        if (!expectedTables.IsSubsetOf(existingTables))
            await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();

        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Requests"
                ADD COLUMN IF NOT EXISTS "MunicipalityCode" character varying(10) NOT NULL DEFAULT '',
                ADD COLUMN IF NOT EXISTS "MunicipalityName" character varying(200) NOT NULL DEFAULT '',
                ADD COLUMN IF NOT EXISTS "Latitude" double precision,
                ADD COLUMN IF NOT EXISTS "Longitude" double precision,
                ADD COLUMN IF NOT EXISTS "EstimatedCompletionAt" timestamp with time zone,
                ADD COLUMN IF NOT EXISTS "ConfirmationEmailStatus" integer NOT NULL DEFAULT 0;
            """);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Requests"
                ADD COLUMN IF NOT EXISTS "ResolvedAt" timestamp with time zone,
                ADD COLUMN IF NOT EXISTS "ResidentReview" integer NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "ResolutionNotes" character varying(1000);
            ALTER TABLE "StatusHistory" ADD COLUMN IF NOT EXISTS "Note" character varying(1000);
            CREATE TABLE IF NOT EXISTS "RequestPhotos" (
                "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                "ProblemRequestId" integer NOT NULL REFERENCES "Requests"("Id") ON DELETE CASCADE,
                "FileName" character varying(120) NOT NULL,
                "ContentType" character varying(30) NOT NULL,
                "Content" bytea NOT NULL,
                "IsResolution" boolean NOT NULL,
                "UploadedAt" timestamp with time zone NOT NULL
            );
            CREATE TABLE IF NOT EXISTS "RequestSupporters" (
                "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                "ProblemRequestId" integer NOT NULL REFERENCES "Requests"("Id") ON DELETE CASCADE,
                "Email" character varying(254) NOT NULL,
                "EmailKey" character varying(64) NOT NULL,
                "JoinedAt" timestamp with time zone NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_RequestPhotos_ProblemRequestId" ON "RequestPhotos" ("ProblemRequestId");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_RequestSupporters_ProblemRequestId_EmailKey"
                ON "RequestSupporters" ("ProblemRequestId", "EmailKey");
            CREATE INDEX IF NOT EXISTS "IX_Requests_MunicipalityCode_Status_ReportedAt"
                ON "Requests" ("MunicipalityCode", "Status", "ReportedAt");
            """);
    }

    private static async Task BackfillAssignmentsAsync(ApplicationDbContext db, AssignmentService assignments)
    {
        var requests = await db.Requests.Where(x => x.AssignedPerson == "" || x.AssignedPerson == "To be assigned" || x.EstimatedCompletionAt == null).ToListAsync();
        foreach (var request in requests)
        {
            if (string.IsNullOrWhiteSpace(request.AssignedPerson) || request.AssignedPerson == "To be assigned")
                request.AssignedPerson = assignments.For(request.Category).Person;
            request.EstimatedCompletionAt ??= AssignmentService.Estimate(request.Category, request.ReportedAt);
            if (db.Database.IsNpgsql()) request.RowVersion = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        }
        await db.SaveChangesAsync();
    }

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException(string.Join(" ", result.Errors.Select(x => x.Description)));
    }
}

using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Municipal.Web.Data;
using Municipal.Web.Models;
using Municipal.Web.Services;

namespace Municipal.Tests;

public class ValidationTests
{
    [Fact]
    public void ReferencesAreRandomAndWellFormed()
    {
        var codes = Enumerable
            .Range(0, 1000)
            .Select(_ => RequestService.NewReference())
            .ToArray();

        Assert.Equal(1000, codes.Distinct().Count());

        Assert.All(
            codes,
            c => Assert.Matches("^MUN-[0-9A-F]{24}$", c));
    }

    [Fact]
    public void InvalidCategoryAndMissingLocationFailValidation()
    {
        var errors = new List<ValidationResult>();

        var form = new ReportForm
        {
            Category = (Category)99,
            Location = ""
        };

        Assert.False(
            System.ComponentModel.DataAnnotations.Validator
                .TryValidateObject(
                    form,
                    new System.ComponentModel.DataAnnotations.ValidationContext(form),
                    errors,
                    true));

        Assert.True(errors.Count >= 2);
    }
}


public class SqlFixture : IAsyncLifetime
{
    public WebApplicationFactory<Program> App { get; private set; } = null!;

    public string Email { get; } = "admin@prototype.test";

    public string Password { get; } =
        "A!" +
        Convert.ToHexString(
            RandomNumberGenerator.GetBytes(24)) +
        "z1";


    public async Task InitializeAsync()
    {
        var connection =
            new SqlConnectionStringBuilder(
                Environment.GetEnvironmentVariable("MUNICIPAL_TEST_SQL")
                ??
                "Server=(localdb)\\MSSQLLocalDB;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=5");

        connection.InitialCatalog =
            "MunicipalReporting_Tests_" +
            Guid.NewGuid().ToString("N");


        var master =
            new SqlConnectionStringBuilder(
                connection.ConnectionString)
            {
                InitialCatalog = "master"
            };


        await using (var check =
            new SqlConnection(master.ConnectionString))
        {
            await check.OpenAsync();

            await using var command =
                check.CreateCommand();

            command.CommandText =
                "SELECT COUNT(*) FROM sys.databases WHERE name = @name";

            command.Parameters.AddWithValue(
                "@name",
                connection.InitialCatalog);

            if (Convert.ToInt32(
                    await command.ExecuteScalarAsync()) != 0)
            {
                throw new InvalidOperationException(
                    "Refusing to use an existing test database.");
            }
        }


        App =
            new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder
                        .UseSetting(
                            "environment",
                            "Testing")
                        .UseSetting(
                            "ConnectionStrings:DefaultConnection",
                            connection.ConnectionString);
                });


        using var scope = App.Services.CreateScope();

        var db =
            scope.ServiceProvider
                .GetRequiredService<ApplicationDbContext>();

        await db.Database.MigrateAsync();


        var users =
            scope.ServiceProvider
                .GetRequiredService<UserManager<IdentityUser>>();

        var roles =
            scope.ServiceProvider
                .GetRequiredService<RoleManager<IdentityRole>>();


        var roleResult =
            await roles.CreateAsync(
                new IdentityRole("Administrator"));

        Assert.True(roleResult.Succeeded);


        var user =
            new IdentityUser
            {
                UserName = Email,
                Email = Email,
                EmailConfirmed = true
            };


        var userResult =
            await users.CreateAsync(
                user,
                Password);

        Assert.True(userResult.Succeeded);


        var roleAssignment =
            await users.AddToRoleAsync(
                user,
                "Administrator");

        Assert.True(roleAssignment.Succeeded);

        Assert.NotEqual(
            Password,
            user.PasswordHash);
    }


    public Task DisposeAsync()
    {
        App?.Dispose();
        return Task.CompletedTask;
    }


    public HttpClient Client()
    {
        return App.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress =
                    new Uri("https://localhost"),

                AllowAutoRedirect = false
            });
    }
}


public class SqlWorkflowTests(SqlFixture fixture)
    : IClassFixture<SqlFixture>
{
    private sealed class CollisionService(
        ApplicationDbContext db,
        AssignmentService assignments,
        EmailService email,
        string duplicate)
        : RequestService(db, assignments, email)
    {
        private int attempts;

        protected override string GenerateReference()
        {
            return attempts++ == 0
                ? duplicate
                : NewReference();
        }
    }


    private static string Field(
        string html,
        string name)
    {
        var tag =
            Regex.Match(
                html,
                "<input[^>]*name=\"" +
                Regex.Escape(name) +
                "\"[^>]*>")
            .Value;

        return WebUtility.HtmlDecode(
            Regex.Match(
                tag,
                "value=\"([^\"]*)\"")
                .Groups[1]
                .Value);
    }


    private static async Task<Dictionary<string, string>> Token(
        HttpClient client,
        string url)
    {
        var response =
            await client.GetAsync(url);

        response.EnsureSuccessStatusCode();

        var html =
            await response.Content.ReadAsStringAsync();

        var token =
            Field(
                html,
                "__RequestVerificationToken");

        Assert.NotEmpty(token);

        return new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        };
    }


    private async Task<HttpClient> Admin()
    {
        var client =
            fixture.Client();

        var data =
            await Token(
                client,
                "/Account/Login");

        data["Email"] =
            fixture.Email;

        data["Password"] =
            fixture.Password;


        var response =
            await client.PostAsync(
                "/Account/Login",
                new FormUrlEncodedContent(data));


        Assert.Equal(
            HttpStatusCode.Redirect,
            response.StatusCode);

        Assert.StartsWith(
            "/Admin",
            response.Headers.Location!
                .OriginalString);


        var cookie =
            string.Join(
                ";",
                response.Headers
                    .GetValues("Set-Cookie"));


        Assert.Contains(
            "secure",
            cookie.ToLowerInvariant());

        Assert.Contains(
            "httponly",
            cookie.ToLowerInvariant());


        return client;
    }


    [Fact]
    public async Task FullHttpFlowPersistsAndTracksStatusHistory()
    {
        using var resident =
            fixture.Client();


        var data =
            await Token(
                resident,
                "/Requests/Report");


        data["Category"] = "0";

        data["Location"] =
            "[FICTIONAL TEST] 24 Example Street";

        data["Description"] =
            "A test pothole.";

        data["ResidentEmail"] =
            "resident@example.com";


        var response =
            await resident.PostAsync(
                "/Requests/Report",
                new FormUrlEncodedContent(data));


        Assert.Equal(
            HttpStatusCode.Redirect,
            response.StatusCode);


        var confirmation =
            await resident.GetStringAsync(
                response.Headers.Location);


        var reference =
            Field(
                confirmation,
                "reference");


        Assert.Matches(
            "^MUN-[0-9A-F]{24}$",
            reference);


        ProblemRequest saved;


        using (var scope =
            fixture.App.Services.CreateScope())
        {
            saved =
                await scope.ServiceProvider
                    .GetRequiredService<ApplicationDbContext>()
                    .Requests
                    .AsNoTracking()
                    .Include(r => r.History)
                    .SingleAsync(
                        r => r.ReferenceCode == reference);


            Assert.Equal(
                Priority.Normal,
                saved.Priority);

            Assert.Equal(
                RequestStatus.Submitted,
                saved.Status);

            Assert.Single(
                saved.History);

            Assert.Equal(
                "Roads maintenance team",
                saved.AssignedRole);
        }


        using var admin =
            await Admin();


        foreach (var status in new[]
        {
            RequestStatus.Submitted,
            RequestStatus.Assigned,
            RequestStatus.InProgress,
            RequestStatus.Resolved,
            RequestStatus.Closed
        })
        {
            var edit =
                await admin.GetStringAsync(
                    $"/Admin/Edit/{saved.Id}");


            var update =
                new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] =
                        Field(
                            edit,
                            "__RequestVerificationToken"),

                    ["Id"] =
                        saved.Id.ToString(),

                    ["RowVersion"] =
                        Field(
                            edit,
                            "RowVersion"),

                    ["Priority"] =
                        "3",

                    ["Status"] =
                        ((int)status).ToString(),

                    ["AssignedRole"] =
                        "Roads maintenance team",

                    ["AssignedPerson"] =
                        "Test Technician"
                };


            var result =
                await admin.PostAsync(
                    "/Admin/Update",
                    new FormUrlEncodedContent(update));


            Assert.Equal(
                HttpStatusCode.Redirect,
                result.StatusCode);


            using var scope =
                fixture.App.Services.CreateScope();


            var row =
                await scope.ServiceProvider
                    .GetRequiredService<ApplicationDbContext>()
                    .Requests
                    .AsNoTracking()
                    .Include(r => r.History)
                    .SingleAsync(
                        r => r.Id == saved.Id);


            Assert.Equal(
                Priority.Urgent,
                row.Priority);

            Assert.Equal(
                status,
                row.Status);

            Assert.Equal(
                "Test Technician",
                row.AssignedPerson);

            Assert.Equal(
                (int)status + 1,
                row.History.Count);
        }


        var track =
            await Token(
                resident,
                "/Requests/Track");


        track["ReferenceCode"] =
            reference.ToLowerInvariant();


        var tracked =
            await resident.PostAsync(
                "/Requests/Track",
                new FormUrlEncodedContent(track));


        var html =
            await tracked.Content
                .ReadAsStringAsync();


        Assert.Contains(
            "Resolved",
            html);

        Assert.Contains(
            "In Progress",
            html);

        Assert.Contains(
            "Status history",
            html);

        Assert.Contains(
            reference,
            html);


        using var finalScope =
            fixture.App.Services.CreateScope();


        var persisted =
            await finalScope.ServiceProvider
                .GetRequiredService<ApplicationDbContext>()
                .Requests
                .AsNoTracking()
                .Include(r => r.History)
                .SingleAsync(
                    r => r.ReferenceCode == reference);


        Assert.Equal(
            5,
            persisted.History.Count);

        Assert.True(
            persisted.LastUpdatedAt >=
            persisted.ReportedAt);


        var filtered =
            await admin.GetStringAsync(
                "/Admin?category=0&priority=3&status=4&sort=oldest");


        Assert.Contains(
            reference,
            filtered);
    }


    [Fact]
    public async Task AnonymousAdminAccessAndForgeryAreRejected()
    {
        using var client =
            fixture.Client();


        foreach (var url in new[]
        {
            "/Admin",
            "/Admin/Edit/1"
        })
        {
            var response =
                await client.GetAsync(url);


            Assert.Equal(
                HttpStatusCode.Redirect,
                response.StatusCode);

            Assert.Contains(
                "/Account/Login",
                response.Headers.Location!
                    .OriginalString);
        }


        var update =
            await client.PostAsync(
                "/Admin/Update",
                new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["Id"] = "1",
                        ["Priority"] = "3",
                        ["Status"] = "1"
                    }));


        Assert.Equal(
            HttpStatusCode.Redirect,
            update.StatusCode);


        var submission =
            await client.PostAsync(
                "/Requests/Report",
                new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["Location"] = "test",
                        ["Category"] = "0"
                    }));


        Assert.Equal(
            HttpStatusCode.BadRequest,
            submission.StatusCode);


        using var admin =
            await Admin();


        var forged =
            await admin.PostAsync(
                "/Admin/Update",
                new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["Id"] = "1",
                        ["Priority"] = "3",
                        ["Status"] = "1"
                    }));


        Assert.Equal(
            HttpStatusCode.BadRequest,
            forged.StatusCode);


        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync(
                "/Account/Register"))
            .StatusCode);
    }


    [Fact]
    public async Task InvalidSubmissionAndUnknownReferenceShowValidation()
    {
        using var client =
            fixture.Client();


        var form =
            await Token(
                client,
                "/Requests/Report");


        form["Category"] = "99";
        form["Location"] = " ";


        var invalid =
            await client.PostAsync(
                "/Requests/Report",
                new FormUrlEncodedContent(form));


        Assert.Equal(
            HttpStatusCode.OK,
            invalid.StatusCode);


        Assert.Contains(
            "validation",
            await invalid.Content
                .ReadAsStringAsync());


        foreach (var reference in new[]
        {
            "invalid",
            RequestService.NewReference()
        })
        {
            var track =
                await Token(
                    client,
                    "/Requests/Track");


            track["ReferenceCode"] =
                reference;


            var response =
                await client.PostAsync(
                    "/Requests/Track",
                    new FormUrlEncodedContent(track));


            Assert.Contains(
                "No request matches",
                await response.Content
                    .ReadAsStringAsync());
        }
    }


    [Fact]
    public async Task StatusValidationUniquenessAndConcurrencyAreEnforced()
    {
        using var scope =
            fixture.App.Services.CreateScope();


        var service =
            scope.ServiceProvider
                .GetRequiredService<RequestService>();

        var db =
            scope.ServiceProvider
                .GetRequiredService<ApplicationDbContext>();


        var r =
            await service.SubmitAsync(
                new ReportForm
                {
                    Category = Category.Other,

                    Location =
                        "[FICTIONAL TEST] Workflow validation",

                    ResidentEmail =
                        "workflow@example.com"
                });


        var originalVersion =
            Convert.ToBase64String(
                r.RowVersion);


        await Assert.ThrowsAsync<ArgumentException>(
            () =>
                service.UpdateAsync(
                    new UpdateForm
                    {
                        Id = r.Id,
                        Priority = (Priority)99,
                        Status = r.Status,
                        AssignedRole = r.AssignedRole,
                        RowVersion = originalVersion
                    }));


        await Assert.ThrowsAsync<ArgumentException>(
            () =>
                service.UpdateAsync(
                    new UpdateForm
                    {
                        Id = r.Id,
                        Priority = r.Priority,
                        Status = RequestStatus.Resolved,
                        AssignedRole = r.AssignedRole,
                        RowVersion = originalVersion
                    }));


        await service.UpdateAsync(
            new UpdateForm
            {
                Id = r.Id,
                Priority = Priority.High,
                Status = RequestStatus.Assigned,
                AssignedRole = r.AssignedRole,
                RowVersion = originalVersion
            });


        await Assert.ThrowsAsync<ArgumentException>(
            () =>
                service.UpdateAsync(
                    new UpdateForm
                    {
                        Id = r.Id,
                        Priority = Priority.High,
                        Status = RequestStatus.Submitted,
                        AssignedRole = r.AssignedRole,
                        RowVersion =
                            Convert.ToBase64String(
                                r.RowVersion)
                    }));


        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () =>
                service.UpdateAsync(
                    new UpdateForm
                    {
                        Id = r.Id,
                        Priority = Priority.Urgent,
                        Status = RequestStatus.Assigned,
                        AssignedRole = r.AssignedRole,
                        RowVersion = originalVersion
                    }));


        db.ChangeTracker.Clear();


        db.Requests.Add(
            new ProblemRequest
            {
                ReferenceCode = r.ReferenceCode,
                Category = Category.Other,
                Location = "Duplicate test",
                ResidentEmail = "duplicate@example.com",
                AssignedRole = "Municipal support desk",
                AssignedPerson = "To be assigned",
                ReportedAt = DateTimeOffset.UtcNow,
                LastUpdatedAt = DateTimeOffset.UtcNow
            });


        await Assert.ThrowsAsync<DbUpdateException>(
            () => db.SaveChangesAsync());
    }


    [Fact]
    public async Task ReferenceCollisionRetriesWithoutLosingInitialHistory()
    {
        using var scope =
            fixture.App.Services.CreateScope();


        var db =
            scope.ServiceProvider
                .GetRequiredService<ApplicationDbContext>();

        var assignments =
            scope.ServiceProvider
                .GetRequiredService<AssignmentService>();

        var email =
            scope.ServiceProvider
                .GetRequiredService<EmailService>();


        var first =
            await new RequestService(
                db,
                assignments,
                email)
            .SubmitAsync(
                new ReportForm
                {
                    Category = Category.Other,

                    Location =
                        "[FICTIONAL TEST] Original",

                    ResidentEmail =
                        "first@example.com"
                });


        var second =
            await new CollisionService(
                db,
                assignments,
                email,
                first.ReferenceCode)
            .SubmitAsync(
                new ReportForm
                {
                    Category = Category.Other,

                    Location =
                        "[FICTIONAL TEST] Retry",

                    ResidentEmail =
                        "second@example.com"
                });


        Assert.NotEqual(
            first.ReferenceCode,
            second.ReferenceCode);


        db.ChangeTracker.Clear();


        var persisted =
            await db.Requests
                .Include(x => x.History)
                .SingleAsync(
                    x => x.Id == second.Id);


        Assert.Single(
            persisted.History);

        Assert.Equal(
            RequestStatus.Submitted,
            persisted.Status);
    }


    [Fact]
    public async Task DemoSeedingIsSeparateLabelledAndRepeatable()
    {
        await SetupUtility.RunAsync(
            fixture.App.Services,
            new ConfigurationBuilder().Build(),
            true);


        await SetupUtility.RunAsync(
            fixture.App.Services,
            new ConfigurationBuilder().Build(),
            true);


        using var scope =
            fixture.App.Services.CreateScope();


        var db =
            scope.ServiceProvider
                .GetRequiredService<ApplicationDbContext>();


        var seeded =
            await db.Requests
                .Where(x =>
                    x.Location
                        .StartsWith("[FICTIONAL DEMO]"))
                .ToListAsync();


        Assert.Equal(
            7,
            seeded.Count);


        Assert.Equal(
            7,
            seeded.Select(x => x.Category)
                .Distinct()
                .Count());


        Assert.All(
            seeded,
            x => Assert.Contains(
                "Fictional",
                x.Description!));


        Assert.Equal(
            1,
            await db.Users.CountAsync());
    }


    [Fact]
    public async Task AdminSortsFiltersAndRejectsTamperedUpdates()
    {
        ProblemRequest oldest;
        ProblemRequest urgent;


        using (var scope =
            fixture.App.Services.CreateScope())
        {
            var db =
                scope.ServiceProvider
                    .GetRequiredService<ApplicationDbContext>();

            var service =
                scope.ServiceProvider
                    .GetRequiredService<RequestService>();


            oldest =
                await service.SubmitAsync(
                    new ReportForm
                    {
                        Category = Category.WaterLeak,

                        Location =
                            "[FICTIONAL TEST] Older",

                        ResidentEmail =
                            "oldest@example.com"
                    });


            oldest.ReportedAt =
                DateTimeOffset.UtcNow.AddDays(-10);

            await db.SaveChangesAsync();


            urgent =
                await service.SubmitAsync(
                    new ReportForm
                    {
                        Category = Category.WaterLeak,

                        Location =
                            "[FICTIONAL TEST] Urgent",

                        ResidentEmail =
                            "urgent@example.com"
                    });


            await service.UpdateAsync(
                new UpdateForm
                {
                    Id = urgent.Id,
                    Priority = Priority.Urgent,
                    Status = urgent.Status,
                    AssignedRole = urgent.AssignedRole,
                    AssignedPerson = urgent.AssignedPerson,
                    RowVersion =
                        Convert.ToBase64String(
                            urgent.RowVersion)
                });
        }


        using var admin =
            await Admin();


        var page =
            await admin.GetStringAsync(
                "/Admin?category=3");


        Assert.True(
            page.IndexOf(
                urgent.ReferenceCode,
                StringComparison.Ordinal)
            <
            page.IndexOf(
                oldest.ReferenceCode,
                StringComparison.Ordinal));


        var sorted =
            await admin.GetStringAsync(
                "/Admin?category=3&sort=oldest");


        Assert.True(
            sorted.IndexOf(
                oldest.ReferenceCode,
                StringComparison.Ordinal)
            <
            sorted.IndexOf(
                urgent.ReferenceCode,
                StringComparison.Ordinal));


        var filtered =
            await admin.GetStringAsync(
                "/Admin?category=3&priority=3&status=0");


        Assert.Contains(
            urgent.ReferenceCode,
            filtered);


        Assert.DoesNotContain(
            oldest.ReferenceCode,
            filtered);


        foreach (var change in new[]
        {
            (priority: "99", status: "0"),
            (priority: "3", status: "99"),
            (priority: "3", status: "3"),
            (priority: "", status: "")
        })
        {
            var edit =
                await admin.GetStringAsync(
                    $"/Admin/Edit/{oldest.Id}");


            var data =
                new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] =
                        Field(
                            edit,
                            "__RequestVerificationToken"),

                    ["Id"] =
                        oldest.Id.ToString(),

                    ["RowVersion"] =
                        Field(
                            edit,
                            "RowVersion"),

                    ["Priority"] =
                        change.priority,

                    ["Status"] =
                        change.status,

                    ["AssignedRole"] =
                        oldest.AssignedRole,

                    ["AssignedPerson"] =
                        oldest.AssignedPerson
                };


            var response =
                await admin.PostAsync(
                    "/Admin/Update",
                    new FormUrlEncodedContent(data));


            Assert.Equal(
                HttpStatusCode.Redirect,
                response.StatusCode);


            var errorPage =
                await admin.GetStringAsync(
                    response.Headers.Location);


            Assert.Contains(
                "role=\"alert\"",
                errorPage);
        }


        using var finalScope =
            fixture.App.Services.CreateScope();


        var persisted =
            await finalScope.ServiceProvider
                .GetRequiredService<ApplicationDbContext>()
                .Requests
                .AsNoTracking()
                .SingleAsync(
                    x => x.Id == oldest.Id);


        Assert.Equal(
            Priority.Normal,
            persisted.Priority);

        Assert.Equal(
            RequestStatus.Submitted,
            persisted.Status);


        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await admin.GetAsync(
                "/Admin?category=99"))
            .StatusCode);
    }
}
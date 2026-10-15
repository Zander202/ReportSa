using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Municipal.Web.Data;
using Municipal.Web.Models;
using Municipal.Web.Services;

namespace Municipal.Tests;

public class PhotoValidationTests
{
    public static byte[] Image => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jf1sAAAAASUVORK5CYII=");
    public static IFormFile File(byte[] content, string name = "photo.png") => new FormFile(new MemoryStream(content), 0, content.Length, "Photos", name);

    [Fact] public async Task RejectsDisguisedActiveContentAndTooManyPhotos()
    {
        var service = new PhotoUploadService();
        await Assert.ThrowsAsync<ArgumentException>(() => service.PrepareAsync([File("<svg onload='alert(1)'/>"u8.ToArray(), "fake.png")]));
        await Assert.ThrowsAsync<ArgumentException>(() => service.PrepareAsync(Enumerable.Range(0, 4).Select(_ => File(Image))));
        await Assert.ThrowsAsync<ArgumentException>(() => service.PrepareAsync([File(new byte[PhotoUploadService.MaxBytes + 1])]));
    }

    [Fact] public async Task UsesActualImageTypeRatherThanFileExtension()
    {
        var photos = await new PhotoUploadService().PrepareAsync([File(Image, "photo.txt")]);
        Assert.Equal("image/png", photos.Single().ContentType);
        Assert.Equal(Image, photos.Single().Content);
    }
}

public class CommunityWorkflowTests(SqlFixture fixture) : IClassFixture<SqlFixture>
{
    [Fact] public async Task DashboardPagesKeepTotalsAndStableOrderWithoutDroppingReports()
    {
        using var scope = fixture.App.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<ApplicationDbContext>();
        var code = Guid.NewGuid().ToString("N")[..10];
        var reported = DateTimeOffset.UtcNow;
        var reports = Enumerable.Range(0, 27).Select(_ => new ProblemRequest
        {
            ReferenceCode = RequestService.NewReference(), MunicipalityCode = code,
            MunicipalityName = "Pagination test", ReportedAt = reported, LastUpdatedAt = reported,
            Category = Category.Pothole, Status = RequestStatus.Submitted
        }).ToArray();
        db.Requests.AddRange(reports);
        await db.SaveChangesAsync();
        var controller = new Municipal.Web.Controllers.AdminController(db,
            services.GetRequiredService<RequestService>(),
            services.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Microsoft.AspNetCore.Identity.IdentityUser>>(),
            services.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<Microsoft.AspNetCore.Identity.IdentityRole>>());
        async Task<List<ProblemRequest>> Page(int page)
        {
            var result = Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(await controller.Index(
                Category.Pothole, null, null, municipalityCode: code, page: page));
            return Assert.IsType<List<ProblemRequest>>(result.Model);
        }
        var first = await Page(1);
        var second = await Page(2);
        Assert.Equal(25, first.Count);
        Assert.Equal(2, second.Count);
        Assert.Equal(27, controller.ViewData["TotalCount"]);
        Assert.Equal(27, controller.ViewData["ActiveCount"]);
        Assert.Equal(reports.Select(r => r.Id).Order(), first.Concat(second).Select(r => r.Id));
        Assert.Equal(second.Select(r => r.Id), (await Page(int.MaxValue)).Select(r => r.Id));
        Assert.Equal(first.Select(r => r.Id), (await Page(-1)).Select(r => r.Id));
    }

    private static ReportForm Report() => new() { MunicipalityCode = "CPT", Category = Category.WaterLeak,
        Location = "Private test location", ResidentEmail = "owner@example.invalid", Latitude = -33.9249, Longitude = 18.4241,
        Photos = [PhotoValidationTests.File(PhotoValidationTests.Image)] };

    [Fact] public async Task SupportIsDeduplicatedAndPublicDataDoesNotExposePrivateReportDetails()
    {
        using var scope = fixture.App.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<RequestService>();
        var community = scope.ServiceProvider.GetRequiredService<CommunityService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var report = await service.SubmitAsync(Report());
        Assert.True(await community.SupportAsync(new() { RequestId = report.Id, Email = "neighbour@example.invalid" }));
        Assert.False(await community.SupportAsync(new() { RequestId = report.Id, Email = "NEIGHBOUR@example.invalid" }));
        Assert.False(await community.SupportAsync(new() { RequestId = report.Id, Email = report.ResidentEmail }));
        Assert.Equal(1, await db.RequestSupporters.CountAsync(s => s.ProblemRequestId == report.Id));
        var nearby = await community.NearbyAsync("CPT", Category.WaterLeak, -33.9249, 18.4241);
        Assert.Equal(2, nearby.Single(r => r.Id == report.Id).Affected);
        using var client = fixture.Client();
        var json = await client.GetStringAsync("/Requests/NearbyData?municipalityCode=CPT");
        Assert.DoesNotContain(report.ResidentEmail, json);
        Assert.DoesNotContain(report.ReferenceCode, json);
        Assert.DoesNotContain(report.Location, json);
        var photo = await db.RequestPhotos.SingleAsync(p => p.ProblemRequestId == report.Id);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync($"/Requests/Photo/{photo.Id}")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync($"/Requests/Photo/{photo.Id}?referenceCode={report.ReferenceCode}")).StatusCode);
    }

    [Fact] public async Task RepairEvidenceReviewAndReopeningPersistInTheTimeline()
    {
        using var scope = fixture.App.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<RequestService>();
        var community = scope.ServiceProvider.GetRequiredService<CommunityService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var report = await service.SubmitAsync(Report());
        async Task Advance(RequestStatus status, bool photo = false) => await service.UpdateAsync(new()
        {
            Id = report.Id, RowVersion = Convert.ToBase64String(report.RowVersion), Status = status,
            Priority = report.Priority, AssignedRole = report.AssignedRole, AssignedPerson = report.AssignedPerson,
            ProgressNote = "Team visited the location.", ResolutionNotes = status == RequestStatus.Resolved ? "Leak repaired." : null,
            ResolutionPhotos = photo ? [PhotoValidationTests.File(PhotoValidationTests.Image)] : []
        });
        await Advance(RequestStatus.Assigned);
        await Advance(RequestStatus.InProgress);
        await Advance(RequestStatus.Resolved, photo: true);
        Assert.NotNull(report.ResolvedAt);
        Assert.Equal(1, await db.RequestPhotos.CountAsync(p => p.ProblemRequestId == report.Id && p.IsResolution));
        await Assert.ThrowsAsync<ArgumentException>(() => community.ReviewAsync(new() { ReferenceCode = report.ReferenceCode,
            Email = "stranger@example.invalid", Confirmed = true }));
        await community.ReviewAsync(new() { ReferenceCode = report.ReferenceCode, Email = report.ResidentEmail, Confirmed = false, Note = "Still leaking." });
        Assert.Equal(RequestStatus.InProgress, report.Status);
        Assert.Equal(ResidentReview.NotFixed, report.ResidentReview);
        Assert.Null(report.ResolvedAt);
        await Advance(RequestStatus.Resolved);
        await community.ReviewAsync(new() { ReferenceCode = report.ReferenceCode, Email = report.ResidentEmail, Confirmed = true });
        Assert.Equal(RequestStatus.Closed, report.Status);
        Assert.Equal(ResidentReview.Confirmed, report.ResidentReview);
        Assert.Contains(await db.StatusHistory.Where(h => h.ProblemRequestId == report.Id).ToListAsync(), h => h.Note != null && h.Note.Contains("Still leaking"));
        var metrics = await community.DashboardAsync("CPT");
        Assert.True(metrics.Resolved >= 1);
        Assert.NotNull(metrics.AverageDays);
        using var admin = fixture.Client();
        var login = await admin.GetStringAsync("/Account/Login");
        var token = System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(login,
            "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(token);
        var signedIn = await admin.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["Email"] = fixture.Email, ["Password"] = fixture.Password
        }));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, signedIn.StatusCode);
        var dashboard = await admin.GetStringAsync("/Admin?tab=resolved");
        Assert.Contains("Before / After", dashboard);
        Assert.Contains("alt=\"Before repair\"", dashboard);
        Assert.Contains("alt=\"After repair\"", dashboard);
        var proof = await db.RequestPhotos.FirstAsync(p => p.ProblemRequestId == report.Id && p.IsResolution);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await admin.GetAsync($"/Requests/Photo/{proof.Id}")).StatusCode);
    }

    [Fact] public async Task AdminCanCloseCompletedTicketAndRepeatedClosureDoesNotSendAgain()
    {
        using var scope = fixture.App.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<RequestService>();
        var report = await service.SubmitAsync(Report());
        var form = new UpdateForm
        {
            Id = report.Id, RowVersion = Convert.ToBase64String(report.RowVersion), CloseTicket = true,
            Status = report.Status, Priority = report.Priority, AssignedRole = report.AssignedRole,
            AssignedPerson = report.AssignedPerson, ResolutionNotes = "Work completed."
        };
        Assert.Equal(EmailDeliveryStatus.Disabled, await service.UpdateAsync(form));
        var closed = await service.TrackAsync(report.ReferenceCode);
        Assert.Equal(RequestStatus.Closed, closed!.Status);
        Assert.NotNull(closed.ResolvedAt);
        Assert.Equal(2, closed.History.Count);
        form.RowVersion = Convert.ToBase64String(closed.RowVersion);
        Assert.Null(await service.UpdateAsync(form));
        Assert.Equal(2, (await service.TrackAsync(report.ReferenceCode))!.History.Count);
    }

    [Fact] public async Task ClosingFromManageReturnsToDashboardAndPersistsClosed()
    {
        ProblemRequest report;
        using (var scope = fixture.App.Services.CreateScope())
            report = await scope.ServiceProvider.GetRequiredService<RequestService>().SubmitAsync(Report());
        using var admin = fixture.Client();
        static string Token(string html) => System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(html,
            "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var login = await admin.GetStringAsync("/Account/Login");
        var signedIn = await admin.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token(login), ["Email"] = fixture.Email, ["Password"] = fixture.Password
        }));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, signedIn.StatusCode);
        var edit = await admin.GetStringAsync($"/Admin/Edit/{report.Id}");
        Assert.Contains("Close ticket and email reporter", edit);
        var response = await admin.PostAsync("/Admin/Update", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token(edit), ["Id"] = report.Id.ToString(),
            ["RowVersion"] = Convert.ToBase64String(report.RowVersion), ["CloseTicket"] = "true",
            ["Status"] = ((int)report.Status).ToString(), ["Priority"] = ((int)report.Priority).ToString(),
            ["AssignedRole"] = report.AssignedRole, ["AssignedPerson"] = report.AssignedPerson,
            ["ResolutionNotes"] = "Work completed."
        }));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin", response.Headers.Location!.OriginalString);
        using var verificationScope = fixture.App.Services.CreateScope();
        var saved = await verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Requests
            .AsNoTracking().Include(r => r.History).SingleAsync(r => r.Id == report.Id);
        Assert.Equal(RequestStatus.Closed, saved.Status);
        Assert.NotNull(saved.ResolvedAt);
        Assert.Equal(RequestStatus.Closed, saved.History.OrderBy(h => h.ChangedAt).Last().Status);
        var active = await admin.GetStringAsync("/Admin");
        Assert.Contains("Ticket closed.", active);
        Assert.DoesNotContain(report.ReferenceCode, active);
        var resolved = await admin.GetStringAsync("/Admin?tab=resolved");
        Assert.Contains(report.ReferenceCode, resolved);
        Assert.Contains("Closed", resolved);
        Assert.Contains("Before repair", resolved);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await admin.GetAsync("/Admin?tab=invalid")).StatusCode);
    }
}

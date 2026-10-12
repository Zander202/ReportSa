using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Municipal.Web.Models;
using Municipal.Web.Services;
using Municipal.Web.Data;
using Microsoft.EntityFrameworkCore;
namespace Municipal.Web.Controllers;
public class RequestsController(RequestService service, MunicipalityDirectory municipalities) : Controller
{
    [HttpGet] public IActionResult Report() => View(new ReportForm());
    [HttpGet] public async Task<IActionResult> SearchAddresses(string? query,
        [FromServices] MunicipalityLocationService locationService, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(query) || query.Trim().Length < 3 || query.Trim().Length > 200)
            return BadRequest(new { message = "Enter at least three characters of the address." });
        try { return Json(await locationService.SearchAsync(query, cancellationToken)); }
        catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException)
        {
            return StatusCode(503, new { message = "Address search is unavailable. You can still enter the address manually." });
        }
    }
    [HttpGet] public async Task<IActionResult> LocateMunicipality(double? latitude, double? longitude,
        [FromServices] MunicipalityLocationService locationService, CancellationToken cancellationToken, double? accuracy = null)
    {
        if (latitude is not double lat || longitude is not double lon
            || !double.IsFinite(lat) || !double.IsFinite(lon) || lat < -90 || lat > 90 || lon < -180 || lon > 180)
            return BadRequest(new { message = "Choose a valid location." });
        try
        {
            var includeStreet = accuracy is double metres && double.IsFinite(metres) && metres >= 0 && metres <= 100;
            var place = await locationService.ResolveAsync(lat, lon, cancellationToken, includeStreet);
            if (place.Municipality is null && place.Address is null && place.Area is null)
                return NotFound(new { message = "We couldn't find your address or municipality. Try again or enter the location manually." });
            return Json(new
            {
                code = place.Municipality?.Code, label = place.Municipality?.Label, address = place.Address, area = place.Area,
                message = place.Municipality is null ? "Location found. Select ur municipality manually."
                    : place.Address is null ? "Municipality found. The street at this pin could not be verified. Enter the address or select a place suggestion." : null
            });
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException)
        {
            return StatusCode(503, new { message = "Municipality lookup is unavailable. Select ur municipality below." });
        }
    }
    [HttpPost, RequestSizeLimit(16 * 1024 * 1024)] public async Task<IActionResult> Report(ReportForm form)
    {
        if (string.IsNullOrWhiteSpace(form.Location)) ModelState.AddModelError(nameof(form.Location), "Enter a location or use Locate me.");
        if (municipalities.Find(form.MunicipalityCode) is null) ModelState.AddModelError(nameof(form.MunicipalityCode), "Select your municipality.");
        if (form.Latitude.HasValue != form.Longitude.HasValue) ModelState.AddModelError(nameof(form.Location), "Try locating your position again, or enter the location manually.");
        if (!ModelState.IsValid) return View(form);
        ProblemRequest request;
        try { request = await service.SubmitAsync(form); }
        catch (ArgumentException error) { ModelState.AddModelError("", error.Message); return View(form); }
        TempData["SubmittedReference"] = request.ReferenceCode;
        return RedirectToAction(nameof(Confirmation));
    }
    [HttpGet] public async Task<IActionResult> Confirmation()
    {
        if (TempData["SubmittedReference"] is not string code) return RedirectToAction(nameof(Report));
        var request = await service.TrackAsync(code);
        if (request is null) return RedirectToAction(nameof(Report));
        return View(request);
    }
    [HttpGet] public async Task<IActionResult> Track(string? referenceCode)
    {
        var form = new TrackForm { ReferenceCode = referenceCode ?? "" };
        if (!string.IsNullOrWhiteSpace(referenceCode)) form.Request = await service.TrackAsync(referenceCode);
        return View(form);
    }
    [HttpPost] public async Task<IActionResult> Track(TrackForm form)
    {
        if (!ModelState.IsValid) return View(form);
        form.Request = await service.TrackAsync(form.ReferenceCode);
        if (form.Request is null) ModelState.AddModelError(nameof(form.ReferenceCode), "No request matches that reference. Check your code and try again.");
        return View(form);
    }
    [HttpGet] public async Task<IActionResult> Nearby([FromServices] CommunityService community, string? municipalityCode, Category? category)
    {
        if (!ModelState.IsValid || category.HasValue && !Enum.IsDefined(category.Value)
            || !string.IsNullOrEmpty(municipalityCode) && municipalities.Find(municipalityCode) is null) return BadRequest();
        return View(new NearbyPage { MunicipalityCode = municipalityCode, Category = category,
            Reports = await community.NearbyAsync(municipalityCode, category) });
    }
    [HttpGet] public async Task<IActionResult> NearbyData([FromServices] CommunityService community,
        string? municipalityCode, Category? category, double? latitude, double? longitude)
    {
        if (!ModelState.IsValid || category.HasValue && !Enum.IsDefined(category.Value)
            || latitude.HasValue != longitude.HasValue || latitude is < -90 or > 90 || longitude is < -180 or > 180
            || latitude.HasValue && (!double.IsFinite(latitude.Value) || !double.IsFinite(longitude!.Value))
            || !string.IsNullOrEmpty(municipalityCode) && municipalities.Find(municipalityCode) is null) return BadRequest();
        return Json((await community.NearbyAsync(municipalityCode, category, latitude, longitude)).Select(r => new
        {
            r.Id, category = Labels.CategoryName(r.Category), status = Labels.Status(r.Status), r.Municipality,
            r.Affected, r.Latitude, r.Longitude, r.DistanceKm, reportedAt = r.ReportedAt.ToString("yyyy-MM-dd")
        }));
    }
    [HttpPost] public async Task<IActionResult> Support(SupportForm form, [FromServices] CommunityService community)
    {
        if (!ModelState.IsValid) { TempData["Error"] = "Enter a valid email address."; return RedirectToAction(nameof(Nearby)); }
        try { TempData["Success"] = await community.SupportAsync(form) ? "Added: you're affected by this problem too." : "You're already counted on this report."; }
        catch (ArgumentException error) { TempData["Error"] = error.Message; }
        catch (KeyNotFoundException) { return NotFound(); }
        return RedirectToAction(nameof(Nearby));
    }
    [HttpPost] public async Task<IActionResult> Review(ReviewForm form, [FromServices] CommunityService community)
    {
        if (!ModelState.IsValid) TempData["Error"] = "Check your email and feedback.";
        else
        {
            try
            {
                await community.ReviewAsync(form);
                TempData["Success"] = form.Confirmed ? "Repair confirmed. Your report is now closed." : "The report is back in progress for the team to review.";
            }
            catch (ArgumentException error) { TempData["Error"] = error.Message; }
            catch (KeyNotFoundException) { return NotFound(); }
            catch (DbUpdateConcurrencyException) { TempData["Error"] = "This report changed. Review the latest status."; }
        }
        return RedirectToAction(nameof(Track), new { referenceCode = form.ReferenceCode });
    }
    [HttpGet] public async Task<IActionResult> Photo(int id, string? referenceCode, [FromServices] ApplicationDbContext db)
    {
        var admin = User.IsInRole("Administrator");
        var photo = await db.RequestPhotos.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id
            && (admin || p.Request.ReferenceCode == referenceCode));
        if (photo is null) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cache-Control"] = "private, no-store";
        return File(photo.Content, photo.ContentType);
    }
    [Route("Requests/Error")] public IActionResult Error() { Response.StatusCode = 500; return View(); }
}

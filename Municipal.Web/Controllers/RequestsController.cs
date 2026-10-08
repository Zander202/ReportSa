using Microsoft.AspNetCore.Mvc;
using Municipal.Web.Models;
using Municipal.Web.Services;
namespace Municipal.Web.Controllers;
public class RequestsController(RequestService service, MunicipalityDirectory municipalities) : Controller
{
    [HttpGet] public IActionResult Report() => View(new ReportForm());
    [HttpPost] public async Task<IActionResult> Report(ReportForm form)
    {
        if (string.IsNullOrWhiteSpace(form.Location)) ModelState.AddModelError(nameof(form.Location), "Enter a location or address.");
        if (municipalities.Find(form.MunicipalityCode) is null) ModelState.AddModelError(nameof(form.MunicipalityCode), "Select your municipality.");
        if (form.Latitude.HasValue != form.Longitude.HasValue) ModelState.AddModelError(nameof(form.Location), "Try locating your position again, or enter the address manually.");
        if (!ModelState.IsValid) return View(form);
        var request = await service.SubmitAsync(form);
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
    [Route("Requests/Error")] public IActionResult Error() { Response.StatusCode = 500; return View(); }
}

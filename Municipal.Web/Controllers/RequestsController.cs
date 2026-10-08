using Microsoft.AspNetCore.Mvc;
using Municipal.Web.Models;
using Municipal.Web.Services;
namespace Municipal.Web.Controllers;
public class RequestsController(RequestService service) : Controller
{
    [HttpGet] public IActionResult Report() => View(new ReportForm());
    [HttpPost] public async Task<IActionResult> Report(ReportForm form)
    {
        if (string.IsNullOrWhiteSpace(form.Location)) ModelState.AddModelError(nameof(form.Location), "Enter a location or address.");
        if (!ModelState.IsValid) return View(form);
        var request = await service.SubmitAsync(form);
        TempData["SubmittedReference"] = request.ReferenceCode;
        return RedirectToAction(nameof(Confirmation));
    }
    [HttpGet] public IActionResult Confirmation()
    {
        if (TempData["SubmittedReference"] is not string code) return RedirectToAction(nameof(Report));
        return View(model: code);
    }
    [HttpGet] public IActionResult Track() => View(new TrackForm());
    [HttpPost] public async Task<IActionResult> Track(TrackForm form)
    {
        if (!ModelState.IsValid) return View(form);
        form.Request = await service.TrackAsync(form.ReferenceCode);
        if (form.Request is null) ModelState.AddModelError(nameof(form.ReferenceCode), "No request matches that reference. Check your code and try again.");
        return View(form);
    }
    [Route("Requests/Error")] public IActionResult Error() { Response.StatusCode = 500; return View(); }
}

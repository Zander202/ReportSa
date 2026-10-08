using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Municipal.Web.Data;
using Municipal.Web.Models;
using Municipal.Web.Services;
namespace Municipal.Web.Controllers;
[Authorize(Roles = "Administrator")]
public class AdminController(ApplicationDbContext db, RequestService service) : Controller
{
    [HttpGet] public async Task<IActionResult> Index(Category? category, Priority? priority, RequestStatus? status, string sort = "priority")
    {
        if (!ModelState.IsValid || (category.HasValue && !Enum.IsDefined(category.Value)) || (priority.HasValue && !Enum.IsDefined(priority.Value)) || (status.HasValue && !Enum.IsDefined(status.Value))) return BadRequest("Invalid filter.");
        if (sort is not ("priority" or "oldest" or "newest")) return BadRequest("Invalid sort.");
        var query = db.Requests.AsNoTracking().AsQueryable();
        if (category.HasValue) query = query.Where(x => x.Category == category);
        if (priority.HasValue) query = query.Where(x => x.Priority == priority);
        if (status.HasValue) query = query.Where(x => x.Status == status);
        query = sort switch { "oldest" => query.OrderBy(x => x.ReportedAt).ThenByDescending(x => x.Priority), "newest" => query.OrderByDescending(x => x.ReportedAt).ThenByDescending(x => x.Priority), _ => query.OrderByDescending(x => x.Priority).ThenBy(x => x.ReportedAt) };
        ViewBag.Category = category; ViewBag.Priority = priority; ViewBag.Status = status; ViewBag.Sort = sort;
        return View(await query.ToListAsync());
    }
    [HttpGet] public async Task<IActionResult> Edit(int id)
    {
        var r = await db.Requests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        return r is null ? NotFound() : View(r);
    }
    [HttpPost] public async Task<IActionResult> Update(UpdateForm form)
    {
        if (!ModelState.IsValid) { TempData["Error"] = "Choose valid values for all fields."; return RedirectToAction(nameof(Edit), new { id = form.Id }); }
        try { await service.UpdateAsync(form); TempData["Success"] = "Request updated."; }
        catch (ArgumentException ex) { TempData["Error"] = ex.Message; }
        catch (DbUpdateConcurrencyException) { TempData["Error"] = "Another update changed this request. Review the latest values and try again."; }
        catch (KeyNotFoundException) { return NotFound(); }
        return RedirectToAction(nameof(Edit), new { id = form.Id });
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Municipal.Web.Data;
using Municipal.Web.Models;
using Municipal.Web.Services;

namespace Municipal.Web.Controllers;

[Authorize(Roles = "Administrator")]
public class AdminController(
    ApplicationDbContext db,
    RequestService service,
    UserManager<IdentityUser> users,
    RoleManager<IdentityRole> roles) : Controller
{
    //testing
    [HttpGet]
    public async Task<IActionResult> Index(
        Category? category,
        Priority? priority,
        RequestStatus? status,
        string sort = "priority", string? municipalityCode = null, bool overdue = false, string tab = "active", int page = 1)
    {
        if (!ModelState.IsValid ||
            (category.HasValue && !Enum.IsDefined(category.Value)) ||
            (priority.HasValue && !Enum.IsDefined(priority.Value)) ||
            (status.HasValue && !Enum.IsDefined(status.Value)))
            return BadRequest("Invalid filter.");

        if (sort is not ("priority" or "oldest" or "newest"))
            return BadRequest("Invalid sort.");
        if (tab is not ("active" or "resolved")) return BadRequest("Invalid ticket tab.");

        var query = db.Requests.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(municipalityCode)) query = query.Where(x => x.MunicipalityCode == municipalityCode);
        query = tab == "resolved" ? query.Where(x => x.Status >= RequestStatus.Resolved)
            : query.Where(x => x.Status < RequestStatus.Resolved);
        if (overdue) query = query.Where(x => x.Status < RequestStatus.Resolved && x.EstimatedCompletionAt < DateTimeOffset.UtcNow);

        if (category.HasValue)
            query = query.Where(x => x.Category == category);

        if (priority.HasValue)
            query = query.Where(x => x.Priority == priority);

        if (status.HasValue)
            query = query.Where(x => x.Status == status);

        query = sort switch
        {
            "oldest" => query.OrderBy(x => x.ReportedAt)
                             .ThenByDescending(x => x.Priority),

            "newest" => query.OrderByDescending(x => x.ReportedAt)
                             .ThenByDescending(x => x.Priority),

            _ => query.OrderByDescending(x => x.Priority)
                      .ThenBy(x => x.ReportedAt)
        };

        ViewBag.Category = category;
        ViewBag.Priority = priority;
        ViewBag.Status = status;
        ViewBag.Sort = sort;
        ViewBag.Tab = tab;
        ViewBag.MunicipalityCode = municipalityCode;
        ViewBag.Overdue = overdue;
        var metrics = await new CommunityService(db).DashboardAsync(municipalityCode);
        ViewBag.Metrics = metrics;
        ViewBag.ActiveCount = metrics.Open;
        ViewBag.ResolvedCount = metrics.Resolved;

        const int pageSize = 25;
        var total = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        ViewBag.Page = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.TotalCount = total;
        var requests = await ((IOrderedQueryable<ProblemRequest>)query).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var ids = requests.Select(r => r.Id).ToList();
        var photos = await db.RequestPhotos.AsNoTracking().Where(p => ids.Contains(p.ProblemRequestId))
            .Select(p => new RequestPhoto { Id = p.Id, ProblemRequestId = p.ProblemRequestId,
                FileName = p.FileName, IsResolution = p.IsResolution }).ToListAsync();
        var byRequest = photos.ToLookup(p => p.ProblemRequestId);
        foreach (var request in requests) request.Photos = byRequest[request.Id].ToList();
        return View(requests);
    }

    // =========================
    // CREATE USER
    // =========================

    [HttpGet]
    public IActionResult CreateUser()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser(
        string email,
        string password)
    {
        email = email?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password))
        {
            ModelState.AddModelError(
                "",
                "Email and password are required.");

            return View();
        }

        var existingUser = await users.FindByEmailAsync(email);

        if (existingUser != null)
        {
            ModelState.AddModelError(
                "",
                "A user with this email already exists.");

            return View();
        }

        var user = new IdentityUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };

        var result = await users.CreateAsync(user, password);

        if (result.Succeeded)
        {
            if (!await roles.RoleExistsAsync("Administrator"))
            {
                var roleResult = await roles.CreateAsync(new IdentityRole("Administrator"));
                if (!roleResult.Succeeded)
                {
                    foreach (var error in roleResult.Errors)
                    {
                        ModelState.AddModelError("", error.Description);
                    }

                    return View();
                }
            }

            var roleAssignment = await users.AddToRoleAsync(user, "Administrator");
            if (!roleAssignment.Succeeded)
            {
                foreach (var error in roleAssignment.Errors)
                {
                    ModelState.AddModelError("", error.Description);
                }

                return View();
            }

            TempData["Success"] = $"Administrator {email} created successfully.";

            return RedirectToAction(nameof(Index));
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError("", error.Description);
        }

        return View();
    }

    // =========================
    // EDIT REQUEST
    // =========================

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var r = await db.Requests
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id);

        if (r is not null) r.Photos = await db.RequestPhotos.AsNoTracking().Where(p => p.ProblemRequestId == r.Id)
            .Select(p => new RequestPhoto { Id = p.Id, IsResolution = p.IsResolution, FileName = p.FileName }).ToListAsync();
        return r is null ? NotFound() : View(r);
    }

    [HttpPost, RequestSizeLimit(16 * 1024 * 1024)]
    public async Task<IActionResult> Update(UpdateForm form)
    {
        if (form.CloseTicket) form.Status = RequestStatus.Closed;
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Choose valid values for all fields.";
            return RedirectToAction(nameof(Edit), new { id = form.Id });
        }

        try
        {
            var delivery = await service.UpdateAsync(form);
            var message = form.Status == RequestStatus.Closed ? "Ticket closed." : "Request updated.";
            TempData["Success"] = delivery == EmailDeliveryStatus.Sent
                ? message + " The reporter has been emailed." : message;
            if (delivery == EmailDeliveryStatus.Failed) TempData["Error"] = "The report was saved, but the reporter's email could not be delivered.";
            if (delivery == EmailDeliveryStatus.Disabled && form.Status >= RequestStatus.Resolved)
                TempData["Error"] = "The ticket was saved, but email notifications are not configured for this report.";
            return RedirectToAction(nameof(Index));
        }
        catch (ArgumentException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["Error"] =
                "Another update changed this request. Review the latest values and try again.";
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }

        return RedirectToAction(nameof(Edit), new { id = form.Id });
    }
}

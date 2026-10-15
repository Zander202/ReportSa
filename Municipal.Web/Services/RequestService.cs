using System.Net.Mail;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Municipal.Web.Data;
using Municipal.Web.Models;
namespace Municipal.Web.Services;

public class RequestService
{
    private readonly ApplicationDbContext db;
    private readonly AssignmentService assignments;
    private readonly EmailService email;
    private readonly MunicipalityDirectory municipalities;

    public RequestService(ApplicationDbContext db, AssignmentService assignments, EmailService email, MunicipalityDirectory? municipalities = null)
    {
        this.db = db;
        this.assignments = assignments;
        this.email = email;
        this.municipalities = municipalities ?? new MunicipalityDirectory();
    }

    public static string NewReference() => "MUN-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
    protected virtual string GenerateReference() => NewReference();
    private static byte[] NewVersion() => RandomNumberGenerator.GetBytes(16);

    private static bool IsValidEmail(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 254 &&
        MailAddress.TryCreate(value.Trim(), out var addr) && addr.Address == value.Trim();

    public async Task<ProblemRequest> SubmitAsync(ReportForm form)
    {
        if (form.Category is null || !Enum.IsDefined(form.Category.Value)
            || string.IsNullOrWhiteSpace(form.Location) || form.Location.Trim().Length > 200
            || form.Description?.Length > 1000
            || !IsValidEmail(form.ResidentEmail)
            || !double.IsFinite(form.Latitude ?? 0) || !double.IsFinite(form.Longitude ?? 0)
            || form.Latitude is < -90 or > 90 || form.Longitude is < -180 or > 180
            || form.Latitude.HasValue != form.Longitude.HasValue)
            throw new ArgumentException("Invalid report.");

        var assignment = assignments.For(form.Category.Value);
        var municipality = municipalities.Find(form.MunicipalityCode)
            ?? throw new ArgumentException("Select a valid municipality.");
        var photos = await new PhotoUploadService().PrepareAsync(form.Photos);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var now = DateTimeOffset.UtcNow;
            var request = new ProblemRequest
            {
                ReferenceCode = GenerateReference(),
                Category = form.Category.Value,
                Location = form.Location.Trim(),
                Description = string.IsNullOrWhiteSpace(form.Description) ? null : form.Description.Trim(),
                ResidentEmail = form.ResidentEmail.Trim(),
                MunicipalityCode = municipality.Code,
                MunicipalityName = municipality.Label,
                Latitude = form.Latitude,
                Longitude = form.Longitude,
                EstimatedCompletionAt = AssignmentService.Estimate(form.Category.Value, now),
                AssignedRole = assignment.Role,
                AssignedPerson = assignment.Person,
                ReportedAt = now,
                LastUpdatedAt = now,
                Priority = Priority.Normal,
                Status = RequestStatus.Submitted,
                RowVersion = db.Database.IsNpgsql() ? NewVersion() : [],
                History = [new StatusHistory { Status = RequestStatus.Submitted, ChangedAt = now, Note = "Report received." }],
                Photos = photos.Select(p => new RequestPhoto { FileName = p.FileName, ContentType = p.ContentType,
                    Content = p.Content, UploadedAt = p.UploadedAt }).ToList()
            };
            db.Requests.Add(request);
            try
            {
                await db.SaveChangesAsync();
                request.ConfirmationEmailStatus = await email.SendRequestReceivedAsync(request);
                await db.SaveChangesAsync();
                return request;
            }
            catch (DbUpdateException ex)
            {
                var pg = ex.InnerException as Npgsql.PostgresException;
                if (pg?.SqlState == "23505")
                {
                    db.ChangeTracker.Clear();
                    continue; // retry loop
                }
                if (ex.InnerException is SqlException { Number: 2601 or 2627 })
                {
                    db.ChangeTracker.Clear();
                    continue;
                }
                throw;
            }
        }
        throw new InvalidOperationException("Could not allocate a reference code. Please retry.");
    }

    // The receipt needs no history or photo metadata.
    public Task<ProblemRequest?> ReceiptAsync(string code) =>
        db.Requests.AsNoTracking().SingleOrDefaultAsync(x => x.ReferenceCode == code);

    public async Task<ProblemRequest?> TrackAsync(string code)
    {
        code = code.Trim().ToUpperInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^MUN-[0-9A-F]{24}$")) return null;
        var request = await db.Requests.AsNoTracking().Include(x => x.History).SingleOrDefaultAsync(x => x.ReferenceCode == code);
        if (request is not null) request.Photos = await db.RequestPhotos.AsNoTracking().Where(p => p.ProblemRequestId == request.Id)
            .Select(p => new RequestPhoto { Id = p.Id, FileName = p.FileName, ContentType = p.ContentType,
                IsResolution = p.IsResolution, UploadedAt = p.UploadedAt }).ToListAsync();
        return request;
    }

    public async Task<EmailDeliveryStatus?> UpdateAsync(UpdateForm form)
    {
        if (form.CloseTicket) form.Status = RequestStatus.Closed;
        if (!Enum.IsDefined(form.Priority) || !Enum.IsDefined(form.Status)) throw new ArgumentException("Invalid priority or status.");
        if (string.IsNullOrWhiteSpace(form.AssignedRole) || form.AssignedRole.Trim().Length > 120 || form.AssignedPerson?.Length > 120) throw new ArgumentException("Enter a valid assigned role or person.");
        if (form.ProgressNote?.Length > 1000 || form.ResolutionNotes?.Length > 1000) throw new ArgumentException("Keep notes within 1,000 characters.");
        var photos = await new PhotoUploadService().PrepareAsync(form.ResolutionPhotos, resolution: true);
        var request = await db.Requests.FindAsync(form.Id) ?? throw new KeyNotFoundException();
        if (form.Status != request.Status && (int)form.Status != (int)request.Status + 1 && form.Status != RequestStatus.Closed)
            throw new ArgumentException("Move to the next workflow stage or close the completed ticket.");
        if (photos.Count > 0 && form.Status < RequestStatus.Resolved) throw new ArgumentException("Attach repair evidence when marking the report resolved.");
        if (photos.Count + await db.RequestPhotos.CountAsync(p => p.ProblemRequestId == request.Id && p.IsResolution) > 3)
            throw new ArgumentException("A report can have up to three repair photos.");
        byte[] version;
        try { version = Convert.FromBase64String(form.RowVersion); } catch (FormatException) { throw new ArgumentException("Invalid request version."); }
        db.Entry(request).Property(x => x.RowVersion).OriginalValue = version;
        var assignedRole = form.AssignedRole.Trim();
        var assignedPerson = form.AssignedPerson?.Trim() ?? "";
        var estimate = form.EstimatedCompletionLocal is { } local
            ? new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeSpan.FromHours(2)).ToUniversalTime()
            : request.EstimatedCompletionAt;
        if (request.Status == form.Status && request.Priority == form.Priority && request.AssignedRole == assignedRole && request.AssignedPerson == assignedPerson && request.EstimatedCompletionAt == estimate
            && string.IsNullOrWhiteSpace(form.ProgressNote) && form.ResolutionNotes == request.ResolutionNotes && photos.Count == 0) return null;
        var now = DateTimeOffset.UtcNow;
        var statusChanged = request.Status != form.Status;
        var scheduleChanged = request.EstimatedCompletionAt != estimate;
        var assignmentChanged = request.AssignedPerson != assignedPerson || request.AssignedRole != assignedRole;
        if (statusChanged || scheduleChanged || assignmentChanged || !string.IsNullOrWhiteSpace(form.ProgressNote) || photos.Count > 0 || form.ResolutionNotes != request.ResolutionNotes)
            request.History.Add(new() { Status = form.Status, ChangedAt = now,
                Note = string.IsNullOrWhiteSpace(form.ProgressNote)
                    ? scheduleChanged ? $"Completion scheduled for {estimate?.ToOffset(TimeSpan.FromHours(2)):dd MMM yyyy, HH:mm} SAST."
                    : assignmentChanged ? $"Assigned to {assignedPerson} ({assignedRole})."
                    : photos.Count > 0 ? "Repair evidence added." : null : form.ProgressNote.Trim() });
        if (statusChanged && form.Status == RequestStatus.Resolved)
        {
            request.ResolvedAt = now;
            request.ResidentReview = ResidentReview.Awaiting;
        }
        if (statusChanged && form.Status == RequestStatus.Closed) request.ResolvedAt ??= now;
        request.ResolutionNotes = string.IsNullOrWhiteSpace(form.ResolutionNotes) ? null : form.ResolutionNotes.Trim();
        request.Photos.AddRange(photos);
        request.Status = form.Status; request.Priority = form.Priority; request.LastUpdatedAt = now;
        request.AssignedRole = assignedRole; request.AssignedPerson = assignedPerson;
        request.EstimatedCompletionAt = estimate;
        if (db.Database.IsNpgsql()) request.RowVersion = NewVersion();
        await db.SaveChangesAsync();
        if (statusChanged && form.Status == RequestStatus.Closed) return await email.SendClosedAsync(request);
        return statusChanged && form.Status == RequestStatus.Resolved
            ? await email.SendResolvedAsync(request) : await email.SendStatusUpdateAsync(request);
    }
}

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

    public RequestService(ApplicationDbContext db, AssignmentService assignments, EmailService email)
    {
        this.db = db;
        this.assignments = assignments;
        this.email = email;
    }

    public static string NewReference() => "MUN-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
    protected virtual string GenerateReference() => NewReference();

    private static bool IsValidEmail(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 254 &&
        MailAddress.TryCreate(value.Trim(), out var addr) && addr.Address == value.Trim();

    public async Task<ProblemRequest> SubmitAsync(ReportForm form)
    {
        if (form.Category is null || !Enum.IsDefined(form.Category.Value)
            || string.IsNullOrWhiteSpace(form.Location) || form.Location.Trim().Length > 200
            || form.Description?.Length > 1000
            || !IsValidEmail(form.ResidentEmail))
            throw new ArgumentException("Invalid report.");

        var assignment = assignments.For(form.Category.Value);

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
                // ASSUMPTION: adjust these two lines to match what AssignmentService.For() returns
                AssignedRole = assignment.Role,
                AssignedPerson = assignment.Person,
                ReportedAt = now,
                LastUpdatedAt = now,
                Priority = Priority.Normal,
                Status = RequestStatus.Submitted,
                History = [new StatusHistory { Status = RequestStatus.Submitted, ChangedAt = now }]
            };
            db.Requests.Add(request);
            try
            {
                await db.SaveChangesAsync();
                await email.SendRequestReceivedAsync(request);
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

    public async Task<ProblemRequest?> TrackAsync(string code)
    {
        code = code.Trim().ToUpperInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^MUN-[0-9A-F]{24}$")) return null;
        return await db.Requests.AsNoTracking().Include(x => x.History).SingleOrDefaultAsync(x => x.ReferenceCode == code);
    }

    public async Task UpdateAsync(UpdateForm form)
    {
        if (!Enum.IsDefined(form.Priority) || !Enum.IsDefined(form.Status)) throw new ArgumentException("Invalid priority or status.");
        if (string.IsNullOrWhiteSpace(form.AssignedRole) || form.AssignedRole.Trim().Length > 120 || form.AssignedPerson?.Length > 120) throw new ArgumentException("Enter a valid assigned role or person.");
        var request = await db.Requests.FindAsync(form.Id) ?? throw new KeyNotFoundException();
        if (form.Status != request.Status && (int)form.Status != (int)request.Status + 1) throw new ArgumentException("Move to the next workflow stage only.");
        byte[] version;
        try { version = Convert.FromBase64String(form.RowVersion); } catch (FormatException) { throw new ArgumentException("Invalid request version."); }
        db.Entry(request).Property(x => x.RowVersion).OriginalValue = version;
        var assignedRole = form.AssignedRole.Trim();
        var assignedPerson = form.AssignedPerson?.Trim() ?? "";
        if (request.Status == form.Status && request.Priority == form.Priority && request.AssignedRole == assignedRole && request.AssignedPerson == assignedPerson) return;
        var now = DateTimeOffset.UtcNow;
        var statusChanged = request.Status != form.Status;
        if (statusChanged) request.History.Add(new() { Status = form.Status, ChangedAt = now });
        request.Status = form.Status; request.Priority = form.Priority; request.LastUpdatedAt = now;
        request.AssignedRole = assignedRole; request.AssignedPerson = assignedPerson;
        await db.SaveChangesAsync();
        if (statusChanged) await email.SendStatusUpdateAsync(request);   // only email when progress changes
    }
}
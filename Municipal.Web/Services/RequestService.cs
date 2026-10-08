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
    private readonly SmsNotificationService sms;

    public RequestService(ApplicationDbContext db)
        : this(db, new AssignmentService(new ConfigurationBuilder().Build()), SmsNotificationService.Disabled)
    {
    }

    public RequestService(ApplicationDbContext db, AssignmentService assignments, SmsNotificationService sms)
    {
        this.db = db;
        this.assignments = assignments;
        this.sms = sms;
    }

    public static string NewReference() => "MUN-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
    protected virtual string GenerateReference() => NewReference();
    public async Task<ProblemRequest> SubmitAsync(ReportForm form)
    {
        if (form.Category is null || !Enum.IsDefined(form.Category.Value) || string.IsNullOrWhiteSpace(form.Location) || form.Location.Trim().Length > 200 || form.Description?.Length > 1000 || string.IsNullOrWhiteSpace(form.ResidentPhoneNumber) || form.ResidentPhoneNumber.Trim().Length > 30) throw new ArgumentException("Invalid report.");
        var assignment = assignments.For(form.Category.Value);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var now = DateTimeOffset.UtcNow;
            var request = new ProblemRequest { ReferenceCode = GenerateReference(), Category = form.Category.Value, Location = form.Location.Trim(), Description = form.Description?.Trim(), ResidentPhoneNumber = form.ResidentPhoneNumber.Trim(), AssignedRole = assignment.Role, AssignedPerson = assignment.Person, ReportedAt = now, LastUpdatedAt = now, History = [new() { Status = RequestStatus.Submitted, ChangedAt = now }] };
            db.Requests.Add(request);
            try
            {
                await db.SaveChangesAsync();
                await sms.SendSubmittedAsync(request);
                return request;
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 }) { db.ChangeTracker.Clear(); }
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
        if (request.Status != form.Status) request.History.Add(new() { Status = form.Status, ChangedAt = now });
        request.Status = form.Status; request.Priority = form.Priority; request.LastUpdatedAt = now;
        request.AssignedRole = assignedRole; request.AssignedPerson = assignedPerson;
        await db.SaveChangesAsync();
        await sms.SendStatusChangedAsync(request);
    }
}

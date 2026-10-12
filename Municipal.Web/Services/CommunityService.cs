using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Municipal.Web.Data;
using Municipal.Web.Models;

namespace Municipal.Web.Services;

public class CommunityService(ApplicationDbContext db)
{
    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double rad = Math.PI / 180;
        var a = Math.Pow(Math.Sin((lat2 - lat1) * rad / 2), 2)
            + Math.Cos(lat1 * rad) * Math.Cos(lat2 * rad) * Math.Pow(Math.Sin((lon2 - lon1) * rad / 2), 2);
        return 6371 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1)));
    }

    public async Task<List<NearbyReport>> NearbyAsync(string? municipality, Category? category, double? latitude = null, double? longitude = null)
    {
        var query = db.Requests.AsNoTracking().Where(r => r.Status < RequestStatus.Resolved);
        if (!string.IsNullOrWhiteSpace(municipality)) query = query.Where(r => r.MunicipalityCode == municipality);
        if (category.HasValue) query = query.Where(r => r.Category == category.Value);
        if (latitude.HasValue && longitude.HasValue)
        {
            var lat = latitude.Value; var lon = longitude.Value;
            query = query.Where(r => r.Latitude >= lat - .06 && r.Latitude <= lat + .06
                && r.Longitude >= lon - .1 && r.Longitude <= lon + .1);
        }
        var items = await query.OrderByDescending(r => r.ReportedAt).Take(300)
            .Select(r => new { r.Id, r.Category, r.Status, r.MunicipalityName, r.ReportedAt, r.Latitude, r.Longitude,
                Affected = 1 + r.Supporters.Count }).ToListAsync();
        return items.Select(r => new NearbyReport(r.Id, r.Category, r.Status, r.MunicipalityName, r.ReportedAt, r.Affected,
            r.Latitude.HasValue ? Math.Round(r.Latitude.Value, 3) : null,
            r.Longitude.HasValue ? Math.Round(r.Longitude.Value, 3) : null,
            latitude.HasValue && longitude.HasValue && r.Latitude.HasValue && r.Longitude.HasValue
                ? Math.Round(DistanceKm(latitude.Value, longitude.Value, r.Latitude.Value, r.Longitude.Value), 1) : null))
            .Where(r => r.DistanceKm is null or <= 5).OrderBy(r => r.DistanceKm ?? double.MaxValue)
            .ThenByDescending(r => r.ReportedAt).Take(100).ToList();
    }

    public async Task<bool> SupportAsync(SupportForm form)
    {
        var email = form.Email.Trim();
        if (email.Length > 254 || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
            throw new ArgumentException("Enter a valid email address.");
        var request = await db.Requests.SingleOrDefaultAsync(r => r.Id == form.RequestId) ?? throw new KeyNotFoundException();
        if (request.Status >= RequestStatus.Resolved) throw new ArgumentException("This report has already been resolved.");
        if (string.Equals(email, request.ResidentEmail, StringComparison.OrdinalIgnoreCase)) return false;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email.ToUpperInvariant())));
        if (await db.RequestSupporters.AnyAsync(s => s.ProblemRequestId == request.Id && s.EmailKey == key)) return false;
        db.RequestSupporters.Add(new RequestSupporter { ProblemRequestId = request.Id, Email = email, EmailKey = key,
            JoinedAt = DateTimeOffset.UtcNow });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }
            or SqlException { Number: 2601 or 2627 }) { db.ChangeTracker.Clear(); return false; }
        return true;
    }

    public async Task ReviewAsync(ReviewForm form)
    {
        var request = await db.Requests.SingleOrDefaultAsync(r => r.ReferenceCode == form.ReferenceCode.Trim().ToUpper())
            ?? throw new KeyNotFoundException();
        if (!string.Equals(form.Email.Trim(), request.ResidentEmail, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use the email address from the original report.");
        if (request.Status != RequestStatus.Resolved) throw new ArgumentException("This report is not awaiting confirmation.");
        if (form.Note?.Length > 1000) throw new ArgumentException("Keep the note within 1,000 characters.");
        request.ResidentReview = form.Confirmed ? ResidentReview.Confirmed : ResidentReview.NotFixed;
        request.Status = form.Confirmed ? RequestStatus.Closed : RequestStatus.InProgress;
        var now = DateTimeOffset.UtcNow;
        request.LastUpdatedAt = now;
        if (!form.Confirmed) request.ResolvedAt = null;
        var note = (form.Confirmed ? "Resident confirmed the repair." : "Resident reported the problem is not fixed.")
            + (string.IsNullOrWhiteSpace(form.Note) ? "" : " " + form.Note.Trim());
        request.History.Add(new StatusHistory { ChangedAt = now, Status = request.Status,
            Note = note.Length > 1000 ? note[..1000] : note });
        if (db.Database.IsNpgsql()) request.RowVersion = RandomNumberGenerator.GetBytes(16);
        await db.SaveChangesAsync();
    }

    public async Task<DashboardMetrics> DashboardAsync(string? municipality)
    {
        var query = db.Requests.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(municipality)) query = query.Where(r => r.MunicipalityCode == municipality);
        var rows = await query.Select(r => new { r.MunicipalityName, r.Status, r.EstimatedCompletionAt, r.ReportedAt,
            Resolved = r.ResolvedAt ?? r.History.Where(h => h.Status == RequestStatus.Resolved)
                .Select(h => (DateTimeOffset?)h.ChangedAt).Max(), r.ResidentReview }).ToListAsync();
        var now = DateTimeOffset.UtcNow;
        var durations = rows.Where(r => r.Status >= RequestStatus.Resolved && r.Resolved.HasValue)
            .Select(r => Math.Max(0, (r.Resolved!.Value - r.ReportedAt).TotalDays)).ToList();
        var groups = rows.GroupBy(r => r.MunicipalityName).Select(group =>
        {
            var times = group.Where(r => r.Status >= RequestStatus.Resolved && r.Resolved.HasValue)
                .Select(r => Math.Max(0, (r.Resolved!.Value - r.ReportedAt).TotalDays)).ToList();
            return new MunicipalityMetric(group.Key, group.Count(r => r.Status < RequestStatus.Resolved),
                group.Count(r => r.Status < RequestStatus.Resolved && r.EstimatedCompletionAt < now),
                group.Count(r => r.Status >= RequestStatus.Resolved), times.Count == 0 ? null : times.Average());
        }).OrderByDescending(g => g.Open).ToList();
        return new DashboardMetrics(rows.Count(r => r.Status < RequestStatus.Resolved),
            rows.Count(r => r.Status < RequestStatus.Resolved && r.EstimatedCompletionAt < now),
            rows.Count(r => r.Status >= RequestStatus.Resolved), rows.Count(r => r.Status == RequestStatus.Resolved),
            durations.Count == 0 ? null : durations.Average(), groups);
    }
}

using System.Net;
using System.Net.Mail;
using Municipal.Web.Models;

namespace Municipal.Web.Services;

public class EmailSettings
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "Municipal Connect";
    public string TrackUrl { get; set; } = "";   // e.g. https://localhost:5001/Requests/Track
}

public class EmailService
{
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailService> _log;

    public EmailService(IConfiguration config, ILogger<EmailService> log)
    {
        _settings = config.GetSection("Email").Get<EmailSettings>() ?? new EmailSettings();
        _log = log;
    }

    public Task<EmailDeliveryStatus> SendRequestReceivedAsync(ProblemRequest r) =>
        SendAsync(r.ResidentEmail,
            $"Request received – {r.ReferenceCode}",
            Wrap("Thank you for reporting",
                $"<p>We have received your <strong>{WebUtility.HtmlEncode(Labels.CategoryName(r.Category))}</strong> report " +
                $"at <strong>{WebUtility.HtmlEncode(r.Location)}</strong>.</p>" +
                $"<p>Your reference code is:</p>{Code(r.ReferenceCode)}" +
                Details(r) + "<p>Keep this code to track your request.</p>",
                r.ReferenceCode));

    public Task<EmailDeliveryStatus> SendStatusUpdateAsync(ProblemRequest r) =>
        SendAsync(r.ResidentEmail,
            $"Update on {r.ReferenceCode}: {Labels.Status(r.Status)}",
            Wrap("Your request has been updated",
                $"<p>Reference: {Code(r.ReferenceCode)}</p>" +
                $"<p>New status: <strong>{Labels.Status(r.Status)}</strong></p>" +
                Details(r),
                r.ReferenceCode));

    private static string Details(ProblemRequest r)
    {
        var eta = r.EstimatedCompletionAt?.ToOffset(TimeSpan.FromHours(2)).ToString("dd MMM yyyy, HH:mm") + " SAST";
        return $"<p>Municipality: <strong>{WebUtility.HtmlEncode(r.MunicipalityName)}</strong></p>" +
            $"<p>Assigned person: <strong>{WebUtility.HtmlEncode(r.AssignedPerson)}</strong><br>Team: {WebUtility.HtmlEncode(r.AssignedRole)}</p>" +
            $"<p>Estimated completion: <strong>{(r.EstimatedCompletionAt.HasValue ? eta : "Awaiting estimate")}</strong></p>" +
            "<p style=\"font-size:12px;color:#54616f\">Completion times are provisional prototype estimates, not confirmed municipal commitments.</p>";
    }

    private async Task<EmailDeliveryStatus> SendAsync(string to, string subject, string html)
    {
        if (!_settings.Enabled || string.IsNullOrWhiteSpace(to) || to.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase))
            return EmailDeliveryStatus.Disabled;
        try
        {
            using var client = new SmtpClient(_settings.Host, _settings.Port)
            {
                Credentials = new NetworkCredential(_settings.Username, _settings.Password),
                EnableSsl = _settings.EnableSsl
            };
            using var msg = new MailMessage
            {
                From = new MailAddress(_settings.FromAddress, _settings.FromName),
                Subject = subject,
                Body = html,
                IsBodyHtml = true
            };
            msg.To.Add(to);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await client.SendMailAsync(msg, timeout.Token);
            return EmailDeliveryStatus.Sent;
        }
        catch (Exception ex)
        {
            // Never break the resident's request because an email failed
            _log.LogWarning(ex, "Report notification could not be sent.");
            return EmailDeliveryStatus.Failed;
        }
    }

    private static string Code(string code) =>
        $"<div style=\"display:inline-block;font-family:Consolas,monospace;font-size:20px;font-weight:700;" +
        $"letter-spacing:1px;background:#f1f8f4;border:2px dashed #007749;padding:8px 16px;border-radius:6px\">" +
        $"{WebUtility.HtmlEncode(code)}</div>";

    private string Wrap(string title, string body, string reference)
    {
        var link = string.IsNullOrWhiteSpace(_settings.TrackUrl) ? "" :
            $"<p><a href=\"{WebUtility.HtmlEncode(_settings.TrackUrl + (_settings.TrackUrl.Contains('?') ? "&" : "?") + "referenceCode=" + Uri.EscapeDataString(reference))}\" style=\"background:#007749;color:#fff;padding:10px 20px;" +
            "border-radius:6px;text-decoration:none;font-weight:700\">Track your request</a></p>";
        return $@"<div style=""font-family:Segoe UI,Arial,sans-serif;max-width:560px;margin:auto;border:1px solid #dbe2e8;border-radius:10px;overflow:hidden"">
<div style=""background:#003d27;color:#fff;padding:14px 20px;font-size:12px;letter-spacing:1px;text-transform:uppercase"">Republic of South Africa · Municipal Connect</div>
<div style=""height:5px;background:linear-gradient(90deg,#000 16%,#ffb81c 16% 33%,#007749 33% 50%,#fff 50% 66%,#e03c31 66% 83%,#001489 83%)""></div>
<div style=""padding:24px""><h2 style=""color:#007749;margin-top:0"">{title}</h2>{body}{link}
<p style=""font-size:12px;color:#54616f"">Student prototype – not for emergencies. SAPS 10111 · Ambulance 10177 · Cell 112</p></div></div>";
    }
}

using System.Net.Http.Headers;
using System.Net.Http.Json;
using Municipal.Web.Models;

namespace Municipal.Web.Services;

public class SmsNotificationService
{
    public static SmsNotificationService Disabled { get; } = new(null, new ConfigurationBuilder().Build(), NullLogger<SmsNotificationService>.Instance);

    private readonly HttpClient? http;
    private readonly IConfiguration config;
    private readonly ILogger<SmsNotificationService> logger;

    public SmsNotificationService(HttpClient? http, IConfiguration config, ILogger<SmsNotificationService> logger)
    {
        this.http = http;
        this.config = config;
        this.logger = logger;
    }

    public Task SendSubmittedAsync(ProblemRequest request)
    {
        var assignedTo = string.IsNullOrWhiteSpace(request.AssignedPerson) ? request.AssignedRole : $"{request.AssignedPerson} ({request.AssignedRole})";
        var message = $"Municipal request {request.ReferenceCode} logged. Assigned to: {assignedTo}. Status: {Labels.Status(request.Status)}.";
        return SendAsync(request.ResidentPhoneNumber, message);
    }

    public Task SendStatusChangedAsync(ProblemRequest request)
    {
        var assignedTo = string.IsNullOrWhiteSpace(request.AssignedPerson) ? request.AssignedRole : $"{request.AssignedPerson} ({request.AssignedRole})";
        var message = $"Municipal request {request.ReferenceCode} is now {Labels.Status(request.Status)}. Assigned to: {assignedTo}.";
        return SendAsync(request.ResidentPhoneNumber, message);
    }

    private async Task SendAsync(string to, string message)
    {
        if (string.IsNullOrWhiteSpace(to)) return;

        var providerUrl = config["Sms:ProviderUrl"];
        if (string.IsNullOrWhiteSpace(providerUrl) || http is null)
        {
            logger.LogInformation("SMS disabled. Would send to {Phone}: {Message}", to, message);
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, providerUrl);
        var apiKey = config["Sms:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(new { to, from = config["Sms:From"], message });

        try
        {
            using var response = await http.SendAsync(request);
            if (!response.IsSuccessStatusCode) logger.LogWarning("SMS provider returned {StatusCode} for {Phone}.", response.StatusCode, to);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "SMS provider could not be reached for {Phone}.", to);
        }
    }

    private sealed class NullLogger<T> : ILogger<T>
    {
        public static NullLogger<T> Instance { get; } = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
        private sealed class NullScope : IDisposable { public static NullScope Instance { get; } = new(); public void Dispose() { } }
    }
}

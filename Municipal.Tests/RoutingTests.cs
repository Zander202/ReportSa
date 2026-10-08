using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Municipal.Web.Models;
using Municipal.Web.Services;

namespace Municipal.Tests;

public class RoutingTests
{
    [Fact]
    public void MunicipalityDirectoryCoversAllProvincesAndRejectsUnknownCodes()
    {
        var directory = new MunicipalityDirectory();
        Assert.Equal(257, directory.All.Count);
        Assert.Equal(9, directory.All.Select(x => x.Province).Distinct().Count());
        Assert.Equal("Western Cape", directory.Find("CPT")!.Province);
        Assert.Null(directory.Find("NOTREAL"));
    }

    [Fact]
    public void AssignmentsUseDemoStaffUnlessAnActualAssigneeIsConfigured()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Assignments:WaterLeak:Person"] = "Configured technician"
        }).Build();
        var assignments = new AssignmentService(configuration);
        Assert.Contains("(demo staff)", assignments.For(Category.Pothole).Person);
        Assert.Equal("Configured technician", assignments.For(Category.WaterLeak).Person);
    }

    [Fact]
    public async Task ReceiptAndUpdateEmailsContainReferenceAssigneeMunicipalityAndEta()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Email:Enabled"] = "true", ["Email:Host"] = "127.0.0.1", ["Email:Port"] = port.ToString(),
            ["Email:EnableSsl"] = "false", ["Email:FromAddress"] = "sender@example.test",
            ["Email:TrackUrl"] = "https://example.test/Requests/Track"
        }).Build();
        var service = new EmailService(configuration, NullLogger<EmailService>.Instance);
        var request = new ProblemRequest
        {
            ReferenceCode = "MUN-0123456789ABCDEF01234567", ResidentEmail = "resident@example.test",
            MunicipalityName = "Cape Town Metropolitan Municipality", AssignedPerson = "Demo Technician",
            AssignedRole = "Roads <team>", Location = "<script>test</script>",
            EstimatedCompletionAt = new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero)
        };
        foreach (var update in new[] { false, true })
        {
            var capture = CaptureMessageAsync(listener);
            var sent = update ? await service.SendStatusUpdateAsync(request) : await service.SendRequestReceivedAsync(request);
            Assert.Equal(EmailDeliveryStatus.Sent, sent);
            var message = await capture.WaitAsync(TimeSpan.FromSeconds(15));
            var parts = message.Split("\r\n\r\n", 2);
            message = parts[0].Contains("Content-Transfer-Encoding: base64", StringComparison.OrdinalIgnoreCase)
                ? System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]))
                : System.Text.RegularExpressions.Regex.Replace(parts[1].Replace("=\r\n", ""), "=([0-9A-F]{2})",
                    match => ((char)Convert.ToByte(match.Groups[1].Value, 16)).ToString());
            Assert.Contains(request.ReferenceCode, message);
            Assert.Contains(request.AssignedPerson, message);
            Assert.Contains(request.MunicipalityName, message);
            Assert.Contains("10 Oct 2026, 10:00 SAST", message);
            Assert.Contains("referenceCode=" + request.ReferenceCode, message);
            Assert.Contains("Roads &lt;team&gt;", message);
            Assert.DoesNotContain("<script>", message);
        }
    }

    private static async Task<string> CaptureMessageAsync(TcpListener listener)
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream);
        await using var writer = new StreamWriter(stream) { AutoFlush = true, NewLine = "\r\n" };
        await writer.WriteLineAsync("220 localhost test SMTP");
        var body = new System.Text.StringBuilder();
        var inData = false;
        while (await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)) is { } line)
        {
            if (inData)
            {
                if (line == ".") { inData = false; await writer.WriteLineAsync("250 accepted"); }
                else body.AppendLine(line.StartsWith("..") ? line[1..] : line);
            }
            else if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
            {
                inData = true;
                await writer.WriteLineAsync("354 end with dot");
            }
            else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("221 goodbye");
                break;
            }
            else await writer.WriteLineAsync("250 OK");
        }
        return body.ToString();
    }
}

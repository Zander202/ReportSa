using System.ComponentModel.DataAnnotations;
namespace Municipal.Web.Models;
public enum Category { Pothole, [Display(Name="Power outage")] PowerOutage, [Display(Name="Broken streetlight")] BrokenStreetlight, [Display(Name="Water leak")] WaterLeak, [Display(Name="Blocked drain")] BlockedDrain, [Display(Name="Illegal dumping")] IllegalDumping, Other }
public enum Priority { Low, Normal, High, Urgent }
public enum EmailDeliveryStatus { Disabled, Sent, Failed }
public enum RequestStatus { Submitted, Assigned, [Display(Name="In Progress")] InProgress, Resolved, Closed }
public class ProblemRequest
{
    public int Id { get; set; }
    [MaxLength(32)] public string ReferenceCode { get; set; } = "";
    public Category Category { get; set; }
    [MaxLength(200)] public string Location { get; set; } = "";
    [MaxLength(1000)] public string? Description { get; set; }
    [MaxLength(30)] public string ResidentPhoneNumber { get; set; } = "";
    [MaxLength(120)] public string AssignedRole { get; set; } = "";
    [MaxLength(120)] public string AssignedPerson { get; set; } = "";
    public DateTimeOffset ReportedAt { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }
    public Priority Priority { get; set; } = Priority.Normal;
    public RequestStatus Status { get; set; } = RequestStatus.Submitted;
    [Timestamp] public byte[] RowVersion { get; set; } = [];
    public List<StatusHistory> History { get; set; } = [];
    [MaxLength(254)] public string ResidentEmail { get; set; } = "";
    [MaxLength(10)] public string MunicipalityCode { get; set; } = "";
    [MaxLength(200)] public string MunicipalityName { get; set; } = "";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateTimeOffset? EstimatedCompletionAt { get; set; }
    public EmailDeliveryStatus ConfirmationEmailStatus { get; set; }
}
public class StatusHistory
{
    public int Id { get; set; }
    public int ProblemRequestId { get; set; }
    public ProblemRequest Request { get; set; } = null!;
    public RequestStatus Status { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
}
public static class Labels
{
    public static string Status(RequestStatus value) => value == RequestStatus.InProgress ? "In Progress" : value.ToString();
    public static string CategoryName(Category value) => value switch { Category.PowerOutage => "Power outage", Category.BrokenStreetlight => "Broken streetlight", Category.WaterLeak => "Water leak", Category.BlockedDrain => "Blocked drain", Category.IllegalDumping => "Illegal dumping", _ => value.ToString() };
}

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc;
namespace Municipal.Web.Models;
public class ReportForm
{
    [Required, StringLength(10), Display(Name="Your municipality")] public string MunicipalityCode { get; set; } = "";
    [Range(-90, 90), ModelBinder(BinderType = typeof(CoordinateModelBinder))] public double? Latitude { get; set; }
    [Range(-180, 180), ModelBinder(BinderType = typeof(CoordinateModelBinder))] public double? Longitude { get; set; }
    [Required, EnumDataType(typeof(Category))] public Category? Category { get; set; }
    [Required, StringLength(200), Display(Name="Location")] public string Location { get; set; } = "";
    [StringLength(1000), Display(Name="Short description (optional)")] public string? Description { get; set; }

    [Required, EmailAddress, StringLength(254), Display(Name = "Email address")] public string ResidentEmail { get; set; } = "";
    public List<IFormFile> Photos { get; set; } = [];
}
public class TrackForm
{
    [Required, StringLength(32), Display(Name="Reference code")] public string ReferenceCode { get; set; } = "";
    public ProblemRequest? Request { get; set; }
}
public class LoginForm
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password), StringLength(256, MinimumLength = 12)] public string Password { get; set; } = "";
}
public class UpdateForm
{
    public bool CloseTicket { get; set; }
    [BindRequired] public int Id { get; set; }
    [BindRequired, EnumDataType(typeof(Priority))] public Priority Priority { get; set; }
    [BindRequired, EnumDataType(typeof(RequestStatus))] public RequestStatus Status { get; set; }
    [Required, StringLength(120)] public string AssignedRole { get; set; } = "";
    [StringLength(120)] public string? AssignedPerson { get; set; }
    [Required] public string RowVersion { get; set; } = "";
    public DateTime? EstimatedCompletionLocal { get; set; }
    [StringLength(1000)] public string? ProgressNote { get; set; }
    [StringLength(1000)] public string? ResolutionNotes { get; set; }
    public List<IFormFile> ResolutionPhotos { get; set; } = [];
}
public class SupportForm
{
    [Range(1, int.MaxValue)] public int RequestId { get; set; }
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
}
public class ReviewForm
{
    [Required, StringLength(32)] public string ReferenceCode { get; set; } = "";
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [BindRequired] public bool Confirmed { get; set; }
    [StringLength(1000)] public string? Note { get; set; }
}
public sealed record NearbyReport(int Id, Category Category, RequestStatus Status, string Municipality,
    DateTimeOffset ReportedAt, int Affected, double? Latitude, double? Longitude, double? DistanceKm);
public class NearbyPage
{
    public string? MunicipalityCode { get; set; }
    public Category? Category { get; set; }
    public List<NearbyReport> Reports { get; set; } = [];
}
public sealed record MunicipalityMetric(string Name, int Open, int Overdue, int Resolved, double? AverageDays);
public sealed record DashboardMetrics(int Open, int Overdue, int Resolved, int AwaitingReview, double? AverageDays,
    IReadOnlyList<MunicipalityMetric> Municipalities);

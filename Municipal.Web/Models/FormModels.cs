using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
namespace Municipal.Web.Models;
public class ReportForm
{
    [Required, EnumDataType(typeof(Category))] public Category? Category { get; set; }
    [Required, StringLength(200), Display(Name="Location / address")] public string Location { get; set; } = "";
    [StringLength(1000), Display(Name="Short description (optional)")] public string? Description { get; set; }
    [Required, Phone, StringLength(30), Display(Name="SMS phone number")] public string ResidentPhoneNumber { get; set; } = "";
}
public class TrackForm
{
    [Required, StringLength(32), Display(Name="Reference code")] public string ReferenceCode { get; set; } = "";
    public ProblemRequest? Request { get; set; }
}
public class LoginForm
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
}
public class UpdateForm
{
    [BindRequired] public int Id { get; set; }
    [BindRequired, EnumDataType(typeof(Priority))] public Priority Priority { get; set; }
    [BindRequired, EnumDataType(typeof(RequestStatus))] public RequestStatus Status { get; set; }
    [Required, StringLength(120)] public string AssignedRole { get; set; } = "";
    [StringLength(120)] public string? AssignedPerson { get; set; }
    [Required] public string RowVersion { get; set; } = "";
}

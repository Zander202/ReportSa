using Municipal.Web.Models;

namespace Municipal.Web.Services;

public sealed record Assignment(string Role, string Person);

public class AssignmentService(IConfiguration config)
{
    public Assignment For(Category category)
    {
        var section = config.GetSection($"Assignments:{category}");
        var role = section["Role"];
        var person = section["Person"];

        if (string.IsNullOrWhiteSpace(role))
        {
            role = category switch
            {
                Category.Pothole => "Roads maintenance team",
                Category.PowerOutage => "Electrical services team",
                Category.BrokenStreetlight => "Street lighting technician",
                Category.WaterLeak => "Water services technician",
                Category.BlockedDrain => "Drainage maintenance team",
                Category.IllegalDumping => "Waste management team",
                _ => "Municipal support desk"
            };
        }

        return new Assignment(role.Trim(), person?.Trim() ?? "To be assigned");
    }
}

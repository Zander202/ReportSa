using Municipal.Web.Models;

namespace Municipal.Web.Services;

public sealed record Assignment(string Role, string Person);

public class AssignmentService(IConfiguration config)
{
    private static readonly string[] DemoStaff =
        ["Thabo Molefe", "Naledi Nkosi", "Sipho Dlamini", "Lerato Mokoena", "Ayesha Jacobs", "Pieter Botha", "Nomsa Khumalo", "Zanele Mthembu"];

    public static string PersonName(string person)
    {
        const string suffix = "(demo staff)";
        var name = person.Trim();
        return name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? name[..^suffix.Length].TrimEnd() : name;
    }

    public static DateTimeOffset Estimate(Category category, DateTimeOffset reportedAt) =>
        reportedAt.AddHours(category switch
        {
            Category.PowerOutage => 4,
            Category.WaterLeak => 24,
            Category.BlockedDrain => 48,
            Category.BrokenStreetlight => 72,
            Category.IllegalDumping => 72,
            Category.Pothole => 120,
            _ => 168
        });

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

        if (string.IsNullOrWhiteSpace(person) || person.Trim().Equals("To be assigned", StringComparison.OrdinalIgnoreCase))
            person = DemoStaff[System.Security.Cryptography.RandomNumberGenerator.GetInt32(DemoStaff.Length)];
        return new Assignment(role.Trim(), PersonName(person));
    }
}

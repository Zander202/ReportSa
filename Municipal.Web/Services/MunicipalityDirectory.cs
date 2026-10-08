using System.Text.Json;

namespace Municipal.Web.Services;

public sealed record Municipality(string Code, string Name, string Province, string Type)
{
    public string Label => $"{Name} {(Type == "A" ? "Metropolitan" : Type == "C" ? "District" : "Local")} Municipality";
}

public class MunicipalityDirectory
{
    public IReadOnlyList<Municipality> All { get; }

    public MunicipalityDirectory()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "municipalities.json");
        All = JsonSerializer.Deserialize<List<Municipality>>(File.ReadAllText(path))
            ?? throw new InvalidOperationException("Municipality directory is unavailable.");
    }

    public Municipality? Find(string? code) => All.FirstOrDefault(x => x.Code == code);
}

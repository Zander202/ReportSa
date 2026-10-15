using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;

namespace Municipal.Web.Services;

public sealed record LocatedPlace(Municipality? Municipality, string? Address, string? Area = null);
public sealed record AddressSuggestion(string Address, double Latitude, double Longitude);

public class MunicipalityLocationService(HttpClient client, MunicipalityDirectory directory,
    IMemoryCache cache, IConfiguration configuration)
{
    public async Task<IReadOnlyList<AddressSuggestion>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        query = query.Trim();
        if (query.Length < 3 || query.Length > 200) return Array.Empty<AddressSuggestion>();
        var key = "address-search:" + query.ToLowerInvariant();
        if (cache.TryGetValue<IReadOnlyList<AddressSuggestion>>(key, out var cached)) return cached!;
        var endpoint = configuration["MunicipalityLocation:SearchUrl"] ?? "https://photon.komoot.io/api";
        var url = QueryHelpers.AddQueryString(endpoint, new Dictionary<string, string?>
        {
            ["q"] = query, ["countrycode"] = "ZA", ["lang"] = "en", ["limit"] = "6"
        });
        using var response = await client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!json.RootElement.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
            throw new JsonException("Address search returned an invalid response.");
        var results = new List<AddressSuggestion>();
        foreach (var feature in features.EnumerateArray())
        {
            if (!feature.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object
                || !feature.TryGetProperty("geometry", out var geometry) || geometry.ValueKind != JsonValueKind.Object
                || !geometry.TryGetProperty("coordinates", out var coordinates) || coordinates.ValueKind != JsonValueKind.Array
                || coordinates.GetArrayLength() < 2 || coordinates[0].ValueKind != JsonValueKind.Number
                || coordinates[1].ValueKind != JsonValueKind.Number
                || !coordinates[0].TryGetDouble(out var lon) || !coordinates[1].TryGetDouble(out var lat)
                || !double.IsFinite(lat) || !double.IsFinite(lon) || lat < -90 || lat > 90 || lon < -180 || lon > 180) continue;
            string? Text(string name) => properties.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;
            if (!string.Equals(Text("countrycode"), "ZA", StringComparison.OrdinalIgnoreCase)) continue;
            var street = string.Join(" ", new[] { Text("housenumber"), Text("street") }.Where(s => !string.IsNullOrWhiteSpace(s)));
            var label = string.Join(", ", new[] { Text("name"), street, Text("district"), Text("city"), Text("county"), Text("state"), Text("postcode") }
                .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(label)) continue;
            results.Add(new AddressSuggestion(label.Length > 200 ? label[..200] : label, lat, lon));
        }
        IReadOnlyList<AddressSuggestion> suggestions = results.Distinct().Take(6).ToArray();
        cache.Set(key, suggestions, TimeSpan.FromMinutes(5));
        return suggestions;
    }

    public async Task<LocatedPlace> ResolveAsync(double latitude, double longitude, CancellationToken cancellationToken,
        bool includeStreet = true)
    {
        var boundaryTask = TryLookupAsync(() => FindAsync(latitude, longitude, cancellationToken), cancellationToken);
        var addressTask = TryLookupAsync(() => FindAddressAsync(latitude, longitude, cancellationToken, includeStreet), cancellationToken);
        await Task.WhenAll(boundaryTask, addressTask);
        var address = await addressTask;
        return new LocatedPlace(await boundaryTask ?? address?.Municipality, address?.Address, address?.Area);
    }

    private static async Task<T?> TryLookupAsync<T>(Func<Task<T?>> lookup, CancellationToken cancellationToken)
        where T : class
    {
        try { return await lookup(); }
        catch (Exception error) when (error is HttpRequestException or JsonException
            || error is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task<LocatedPlace?> FindAddressAsync(double latitude, double longitude, CancellationToken cancellationToken,
        bool includeStreet)
    {
        var key = FormattableString.Invariant($"address-road-v2:{includeStreet}:{latitude:R}:{longitude:R}");
        if (cache.TryGetValue<LocatedPlace>(key, out var cached)) return cached;
        var roadTask = includeStreet
            ? TryLookupAsync(() => FindRoadAsync(latitude, longitude, cancellationToken), cancellationToken)
            : Task.FromResult<string?>(null);
        var endpoint = configuration["MunicipalityLocation:AddressUrl"] ?? "https://photon.komoot.io/reverse";
        var url = QueryHelpers.AddQueryString(endpoint, new Dictionary<string, string?>
        {
            ["lat"] = latitude.ToString("R", CultureInfo.InvariantCulture),
            ["lon"] = longitude.ToString("R", CultureInfo.InvariantCulture), ["lang"] = "en",
            ["radius"] = includeStreet ? "0.15" : "5", ["limit"] = includeStreet ? "10" : "1"
        });
        using var response = await client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!json.RootElement.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
            throw new JsonException("Address lookup returned an invalid response.");
        if (features.GetArrayLength() == 0) return null;
        var places = features.EnumerateArray()
            .Where(feature => feature.TryGetProperty("properties", out var value) && value.ValueKind == JsonValueKind.Object)
            .Select(feature => feature.GetProperty("properties"))
            .Where(value => value.TryGetProperty("countrycode", out var country)
                && country.ValueKind == JsonValueKind.String && country.GetString()!.Equals("ZA", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (places.Count == 0) return null;
        // Nearby POIs can omit streets; prefer the closest returned place with a usable street.
        var properties = includeStreet ? places.FirstOrDefault(value =>
            value.TryGetProperty("street", out var streetValue) && streetValue.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(streetValue.GetString())
            || value.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "street"
                && value.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(name.GetString())) : places[0];
        if (properties.ValueKind == JsonValueKind.Undefined) properties = places[0];
        string? Text(string name) => properties.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
        if (!string.Equals(Text("countrycode"), "ZA", StringComparison.OrdinalIgnoreCase)) return null;
        // A geocoder's nearby building may refer to an entirely different road.
        var firstLine = await roadTask;
        var parts = new[] { firstLine, Text("district"), Text("city"), Text("state"), Text("postcode") }
            .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase);
        var label = string.IsNullOrWhiteSpace(firstLine) ? "" : string.Join(", ", parts);
        if (label.Length > 200) label = label[..200];
        // Only exact administrative-name matches are used as a fallback; do not guess nearby municipalities.
        var county = NormalizeMunicipality(Text("county"));
        var candidates = directory.All.Where(item => item.Type != "C"
            && string.Equals(item.Province, Text("state"), StringComparison.OrdinalIgnoreCase)
            && NormalizeMunicipality(item.Name) == county).ToList();
        var area = string.Join(", ", new[] { Text("city"), Text("county"), Text("state") }
            .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase));
        var result = new LocatedPlace(candidates.Count == 1 ? candidates[0] : null,
            !includeStreet || string.IsNullOrWhiteSpace(label) ? null : label,
            string.IsNullOrWhiteSpace(area) ? null : area);
        cache.Set(key, result, TimeSpan.FromMinutes(result.Address is null ? 1 : 30));
        return result;
    }

    private async Task<string?> FindRoadAsync(double latitude, double longitude, CancellationToken cancellationToken)
    {
        var endpoint = configuration["MunicipalityLocation:RoadUrl"] ?? "https://overpass-api.de/api/interpreter";
        var query = FormattableString.Invariant($"[out:json][timeout:3];way(around:30,{latitude:R},{longitude:R})[highway][name];out tags geom;");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        using var request = new HttpRequestMessage(HttpMethod.Get, QueryHelpers.AddQueryString(endpoint, "data", query));
        request.Headers.UserAgent.ParseAdd("ReportSA/1.0");
        using var response = await client.SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        if (!json.RootElement.TryGetProperty("elements", out var elements) || elements.ValueKind != JsonValueKind.Array)
            throw new JsonException("Road lookup returned an invalid response.");
        var roads = new List<(string Name, double Distance)>();
        foreach (var element in elements.EnumerateArray())
        {
            if (!element.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Object
                || !tags.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(name.GetString())
                || !element.TryGetProperty("geometry", out var geometry) || geometry.ValueKind != JsonValueKind.Array) continue;
            (double X, double Y)? previous = null;
            var distance = double.PositiveInfinity;
            foreach (var point in geometry.EnumerateArray())
            {
                if (!point.TryGetProperty("lat", out var latValue) || latValue.ValueKind != JsonValueKind.Number
                    || !point.TryGetProperty("lon", out var lonValue) || lonValue.ValueKind != JsonValueKind.Number
                    || !latValue.TryGetDouble(out var lat) || !lonValue.TryGetDouble(out var lon)
                    || !double.IsFinite(lat) || !double.IsFinite(lon) || lat < -90 || lat > 90 || lon < -180 || lon > 180)
                { previous = null; continue; }
                // Local metre projection; measure to each road segment, not its centre point.
                var current = (X: (lon - longitude) * 111195 * Math.Cos(latitude * Math.PI / 180),
                    Y: (lat - latitude) * 111195);
                if (previous is { } start)
                {
                    var dx = current.X - start.X;
                    var dy = current.Y - start.Y;
                    var lengthSquared = dx * dx + dy * dy;
                    var t = lengthSquared == 0 ? 0 : Math.Clamp(-(start.X * dx + start.Y * dy) / lengthSquared, 0, 1);
                    distance = Math.Min(distance, Math.Sqrt(Math.Pow(start.X + t * dx, 2) + Math.Pow(start.Y + t * dy, 2)));
                }
                previous = current;
            }
            if (distance <= 25) roads.Add((name.GetString()!, distance));
        }
        var nearest = roads.GroupBy(road => road.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.MinBy(road => road.Distance)).OrderBy(road => road.Distance).ToList();
        if (nearest.Count == 0 || nearest.Count > 1 && nearest[1].Distance - nearest[0].Distance < 3) return null;
        return nearest[0].Name;
    }

    private static string NormalizeMunicipality(string? name)
    {
        var normalized = (name ?? "").ToLowerInvariant();
        foreach (var word in new[] { "municipality", "metropolitan", "local", "city of" })
            normalized = normalized.Replace(word, "");
        return string.Join(" ", normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public async Task<Municipality?> FindAsync(double latitude, double longitude, CancellationToken cancellationToken)
    {
        var key = $"municipality:{latitude.ToString("R", CultureInfo.InvariantCulture)}:{longitude.ToString("R", CultureInfo.InvariantCulture)}";
        if (cache.TryGetValue<Municipality>(key, out var cached)) return cached;

        // Select the containing local/metro boundary rather than the nearest city.
        var endpoint = configuration["MunicipalityLocation:QueryUrl"]
            ?? "https://pta-gis-2-web1.csir.co.za/server2/rest/services/Hosted/GH2_Export_Atlas_web_v1_1_WFL1/FeatureServer/0/query";
        var url = QueryHelpers.AddQueryString(endpoint, new Dictionary<string, string?>
        {
            ["f"] = "json", ["where"] = "1=1",
            ["geometry"] = FormattableString.Invariant($"{longitude:R},{latitude:R}"),
            ["geometryType"] = "esriGeometryPoint", ["inSR"] = "4326",
            ["spatialRel"] = "esriSpatialRelIntersects", ["outFields"] = "cat_b",
            ["returnGeometry"] = "false"
        });
        using var response = await client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (json.RootElement.TryGetProperty("error", out _))
            throw new HttpRequestException("Municipality boundary lookup is unavailable.");
        if (!json.RootElement.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
            throw new JsonException("Municipality lookup returned an invalid response.");
        var matches = features.EnumerateArray()
            .Select(feature => feature.TryGetProperty("attributes", out var attributes)
                && attributes.TryGetProperty("cat_b", out var code) && code.ValueKind == JsonValueKind.String
                ? directory.Find(code.GetString()) : null)
            .Where(municipality => municipality is not null && municipality.Type != "C")
            .DistinctBy(municipality => municipality!.Code).ToList();
        if (matches.Count != 1) return null;
        cache.Set(key, matches[0]!, TimeSpan.FromHours(1));
        return matches[0];
    }
}

using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Municipal.Web.Services;

namespace Municipal.Tests;

public class MunicipalityLocationTests
{
    private const string AddressResponse = """
        {"features":[{"properties":{"street":"Darling Street","district":"City Centre","city":"Cape Town",
        "county":"City of Cape Town","state":"Western Cape","postcode":"8001","countrycode":"ZA"}}]}
        """;

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task AddressAndMunicipalitySurviveIndependentLookupFailures(bool boundaryAvailable, bool addressAvailable)
    {
        using var handler = new LookupHandler(boundaryAvailable ? "{\"features\":[{\"attributes\":{\"cat_b\":\"CPT\"}}]}" : null,
            addressAvailable ? AddressResponse : null);
        using var client = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new MunicipalityLocationService(client, new MunicipalityDirectory(), cache,
            new ConfigurationBuilder().Build());

        var place = await service.ResolveAsync(-33.9249, 18.4241, CancellationToken.None);

        Assert.Equal("CPT", place.Municipality?.Code);
        if (addressAvailable) Assert.Equal("Darling Street, City Centre, Cape Town, Western Cape, 8001", place.Address);
        else Assert.Null(place.Address);
    }

    [Theory]
    [InlineData("City of Cape Town", "Gauteng", "ZA")]
    [InlineData("Cape Winelands District Municipality", "Western Cape", "ZA")]
    [InlineData("City of Cape Town", "Western Cape", "US")]
    public async Task FallbackDoesNotGuessMunicipalityFromDistrictOrWrongProvinceOrCountry(string county, string province, string country)
    {
        var address = System.Text.Json.JsonSerializer.Serialize(new
        {
            features = new[] { new { properties = new { street = "Test Road", city = "Cape Town", county, state = province, countrycode = country } } }
        });
        using var handler = new LookupHandler(null, address);
        using var client = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new MunicipalityLocationService(client, new MunicipalityDirectory(), cache,
            new ConfigurationBuilder().Build());

        var place = await service.ResolveAsync(-33.9249, 18.4241, CancellationToken.None);

        Assert.Null(place.Municipality);
        if (country != "ZA") Assert.Null(place.Address);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("Darling Street", "Darling Street, Cape Town, Western Cape")]
    public async Task NearbyPoiAndHouseNumberAreNotPresentedAsExactAddress(string? street, string? expected)
    {
        var response = System.Text.Json.JsonSerializer.Serialize(new
        {
            features = new[] { new { properties = new { name = "Nearby shop", type = "house", housenumber = "99",
                street, city = "Cape Town", county = "City of Cape Town", state = "Western Cape", countrycode = "ZA" } } }
        });
        using var handler = new LookupHandler(null, response);
        using var client = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new MunicipalityLocationService(client, new MunicipalityDirectory(), cache,
            new ConfigurationBuilder().Build());

        var place = await service.ResolveAsync(-33.9249, 18.4241, CancellationToken.None);

        Assert.Equal(expected, place.Address);
        Assert.Equal("CPT", place.Municipality?.Code);
    }

    [Fact]
    public async Task ApproximateLocationReturnsAreaAndMunicipalityWithoutAStreetAndKeepsCachesSeparate()
    {
        using var handler = new LookupHandler(null, AddressResponse);
        using var client = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new MunicipalityLocationService(client, new MunicipalityDirectory(), cache,
            new ConfigurationBuilder().Build());

        var approximate = await service.ResolveAsync(-33.9249, 18.4241, CancellationToken.None, includeStreet: false);
        Assert.Equal("CPT", approximate.Municipality?.Code);
        Assert.Null(approximate.Address);
        Assert.Equal("Cape Town, City of Cape Town, Western Cape", approximate.Area);

        var precise = await service.ResolveAsync(-33.9249, 18.4241, CancellationToken.None);
        Assert.Contains("Darling Street", precise.Address);
    }

    private sealed class LookupHandler(string? boundary, string? address, string? roads = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.RequestUri!.AbsolutePath.EndsWith("/reverse") ? address : boundary;
            if (request.RequestUri.AbsolutePath.EndsWith("/interpreter"))
            {
                body = roads;
                if (body is null && address is not null)
                {
                    using var json = System.Text.Json.JsonDocument.Parse(address);
                    var street = json.RootElement.GetProperty("features").EnumerateArray()
                        .Select(f => f.GetProperty("properties"))
                        .Where(p => p.TryGetProperty("street", out var s) && s.ValueKind == System.Text.Json.JsonValueKind.String)
                        .Select(p => p.GetProperty("street").GetString()).FirstOrDefault();
                    body = street is null ? "{\"elements\":[]}" : System.Text.Json.JsonSerializer.Serialize(new
                    {
                        elements = new[] { new { tags = new { name = street }, geometry = new[] {
                            new { lat = -33.9259, lon = 18.4241 }, new { lat = -33.9239, lon = 18.4241 } } } }
                    });
                }
            }
            return Task.FromResult(new HttpResponseMessage(body is null ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            {
                Content = new StringContent(body ?? "{}", Encoding.UTF8, "application/json")
            });
        }
    }

    [Fact]
    public async Task SearchReturnsSelectedAddressCoordinatesAndRejectsInvalidOrForeignPlaces()
    {
        const string response = """
            {"features":[
              {"properties":{"housenumber":"12","street":"Darling Street","city":"Cape Town","countrycode":"ZA"},"geometry":{"coordinates":[18.4241,-33.9249]}},
              {"properties":{"name":"Foreign place","countrycode":"US"},"geometry":{"coordinates":[-70,40]}},
              {"properties":{"name":"Invalid place","countrycode":"ZA"},"geometry":{"coordinates":[18,120]}},
              {"properties":{"name":"Missing coordinates","countrycode":"ZA"}}
            ]}
            """;
        using var handler = new LookupHandler(response, null);
        using var client = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new MunicipalityLocationService(client, new MunicipalityDirectory(), cache,
            new ConfigurationBuilder().Build());
        var results = await service.SearchAsync("12 Darling Street", CancellationToken.None);
        var place = Assert.Single(results);
        Assert.Equal("12 Darling Street, Cape Town", place.Address);
        Assert.Equal(-33.9249, place.Latitude);
        Assert.Equal(18.4241, place.Longitude);
        Assert.Empty(await service.SearchAsync("ab", CancellationToken.None));
    }

    [Fact]
    public async Task PinLookupFindsStreetEvenWhenClosestPoiHasNoAddress()
    {
        const string response = """
            {"features":[{"properties":{"name":"Shop","countrycode":"ZA"}},
            {"properties":{"street":"Darling Street","city":"Cape Town","county":"City of Cape Town","state":"Western Cape","countrycode":"ZA"}}]}
            """;
        using var handler = new LookupHandler(null, response);
        using var client = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new MunicipalityLocationService(client, new MunicipalityDirectory(), cache,
            new ConfigurationBuilder().Build());
        var result = await service.ResolveAsync(-33.9249, 18.4241, CancellationToken.None);
        Assert.Equal("Darling Street, Cape Town, Western Cape", result.Address);
        Assert.Equal("CPT", result.Municipality?.Code);
    }

    [Theory]
    [InlineData(18.4241, 18.4243, "Begonia Drive")]
    [InlineData(18.4241, 18.42411, null)]
    [InlineData(18.4251, 18.4261, null)]
    public async Task RoadGeometryOverridesWrongGeocoderStreetAndRejectsAmbiguousOrDistantRoads(
        double begoniaLongitude, double jacarandaLongitude, string? expected)
    {
        var roads = System.Text.Json.JsonSerializer.Serialize(new { elements = new[] {
            new { tags = new { name = "Jacaranda Crescent" }, geometry = new[] {
                new { lat = -33.9259, lon = jacarandaLongitude }, new { lat = -33.9239, lon = jacarandaLongitude } } },
            new { tags = new { name = "Begonia Drive" }, geometry = new[] {
                new { lat = -33.9259, lon = begoniaLongitude }, new { lat = -33.9239, lon = begoniaLongitude } } }
        } });
        using var handler = new LookupHandler(null, AddressResponse.Replace("Darling Street", "Jacaranda Crescent"), roads);
        using var client = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new MunicipalityLocationService(client, new MunicipalityDirectory(), cache, new ConfigurationBuilder().Build());
        var result = await service.ResolveAsync(-33.9249, 18.4241, CancellationToken.None);
        if (expected is null) Assert.Null(result.Address);
        else { Assert.StartsWith(expected, result.Address); Assert.DoesNotContain("Jacaranda", result.Address!); }
        Assert.Equal("CPT", result.Municipality?.Code);
    }

    [Fact]
    public async Task UnavailableRoadDataNeverFallsBackToUnverifiedStreet()
    {
        using var handler = new LookupHandler(null, AddressResponse, "{}");
        using var client = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new MunicipalityLocationService(client, new MunicipalityDirectory(), cache, new ConfigurationBuilder().Build());
        var result = await service.ResolveAsync(-33.9249, 18.4241, CancellationToken.None);
        Assert.Null(result.Address);
        Assert.Equal("CPT", result.Municipality?.Code);
        Assert.Contains("Cape Town", result.Area);
    }
}

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

    [Fact]
    public async Task PinLookupFindsStreetEvenWhenClosestPoiHasNoAddress()
    {
        const string response = """
            {"features":[
                {"properties":{"name":"Park","type":"house","city":"Cape Town","county":"City of Cape Town","state":"Western Cape","countrycode":"ZA"}},
                {"properties":{"street":"Darling Street","city":"Cape Town","county":"City of Cape Town","state":"Western Cape","countrycode":"ZA"}}
            ]}
            """;
        using var handler = new LookupHandler(null, response);
        using var client = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new MunicipalityLocationService(client, new MunicipalityDirectory(), cache,
            new ConfigurationBuilder().Build());
        var place = await service.ResolveAsync(-33.9249, 18.4241, CancellationToken.None);
        Assert.Equal("Darling Street, Cape Town, Western Cape", place.Address);
        Assert.Equal("CPT", place.Municipality?.Code);
    }

    private sealed class LookupHandler(string? boundary, string? address) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.RequestUri!.AbsolutePath.EndsWith("/reverse") ? address : boundary;
            return Task.FromResult(new HttpResponseMessage(body is null ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            {
                Content = new StringContent(body ?? "{}", Encoding.UTF8, "application/json")
            });
        }
    }
}

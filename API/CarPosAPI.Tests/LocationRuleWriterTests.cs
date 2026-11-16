using System.Text.Json;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Common;
using CarPosAPI.Services.Trips;

namespace CarPosAPI.Tests;

/// <summary>
/// The value rules every location-rule write passes — from the rule endpoints and from the
/// place a journey's dialog sends nested in the journey's body, where the request
/// attributes are the client's word only — and the journey body's backward compatibility.
/// </summary>
public sealed class LocationRuleWriterTests
{
    private static readonly DateTime ValidFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static LocationRuleWriter.RuleValues Values(
        double latitude = 50.08,
        double longitude = 14.42,
        int radiusMeters = 200,
        bool matchStart = true,
        bool matchEnd = true,
        string? name = null) =>
        new LocationRuleWriter.RuleValues(name, latitude, longitude, radiusMeters, matchStart, matchEnd, ValidFrom);

    [Fact]
    public void AnOrdinaryPlacePasses()
    {
        Assert.Null(LocationRuleWriter.Check(Values()));
        Assert.Null(LocationRuleWriter.Check(Values(matchStart: false)));
        Assert.Null(LocationRuleWriter.Check(Values(matchEnd: false)));
        Assert.Null(LocationRuleWriter.Check(Values(latitude: -90, longitude: 180)));
        Assert.Null(LocationRuleWriter.Check(Values(radiusMeters: TripRules.MinLocationRuleRadiusMeters)));
        Assert.Null(LocationRuleWriter.Check(Values(radiusMeters: TripRules.MaxLocationRuleRadiusMeters)));
    }

    [Fact]
    public void ARuleMustMatchAtLeastOneEnd()
    {
        OperationResult<JourneyLocationRule>? refused = LocationRuleWriter.Check(Values(matchStart: false, matchEnd: false));

        Assert.NotNull(refused);
        Assert.Equal(OperationOutcome.Invalid, refused.Outcome);
        Assert.Equal(ErrorCodes.LocationRuleInvalid, refused.Code);
    }

    [Theory]
    [InlineData(90.0001, 14.0)]
    [InlineData(-91.0, 14.0)]
    [InlineData(50.0, 180.5)]
    [InlineData(double.NaN, 14.0)]
    [InlineData(50.0, double.PositiveInfinity)]
    public void APlaceOffTheGlobeIsRefused(double latitude, double longitude)
    {
        OperationResult<JourneyLocationRule>? refused = LocationRuleWriter.Check(Values(latitude, longitude));

        Assert.NotNull(refused);
        Assert.Equal(ErrorCodes.LocationRuleInvalid, refused.Code);
    }

    [Theory]
    [InlineData(TripRules.MinLocationRuleRadiusMeters - 1)]
    [InlineData(TripRules.MaxLocationRuleRadiusMeters + 1)]
    [InlineData(0)]
    public void ARadiusOutsideTheLimitsIsRefused(int radiusMeters)
    {
        OperationResult<JourneyLocationRule>? refused = LocationRuleWriter.Check(Values(radiusMeters: radiusMeters));

        Assert.NotNull(refused);
        Assert.Equal(ErrorCodes.LocationRuleInvalid, refused.Code);
    }

    [Fact]
    public void ANameLongerThanTheLimitIsRefused()
    {
        OperationResult<JourneyLocationRule>? refused =
            LocationRuleWriter.Check(Values(name: new string('x', TripRules.MaxNameLength + 1)));

        Assert.NotNull(refused);
        Assert.Equal(ErrorCodes.ValidationFailed, refused.Code);
    }

    [Fact]
    public void ARefusalKeepsItsOutcomeCodeAndParametersWhenRelayed()
    {
        OperationResult<JourneyLocationRule> tooMany = OperationResult<JourneyLocationRule>.Conflict(
            ErrorCodes.TooManyLocationRules,
            "Too many.",
            new Dictionary<string, object> { ["max"] = 100 });

        OperationResult<JourneyDto> relayed = LocationRuleWriter.Relay<JourneyDto>(tooMany);

        Assert.Equal(OperationOutcome.Conflict, relayed.Outcome);
        Assert.Equal(ErrorCodes.TooManyLocationRules, relayed.Code);
        Assert.Equal(100, relayed.Parameters!["max"]);
    }

    [Fact]
    public void AJourneyBodyWithoutAPlaceLeavesTheRulesAlone()
    {
        // Clients that predate the place send no "place" and no "endPlaceRuleId".
        const string body = """{"name":"Work","parentId":null,"rangeFrom":null,"rangeTo":null,"includePinned":false}""";

        JourneySaveRequestDto request = JsonSerializer.Deserialize<JourneySaveRequestDto>(
            body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Null(request.Place);
        Assert.Null(request.EndPlaceRuleId);
    }

    [Fact]
    public void AJourneyBodyCarriesItsPlace()
    {
        const string body = """
            {"name":"Work","parentId":null,"rangeFrom":null,"rangeTo":null,"includePinned":false,
             "place":{"ruleId":null,"name":"Office","latitude":50.08,"longitude":14.42,"radiusMeters":150,
                      "matchStart":false,"matchEnd":true,"validFrom":"2025-12-31T23:00:00.000Z"}}
            """;

        JourneySaveRequestDto request = JsonSerializer.Deserialize<JourneySaveRequestDto>(
            body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.NotNull(request.Place);
        Assert.Null(request.Place.RuleId);
        Assert.Equal(150, request.Place.RadiusMeters);
        Assert.False(request.Place.MatchStart);
        Assert.True(request.Place.MatchEnd);
        Assert.Equal(new DateTime(2025, 12, 31, 23, 0, 0, DateTimeKind.Utc), request.Place.ValidFrom!.Value.ToUniversalTime());
    }
}

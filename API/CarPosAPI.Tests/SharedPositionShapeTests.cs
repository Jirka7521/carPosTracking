using System.Reflection;
using System.Text.Json;
using CarPosAPI.Dtos;

namespace CarPosAPI.Tests;

/// <summary>
/// What an anonymous share visitor is allowed to receive.
///
/// <para>
/// <see cref="SharedPositionDto"/> is a deliberately impoverished cousin of
/// <see cref="PositionDto"/>, and the fields missing from it are the feature. This
/// pins the difference down, because the natural direction of drift is for somebody
/// to "fix" the share view by reusing the richer record — which would hand every
/// visitor the device's MQTT identity, its connectivity pattern, and how the
/// vehicle is being driven.
/// </para>
/// </summary>
public sealed class SharedPositionShapeTests
{
    /// <summary>
    /// Everything a visitor may ever be told about a fix. Not a minimum — an
    /// exhaustive list, asserted as one.
    /// </summary>
    private static readonly string[] PermittedProperties =
    [
        nameof(SharedPositionDto.Timestamp),
        nameof(SharedPositionDto.Latitude),
        nameof(SharedPositionDto.Longitude),
        nameof(SharedPositionDto.SpeedKmph),
        nameof(SharedPositionDto.BatteryPct),
        nameof(SharedPositionDto.TemperatureC),
    ];

    [Fact]
    public void TheVisitorRecordCarriesExactlyTheseFieldsAndNoOthers()
    {
        IEnumerable<string> actual = typeof(SharedPositionDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Where(name => !string.Equals(name, "EqualityContract", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal);

        Assert.Equal(PermittedProperties.OrderBy(name => name, StringComparer.Ordinal), actual);
    }

    [Fact]
    public void TheVisitorNeverLearnsTheDeviceIdentity()
    {
        // deviceId is the MQTT topic name, exact-case, and the broker address is
        // public. Together they are most of what somebody would need to go looking
        // for the tracker itself, so it must not appear even as a serialised key.
        SharedPositionDto position = new SharedPositionDto(
            new DateTime(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc),
            50.08,
            14.43,
            null,
            null,
            null);

        string json = JsonSerializer.Serialize(position);

        Assert.DoesNotContain("device", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("receivedAt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("altitude", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accel", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheRicherRecordItReplacesDoesCarryThoseFields()
    {
        // If this ever fails, PositionDto stopped carrying the things the share
        // record exists to withhold, and the distinction above became unnecessary.
        Assert.NotNull(typeof(PositionDto).GetProperty(nameof(PositionDto.DeviceId)));
        Assert.NotNull(typeof(PositionDto).GetProperty(nameof(PositionDto.ReceivedAt)));
        Assert.NotNull(typeof(PositionDto).GetProperty(nameof(PositionDto.AltitudeMeters)));
        Assert.NotNull(typeof(PositionDto).GetProperty(nameof(PositionDto.AccelXG)));
    }

    [Fact]
    public void TheSessionDescriptionNamesNoDeviceAndNoPerson()
    {
        // The other half of the guarantee: what the visitor is told about the share
        // itself. A label the creator chose, a window, and the two disclosure flags.
        ShareSessionDto session = new ShareSessionDto(
            "The car",
            new DateTime(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 12, 18, 0, 0, DateTimeKind.Utc),
            ShareScopeNames.LatestOnly,
            false,
            false);

        string json = JsonSerializer.Serialize(session);

        Assert.DoesNotContain("deviceId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("owner", json, StringComparison.OrdinalIgnoreCase);
    }
}

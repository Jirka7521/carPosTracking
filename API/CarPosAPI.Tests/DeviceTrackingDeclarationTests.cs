using System.Reflection;
using CarPosAPI.Dtos;

namespace CarPosAPI.Tests;

/// <summary>
/// Registering a device must not be possible without the operator declaring they
/// are entitled to track the vehicle and will tell the people who drive it.
///
/// This matters more than the registration acknowledgement does. A tracker usually
/// ends up in a car somebody else also drives, and that person is a data subject who
/// never signed up here and cannot be reached through the app: the declaration is
/// the whole of what puts the duty to inform them on the account holder, and a duty
/// nobody can show was accepted is a duty that will be denied later.
///
/// The refusal itself lives in <c>DeviceService.CreateAsync</c> and needs a database,
/// so what is pinned here is the contract in front of it — the field exists, it is a
/// bool, and it defaults to <c>false</c>. The realistic way this protection is lost
/// is not somebody deleting the guard; it is somebody defaulting the parameter to
/// <c>true</c> to stop an old client or a test fixture failing, which would wave
/// every caller through with a declaration nobody made.
/// </summary>
public sealed class DeviceTrackingDeclarationTests
{
    [Fact]
    public void TheDeclarationIsPartOfTheDeviceCreationContract()
    {
        ParameterInfo parameter = FindParameter(nameof(CreateDeviceRequestDto.TrackingDeclarationAccepted));

        Assert.Equal(typeof(bool), parameter.ParameterType);
    }

    [Fact]
    public void TheDeclarationDefaultsToRefused()
    {
        ParameterInfo parameter = FindParameter(nameof(CreateDeviceRequestDto.TrackingDeclarationAccepted));

        // A client that does not know about this field must be refused, not waved
        // through. Defaulting to true would silently forge a declaration for every
        // caller that omits it.
        Assert.True(parameter.HasDefaultValue);
        Assert.Equal(false, parameter.DefaultValue);
    }

    [Fact]
    public void ADeviceRequestStillCarriesEverythingElseItUsedTo()
    {
        // Guards against the declaration being added by *replacing* a field.
        CreateDeviceRequestDto request = new CreateDeviceRequestDto(
            "GNSS01",
            "The car",
            null,
            true);

        Assert.Equal("GNSS01", request.DeviceId);
        Assert.Equal("The car", request.DisplayName);
        Assert.Null(request.AdditionalAccesses);
        Assert.True(request.TrackingDeclarationAccepted);
    }

    [Fact]
    public void TheAcceptanceTimeHasSomewhereToBeRecorded()
    {
        // The declaration is only evidence if it is written down. Without this
        // column the checkbox is theatre: nothing would survive the request.
        PropertyInfo? column = typeof(Data.Entities.Device)
            .GetProperty(nameof(Data.Entities.Device.TrackingDeclarationAcceptedAt));

        Assert.NotNull(column);
        Assert.Equal(typeof(DateTime?), column.PropertyType);
    }

    /// <summary>Finds one parameter of the DTO's primary constructor by name.</summary>
    /// <param name="name">The parameter (and property) name.</param>
    /// <returns>The parameter, which carries the default value and attributes.</returns>
    private static ParameterInfo FindParameter(string name)
    {
        ConstructorInfo constructor = typeof(CreateDeviceRequestDto).GetConstructors().Single();

        ParameterInfo? parameter = constructor.GetParameters()
            .SingleOrDefault(candidate => candidate.Name == name);

        Assert.NotNull(parameter);

        return parameter;
    }
}

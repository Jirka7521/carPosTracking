using System.ComponentModel.DataAnnotations;
using System.Reflection;
using CarPosAPI.Dtos;

namespace CarPosAPI.Tests;

/// <summary>
/// Registration must not be able to create an account without a recorded
/// acknowledgement of the privacy policy.
///
/// Two layers enforce that, and neither is unit-testable the obvious way. The
/// version comparison lives in <c>UserAccountService</c> and needs a database; the
/// layer in front of it is the DataAnnotations on this DTO, which
/// <c>[ApiController]</c> applies through MVC's model metadata — <em>not</em>
/// through <see cref="Validator"/>, because on a positional record the attributes
/// sit on the constructor parameters rather than the generated properties.
///
/// So what is pinned here is the contract itself: the field exists, and it is
/// marked required. Deleting either is the realistic way this protection gets lost,
/// and both are caught below.
/// </summary>
public sealed class RegisterRequestValidationTests
{
    [Fact]
    public void TheAcknowledgementIsPartOfTheRegistrationContract()
    {
        ParameterInfo parameter = FindParameter(nameof(RegisterRequestDto.AcceptedPrivacyPolicyVersion));

        Assert.Equal(typeof(string), parameter.ParameterType);
    }

    [Fact]
    public void TheAcknowledgementIsRequired()
    {
        ParameterInfo parameter = FindParameter(nameof(RegisterRequestDto.AcceptedPrivacyPolicyVersion));

        // Required rejects a missing field and, by default, an empty string — so a
        // client cannot satisfy it by sending "".
        RequiredAttribute? required = parameter.GetCustomAttribute<RequiredAttribute>();

        Assert.NotNull(required);
        Assert.False(required.AllowEmptyStrings);
    }

    [Fact]
    public void ARegistrationStillCarriesEverythingElseItUsedTo()
    {
        // Guards against the acknowledgement being added by *replacing* a field.
        RegisterRequestDto request = new RegisterRequestDto(
            "someone@example.org",
            "a-sufficiently-long-password",
            "Test",
            "Person",
            "2026-09-06");

        Assert.Equal("someone@example.org", request.Email);
        Assert.Equal("Test", request.FirstName);
        Assert.Equal("Person", request.LastName);
        Assert.Equal("2026-09-06", request.AcceptedPrivacyPolicyVersion);
    }

    /// <summary>Finds one parameter of the DTO's primary constructor by name.</summary>
    /// <param name="name">The parameter (and property) name.</param>
    /// <returns>The parameter, which carries the validation attributes.</returns>
    private static ParameterInfo FindParameter(string name)
    {
        ConstructorInfo constructor = typeof(RegisterRequestDto).GetConstructors().Single();

        ParameterInfo? parameter = constructor.GetParameters()
            .SingleOrDefault(candidate => candidate.Name == name);

        Assert.NotNull(parameter);

        return parameter;
    }
}

using System.ComponentModel.DataAnnotations;
using System.Reflection;
using CarPosAPI.Dtos;

namespace CarPosAPI.Tests;

/// <summary>
/// Catches a mistake that compiles, passes every other test, and then throws on
/// the first real request to the endpoint.
///
/// <para>
/// On a positional <c>record</c>, an attribute written as
/// <c>[property: Required]</c> lands on the generated <em>property</em> instead of
/// on the constructor <em>parameter</em>. MVC's model binder refuses that
/// outright — <c>ModelMetadata.ThrowIfRecordTypeHasValidationOnProperties</c>
/// raises <c>InvalidOperationException</c> while binding the body — so the
/// endpoint returns a 500 for every call, however well-formed. Nothing before
/// that point objects: it is valid C#, the project builds clean, and a unit test
/// that constructs the record by hand never goes near model binding.
/// </para>
///
/// <para>
/// The rule is therefore asserted structurally here rather than through MVC's own
/// check, which is <c>internal</c>: for any DTO whose constructor parameter shares
/// a name with a property, the validation attributes must be on the parameter.
/// That is version-independent and needs no reference to the MVC internals it
/// mirrors.
/// </para>
/// </summary>
public sealed class RequestDtoValidationMetadataTests
{
    /// <summary>Every type in the DTO namespace, which is where the wire contract lives.</summary>
    public static TheoryData<Type> DtoTypes
    {
        get
        {
            TheoryData<Type> types = new TheoryData<Type>();

            foreach (Type type in typeof(ShareLinkCreateRequestDto).Assembly.GetTypes())
            {
                if (type.IsClass
                    && !type.IsAbstract
                    && string.Equals(type.Namespace, typeof(ShareLinkCreateRequestDto).Namespace, StringComparison.Ordinal))
                {
                    types.Add(type);
                }
            }

            return types;
        }
    }

    [Theory]
    [MemberData(nameof(DtoTypes))]
    public void ValidationAttributesSitOnConstructorParametersNotProperties(Type dtoType)
    {
        foreach (ConstructorInfo constructor in dtoType.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (ParameterInfo parameter in constructor.GetParameters())
            {
                if (parameter.Name is null)
                {
                    continue;
                }

                // A parameter whose name matches a property is a positional record's
                // primary-constructor parameter — precisely the case MVC polices.
                PropertyInfo? property = dtoType.GetProperty(
                    parameter.Name,
                    BindingFlags.Public | BindingFlags.Instance);

                if (property is null)
                {
                    continue;
                }

                ValidationAttribute[] onProperty = property
                    .GetCustomAttributes<ValidationAttribute>(inherit: true)
                    .ToArray();

                Assert.True(
                    onProperty.Length == 0,
                    $"{dtoType.Name}.{property.Name} carries {onProperty.Length} validation attribute(s) on the "
                    + "PROPERTY. On a positional record they must be on the constructor parameter — write "
                    + "[Required], not [property: Required]. MVC throws InvalidOperationException while binding "
                    + "the body otherwise, so every request to the endpoint using this DTO returns 500.");
            }
        }
    }

    [Fact]
    public void TheRequestDtosThatNeedValidationStillHaveIt()
    {
        // The test above is satisfied by having no validation at all, which would be
        // the wrong way to make it pass. These two are the share feature's untrusted
        // inputs — one of them is reachable without any authentication — so assert
        // that their rules survived being moved onto the parameters.
        AssertHasParameterValidation(typeof(ShareRedeemRequestDto), "Token");
        AssertHasParameterValidation(typeof(ShareRedeemRequestDto), "Passphrase");
        AssertHasParameterValidation(typeof(ShareLinkCreateRequestDto), "DeviceId");
        AssertHasParameterValidation(typeof(ShareLinkCreateRequestDto), "Scope");
    }

    /// <summary>
    /// Asserts that a named primary-constructor parameter carries at least one
    /// validation attribute.
    /// </summary>
    /// <param name="dtoType">The record to inspect.</param>
    /// <param name="parameterName">The parameter that must be validated.</param>
    private static void AssertHasParameterValidation(Type dtoType, string parameterName)
    {
        ConstructorInfo constructor = dtoType.GetConstructors().Single();

        ParameterInfo parameter = constructor
            .GetParameters()
            .Single(candidate => string.Equals(candidate.Name, parameterName, StringComparison.Ordinal));

        Assert.NotEmpty(parameter.GetCustomAttributes<ValidationAttribute>(inherit: true).ToArray());
    }
}

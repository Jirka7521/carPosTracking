using System.Reflection;
using CarPosAPI.Controllers;
using CarPosAPI.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;

namespace CarPosAPI.Tests;

/// <summary>
/// Pins the anonymous surface of the API and the per-account rate limits, both of
/// which live in attributes and so fail silently when one goes missing.
///
/// <para>
/// The authorization fallback policy (Program.cs) makes an endpoint with no
/// attribute require a session, so a forgotten <c>[Authorize]</c> now fails
/// closed. The opposite mistake — an <c>[AllowAnonymous]</c> where none belongs —
/// still fails open, which is what the first test is for: the anonymous actions
/// are listed here by name, and adding one is a decision this file has to record.
/// </para>
/// </summary>
public sealed class EndpointAccessMetadataTests
{
    /// <summary>Every action reachable without a session, as "Controller.Action".</summary>
    private static readonly string[] ExpectedAnonymousActions =
    [
        "AuthController.LoginAsync",
        "AuthController.Logout",
        "AuthController.RegisterAsync",
        "PrivacyController.GetPolicy",
        "ShareAccessController.Leave",
        "ShareAccessController.RedeemAsync",
    ];

    [Fact]
    public void OnlyTheIntendedActionsAreAnonymous()
    {
        List<string> anonymous = new List<string>();

        foreach (MethodInfo action in Actions())
        {
            bool allowsAnonymous = action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any()
                || action.DeclaringType!.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();

            if (allowsAnonymous)
            {
                anonymous.Add($"{action.DeclaringType!.Name}.{action.Name}");
            }
        }

        anonymous.Sort(StringComparer.Ordinal);

        Assert.Equal(ExpectedAnonymousActions, anonymous);
    }

    [Fact]
    public void EveryOtherActionNamesItsAuthorization()
    {
        // Belt and braces with the fallback policy: each non-anonymous action still
        // carries an [Authorize] of its own (or its controller's), so reading a
        // controller tells you how it is protected without opening Program.cs.
        foreach (MethodInfo action in Actions())
        {
            Type controller = action.DeclaringType!;

            bool anonymous = action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any()
                || controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();
            bool authorized = action.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any()
                || controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any();

            Assert.True(anonymous || authorized, $"{controller.Name}.{action.Name} has neither [Authorize] nor [AllowAnonymous].");
        }
    }

    [Fact]
    public void TheUserLookupIsRateLimitedPerAccount()
    {
        Assert.Equal(
            RateLimitPolicies.UserLookup,
            PolicyOn(typeof(UsersController), nameof(UsersController.SearchAsync)));
    }

    [Fact]
    public void DeviceRegistrationIsRateLimitedPerAccount()
    {
        Assert.Equal(
            RateLimitPolicies.DeviceRegistration,
            PolicyOn(typeof(DevicesController), nameof(DevicesController.CreateAsync)));
    }

    /// <summary>Every routed action on every controller in the API assembly.</summary>
    /// <returns>The action methods.</returns>
    private static IEnumerable<MethodInfo> Actions()
    {
        return typeof(ApiControllerBase).Assembly
            .GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any());
    }

    /// <summary>The rate-limiting policy named on one action.</summary>
    /// <param name="controller">The controller type.</param>
    /// <param name="action">The action's method name.</param>
    /// <returns>The policy name, or null when the action has none of its own.</returns>
    private static string? PolicyOn(Type controller, string action)
    {
        MethodInfo method = controller.GetMethod(action)!;

        return method.GetCustomAttribute<EnableRateLimitingAttribute>(inherit: true)?.PolicyName;
    }
}

using CarPosAPI.Controllers;
using CarPosAPI.Middleware;
using CarPosAPI.Services.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CarPosAPI.Tests;

/// <summary>
/// Covers the error shape every controller returns — the translation in
/// <see cref="ApiControllerBase"/> from a service outcome to a status code, and the
/// correlation id Program.cs stamps on every problem response.
///
/// <para>
/// The status mapping is a security control as much as a convention. "A device the
/// caller cannot see answers 404, not 403" only holds if
/// <see cref="OperationOutcome.NotFound"/> really does come out as a 404, and a
/// silent drift to 403 here would confirm that a guessed id exists — which is
/// precisely what an enumeration attempt is after. The table below is what pins it.
/// </para>
///
/// <para>
/// The traceId test exercises the real <see cref="ProblemDetailsFactory"/> rather
/// than a stub, because the question it answers is whether the customisation
/// registered on <c>AddProblemDetails</c> actually reaches responses built by MVC.
/// That is an assumption about framework wiring, and assumptions about wiring are
/// the ones worth testing.
/// </para>
/// </summary>
public sealed class ProblemResponseShapeTests
{
    private const string TraceId = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

    [Theory]
    [InlineData(OperationOutcome.NotFound, StatusCodes.Status404NotFound, "Not found")]
    [InlineData(OperationOutcome.Forbidden, StatusCodes.Status403Forbidden, "Forbidden")]
    [InlineData(OperationOutcome.Conflict, StatusCodes.Status409Conflict, "Conflict")]
    [InlineData(OperationOutcome.Invalid, StatusCodes.Status400BadRequest, "Invalid request")]
    public void MapsEachOutcomeToItsStatus(OperationOutcome outcome, int expectedStatus, string expectedTitle)
    {
        ObjectResult result = Map(new OperationResult<string>(outcome, null, "Something the user may read."));

        ProblemDetails problem = Assert.IsAssignableFrom<ProblemDetails>(result.Value);

        Assert.Equal(expectedStatus, result.StatusCode);
        Assert.Equal(expectedStatus, problem.Status);
        Assert.Equal(expectedTitle, problem.Title);
    }

    [Fact]
    public void SurfacesTheServiceDetailVerbatim()
    {
        // The service layer writes Detail for the end user, so it crosses the wire as
        // written. The contract that makes that safe lives on OperationResult itself:
        // never SQL, never a stack trace, never a fact the caller is not entitled to.
        const string Detail = "You do not have permission to delete this device.";

        ObjectResult result = Map(OperationResult<string>.Forbidden(ErrorCodes.NoPermissionDeleteDevice, Detail));

        Assert.Equal(Detail, Assert.IsAssignableFrom<ProblemDetails>(result.Value).Detail);
    }

    [Fact]
    public void CarriesTheServiceCodeAndParameters()
    {
        // The code is what the translated frontend keys on, and it must be the
        // service's own — not the status-based default the customisation adds while
        // MVC builds the body, which Failure() has to overwrite.
        Dictionary<string, object> parameters = new Dictionary<string, object>
        {
            ["name"] = "Night",
            ["count"] = 2,
        };

        ObjectResult result = Map(OperationResult<string>.Conflict(
            ErrorCodes.ProfileInUse,
            "\"Night\" is used by 2 rule(s). Delete or repoint them first.",
            parameters));

        ProblemDetails problem = Assert.IsAssignableFrom<ProblemDetails>(result.Value);

        Assert.Equal(ErrorCodes.ProfileInUse, Assert.Contains(ProblemCodeDefaults.CodeKey, problem.Extensions));
        Assert.Same(parameters, Assert.Contains(ProblemCodeDefaults.ParamsKey, problem.Extensions));
    }

    [Fact]
    public void OmitsParametersWhenThereAreNone()
    {
        ObjectResult result = Map(OperationResult<string>.NotFound(ErrorCodes.NoSuchDevice, "No such device."));

        ProblemDetails problem = Assert.IsAssignableFrom<ProblemDetails>(result.Value);

        Assert.DoesNotContain(ProblemCodeDefaults.ParamsKey, (IDictionary<string, object?>)problem.Extensions);
    }

    [Fact]
    public void GivesAValidationProblemACodeAndItsFieldNames()
    {
        // The automatic 400 is built by the framework, so its code can only come
        // from the customisation — the path the frontend relies on for it.
        using ServiceProvider provider = BuildServices();

        ProblemDetailsFactory factory = provider.GetRequiredService<ProblemDetailsFactory>();

        Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary modelState =
            new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary();
        modelState.AddModelError("DeviceId", "The DeviceId field is required.");

        ValidationProblemDetails problem = factory.CreateValidationProblemDetails(
            NewHttpContext(provider),
            modelState);

        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Contains(ProblemCodeDefaults.CodeKey, problem.Extensions));

        IReadOnlyDictionary<string, object> parameters = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object>>(
            Assert.Contains(ProblemCodeDefaults.ParamsKey, problem.Extensions));
        Assert.Equal(new[] { "DeviceId" }, Assert.IsType<string[]>(parameters["fields"]));
    }

    [Theory]
    [InlineData(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized)]
    [InlineData(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden)]
    [InlineData(StatusCodes.Status404NotFound, ErrorCodes.NotFound)]
    [InlineData(StatusCodes.Status429TooManyRequests, ErrorCodes.TooManyRequests)]
    [InlineData(StatusCodes.Status413PayloadTooLarge, ErrorCodes.BodyTooLarge)]
    [InlineData(StatusCodes.Status500InternalServerError, ErrorCodes.ServerError)]
    [InlineData(StatusCodes.Status503ServiceUnavailable, ErrorCodes.ServerError)]
    public void DerivesACodeFromTheStatusWhenNoneWasSet(int status, string expectedCode)
    {
        ProblemDetails problem = new ProblemDetails { Status = status };

        ProblemCodeDefaults.Apply(problem);

        Assert.Equal(expectedCode, Assert.Contains(ProblemCodeDefaults.CodeKey, problem.Extensions));
    }

    [Fact]
    public void KeepsACodeTheProducerAlreadySet()
    {
        // The CSRF middleware sets its own code before the body is written; the
        // status-based default must not replace it with a generic "forbidden".
        ProblemDetails problem = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Extensions = { [ProblemCodeDefaults.CodeKey] = ErrorCodes.CsrfInvalid },
        };

        ProblemCodeDefaults.Apply(problem);

        Assert.Equal(ErrorCodes.CsrfInvalid, problem.Extensions[ProblemCodeDefaults.CodeKey]);
    }

    [Fact]
    public void RefusesToTurnASuccessIntoAFailure()
    {
        Assert.Throws<ArgumentException>(() => Map(OperationResult<string>.Success("fine")));
    }

    [Fact]
    public void StampsTheCorrelationIdOnAControllerProblem()
    {
        // Failure() goes through ControllerBase.Problem, which builds its body with
        // the MVC ProblemDetailsFactory rather than with IProblemDetailsService. If
        // the customisation did not reach that path, 4xx responses would quietly lack
        // the id that 500s carry, and a caller could not report either consistently.
        ObjectResult result = Map(OperationResult<string>.NotFound(ErrorCodes.NoSuchDevice, "No such device."));

        ProblemDetails problem = Assert.IsAssignableFrom<ProblemDetails>(result.Value);

        Assert.Equal(TraceId, Assert.Contains("traceId", problem.Extensions));
    }

    [Fact]
    public void StampsTheCorrelationIdOnAValidationProblem()
    {
        // The automatic 400 that [ApiController] produces for a DataAnnotations
        // failure is built by the same factory, through a different method. Both are
        // checked because they are separate code paths in MVC.
        using ServiceProvider provider = BuildServices();

        ProblemDetailsFactory factory = provider.GetRequiredService<ProblemDetailsFactory>();

        ValidationProblemDetails problem = factory.CreateValidationProblemDetails(
            NewHttpContext(provider),
            new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary());

        Assert.Equal(TraceId, Assert.Contains("traceId", problem.Extensions));
    }

    // -----------------------------------------------------------------------
    // Fixtures
    // -----------------------------------------------------------------------

    /// <summary>Runs one result through the real controller translation.</summary>
    /// <typeparam name="TValue">The value type the result would have carried.</typeparam>
    /// <param name="result">The service result to translate.</param>
    /// <returns>The response the base class produced.</returns>
    private static ObjectResult Map<TValue>(OperationResult<TValue> result)
    {
        using ServiceProvider provider = BuildServices();

        TestController controller = new TestController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = NewHttpContext(provider),
            },
        };

        return controller.Map(result);
    }

    /// <summary>
    /// The MVC services a controller needs to build a ProblemDetails, with the same
    /// customisation Program.cs registers (the correlation id and the default code).
    /// </summary>
    /// <returns>A provider the caller disposes.</returns>
    private static ServiceProvider BuildServices()
    {
        ServiceCollection services = new ServiceCollection();

        services.AddLogging();
        services.AddControllers();
        services.AddProblemDetails((ProblemDetailsOptions options) =>
            options.CustomizeProblemDetails = (ProblemDetailsContext context) =>
            {
                context.ProblemDetails.Extensions["traceId"] = TraceId;
                ProblemCodeDefaults.Apply(context.ProblemDetails);
            });

        return services.BuildServiceProvider();
    }

    /// <summary>A context wired to the given services.</summary>
    /// <param name="provider">Services the framework will resolve from.</param>
    /// <returns>The context.</returns>
    private static DefaultHttpContext NewHttpContext(ServiceProvider provider)
    {
        return new DefaultHttpContext
        {
            RequestServices = provider,
        };
    }

    /// <summary>
    /// Exists only to reach the protected translation. Deliberately adds nothing of
    /// its own, so what is under test is the base class every real controller uses.
    /// </summary>
    private sealed class TestController : ApiControllerBase
    {
        /// <summary>Exposes <see cref="ApiControllerBase.Failure{TValue}"/>.</summary>
        /// <typeparam name="TValue">The value type the result would have carried.</typeparam>
        /// <param name="result">The service result to translate.</param>
        /// <returns>The response for that outcome.</returns>
        public ObjectResult Map<TValue>(OperationResult<TValue> result) => Failure(result);
    }
}

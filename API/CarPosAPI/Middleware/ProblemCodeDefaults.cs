using CarPosAPI.Services.Common;
using Microsoft.AspNetCore.Mvc;

namespace CarPosAPI.Middleware;

/// <summary>
/// Makes sure every ProblemDetails response carries an error <c>code</c>, even the
/// ones no code of ours builds: the automatic DataAnnotations 400 from
/// <c>[ApiController]</c>, the bodies <c>UseStatusCodePages</c> fills in for a bare
/// 401/403/404/405/429, and the 4xx/500 from <see cref="GlobalExceptionHandler"/>.
///
/// <para>
/// Runs from the <c>CustomizeProblemDetails</c> hook in Program.cs, which every
/// problem response passes through. It only ever fills a <em>missing</em> code, and
/// the code is derived from nothing but the status, so it cannot say more than the
/// status line already does. A producer that knows better — a service failure via
/// <c>ApiControllerBase.Failure</c>, the CSRF rejection — sets its own, either
/// before this runs or after it (MVC calls the hook while building the body, and
/// Failure writes its code over the default afterwards).
/// </para>
/// </summary>
public static class ProblemCodeDefaults
{
    /// <summary>The extension member holding one of <see cref="ErrorCodes"/>.</summary>
    public const string CodeKey = "code";

    /// <summary>The extension member holding the values a message mentions.</summary>
    public const string ParamsKey = "params";

    /// <summary>Adds a status-derived code to a problem that has none.</summary>
    /// <param name="problem">The response body being written.</param>
    public static void Apply(ProblemDetails problem)
    {
        ArgumentNullException.ThrowIfNull(problem);

        if (problem.Extensions.ContainsKey(CodeKey))
        {
            return;
        }

        if (problem is ValidationProblemDetails validation)
        {
            problem.Extensions[CodeKey] = ErrorCodes.ValidationFailed;

            // The field names only — the framework's messages stay English in
            // `errors`, but a translated client can at least say which fields.
            if (validation.Errors.Count > 0)
            {
                problem.Extensions[ParamsKey] = new Dictionary<string, object>
                {
                    ["fields"] = validation.Errors.Keys.ToArray(),
                };
            }

            return;
        }

        string? code = ForStatus(problem.Status);

        if (code is not null)
        {
            problem.Extensions[CodeKey] = code;
        }
    }

    /// <summary>The generic code for a status, or null for one with no generic meaning.</summary>
    /// <param name="status">The response status.</param>
    /// <returns>One of <see cref="ErrorCodes"/>, or null.</returns>
    private static string? ForStatus(int? status)
    {
        return status switch
        {
            StatusCodes.Status400BadRequest => ErrorCodes.RequestUnreadable,
            StatusCodes.Status401Unauthorized => ErrorCodes.Unauthorized,
            StatusCodes.Status403Forbidden => ErrorCodes.Forbidden,
            StatusCodes.Status404NotFound => ErrorCodes.NotFound,
            StatusCodes.Status405MethodNotAllowed => ErrorCodes.MethodNotAllowed,
            StatusCodes.Status413PayloadTooLarge => ErrorCodes.BodyTooLarge,
            StatusCodes.Status429TooManyRequests => ErrorCodes.TooManyRequests,
            StatusCodes.Status431RequestHeaderFieldsTooLarge => ErrorCodes.HeadersTooLarge,
            >= StatusCodes.Status500InternalServerError => ErrorCodes.ServerError,
            _ => null,
        };
    }
}

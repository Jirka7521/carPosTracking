namespace CarPosAPI.Services.Common;

/// <summary>
/// How a service call ended. Every value here is an <em>expected</em> outcome —
/// the caller lacked a permission, asked for something that is not there, or
/// collided with an existing row — so they travel as return values rather than
/// exceptions, which are reserved for the genuinely unexpected.
///
/// The values map one-to-one onto HTTP status codes in
/// <see cref="Controllers.ApiControllerBase"/>, which is the only place that
/// translation happens.
/// </summary>
public enum OperationOutcome
{
    /// <summary>The operation completed. → 200 / 201 / 204.</summary>
    Success = 0,

    /// <summary>
    /// The resource does not exist <em>or</em> the caller has no grant on it. The
    /// two are deliberately the same outcome for devices: answering 403 for a
    /// device you cannot see would confirm that it exists, which is exactly the
    /// enumeration hint an attacker wants. → 404.
    /// </summary>
    NotFound,

    /// <summary>
    /// The caller can see the resource but lacks the specific capability this
    /// operation needs. → 403.
    /// </summary>
    Forbidden,

    /// <summary>The write collided with something already there. → 409.</summary>
    Conflict,

    /// <summary>
    /// The request was well-formed but semantically wrong in a way DataAnnotations
    /// cannot express (wrong current password, self-referential grant, …). → 400.
    /// </summary>
    Invalid,
}

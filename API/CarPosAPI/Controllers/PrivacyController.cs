using CarPosAPI.Dtos;
using CarPosAPI.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CarPosAPI.Controllers;

/// <summary>
/// The privacy policy's machine-readable half: which version is in force, and who
/// is answerable for the processing.
///
/// Anonymous on purpose. The registration form has to show the acknowledgement and
/// echo back the version it displayed, and it does that before anybody has an
/// account — so a policy version behind a login would be a policy nobody could
/// agree to. Nothing here is a secret: it is the same text served at
/// <c>/privacy</c> in the dashboard and committed in docs/PRIVACY.md.
/// </summary>
[Route("api/privacy")]
[AllowAnonymous]
public sealed class PrivacyController : ApiControllerBase
{
    private readonly PrivacyOptions _privacy;

    /// <summary>Creates the controller.</summary>
    /// <param name="privacy">The configured controller identity and policy version.</param>
    public PrivacyController(IOptions<PrivacyOptions> privacy)
    {
        ArgumentNullException.ThrowIfNull(privacy);

        _privacy = privacy.Value;
    }

    /// <summary>Returns the privacy policy version currently in force.</summary>
    /// <returns>200 with the version and the controller's contact details.</returns>
    [HttpGet("policy")]
    [ProducesResponseType(typeof(PrivacyPolicyDto), StatusCodes.Status200OK)]
    public ActionResult<PrivacyPolicyDto> GetPolicy()
    {
        return Ok(new PrivacyPolicyDto(
            _privacy.PolicyVersion,
            _privacy.ControllerName,
            _privacy.ControllerContactEmail));
    }
}

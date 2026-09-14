using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Dtos;

/// <summary>
/// A visitor's attempt to open a share link.
///
/// <para>
/// <b>The token travels in this body, never in the request URL.</b> That is the
/// one thing this record exists to guarantee. The secret already appears in the
/// address of the page the visitor loaded, where the deployment's access log
/// redacts it; putting it in an API path as well would write it into a second log
/// that has no such rule, for no benefit.
/// </para>
/// </summary>
/// <param name="Token">The <c>selector.verifier</c> secret from the link.</param>
/// <param name="Passphrase">
/// The code, as typed. Case and punctuation are normalised before comparison, so a
/// visitor who drops the hyphens has not got it wrong.
/// </param>
public sealed record ShareRedeemRequestDto(
    [Required]
    [StringLength(128, MinimumLength = 1)]
    string Token,

    [Required]
    [StringLength(128, MinimumLength = 1)]
    string Passphrase);

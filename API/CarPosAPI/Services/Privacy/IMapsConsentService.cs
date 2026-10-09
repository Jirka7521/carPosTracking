using CarPosAPI.Dtos;
using CarPosAPI.Services.Common;

namespace CarPosAPI.Services.Privacy;

/// <summary>
/// The account's standing agreement to load the Google map — the one Art. 6(1)(a)
/// consent in the system, recorded so that a person who said "always" is asked once
/// rather than once per browser.
///
/// Three operations and no more: read it, give it, withdraw it. Withdrawal is a
/// single call with no confirmation step, because Art. 7(3) asks for it to be as
/// easy as giving was.
///
/// Share-link visitors have no account and never reach this; their answer lives in
/// a cookie on their own browser (FE <c>utils/mapsConsent.ts</c>).
/// </summary>
public interface IMapsConsentService
{
    /// <summary>Reads the caller's standing agreement.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The agreement (both members null when there is none), or <see cref="OperationOutcome.NotFound"/>.</returns>
    Task<OperationResult<MapsConsentDto>> GetAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Records that the caller agreed to this version of the prompt, now.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="version">The prompt version they were shown.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The stored agreement, or <see cref="OperationOutcome.NotFound"/>.</returns>
    Task<OperationResult<MapsConsentDto>> GrantAsync(int userId, string version, CancellationToken cancellationToken);

    /// <summary>Withdraws the caller's standing agreement. Idempotent.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>Success, or <see cref="OperationOutcome.NotFound"/>.</returns>
    Task<OperationResult<bool>> RevokeAsync(int userId, CancellationToken cancellationToken);
}

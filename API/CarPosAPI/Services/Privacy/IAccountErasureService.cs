using CarPosAPI.Services.Common;

namespace CarPosAPI.Services.Privacy;

/// <summary>
/// The right to erasure (GDPR Art. 17), implemented as an actual <c>DELETE</c>.
///
/// This is the one place in the application that deliberately breaks the
/// "records are never physically removed" rule the rest of the code follows. That
/// rule is right for ordinary operations — a revoked share keeps its audit trail,
/// a deleted device keeps its history — but it cannot be right for a person asking
/// to be forgotten, and a soft-delete flag on a row that still holds an email
/// address and a year of movements is not erasure by any reading of Art. 17.
///
/// What survives is deliberately impersonal: configuration revisions and grants
/// held by <em>other</em> people stay, with their reference to the erased account
/// nulled. The operational record of what happened survives; the link to who did
/// it does not.
/// </summary>
public interface IAccountErasureService
{
    /// <summary>Permanently erases an account and everything only it could see.</summary>
    /// <param name="userId">The account to erase. Always the caller's own.</param>
    /// <param name="password">The caller's current password, as proof of identity.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// A summary of what was removed, or <see cref="OperationOutcome.Invalid"/> when
    /// the password does not match — a stolen session cookie must not be enough to
    /// destroy somebody's data.
    /// </returns>
    Task<OperationResult<AccountErasureSummary>> EraseAsync(
        int userId,
        string password,
        CancellationToken cancellationToken);
}

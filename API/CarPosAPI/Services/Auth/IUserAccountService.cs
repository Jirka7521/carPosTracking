using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Common;

namespace CarPosAPI.Services.Auth;

/// <summary>
/// Everything that happens to a <see cref="User"/> row: creating one, checking a
/// sign-in, editing names and changing a password. The controller above it does
/// no more than model-bind, call one of these, and turn the result into a status
/// code.
/// </summary>
public interface IUserAccountService
{
    /// <summary>Creates an account.</summary>
    /// <param name="request">Validated registration details.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The new user, or <see cref="OperationOutcome.Conflict"/> if the email is taken.</returns>
    Task<OperationResult<User>> RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken);

    /// <summary>Verifies credentials and returns the matching user.</summary>
    /// <param name="request">Email and password as supplied by the caller.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>
    /// The user on success, otherwise <see cref="OperationOutcome.Invalid"/> with a
    /// message that does not distinguish "no such account" from "wrong password" —
    /// telling them apart is how an attacker enumerates who has an account here.
    /// </returns>
    Task<OperationResult<User>> AuthenticateAsync(LoginRequestDto request, CancellationToken cancellationToken);

    /// <summary>Loads one user's public profile.</summary>
    /// <param name="userId">Id of the user to load.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The profile, or <see cref="OperationOutcome.NotFound"/>.</returns>
    Task<OperationResult<UserProfileDto>> GetProfileAsync(int userId, CancellationToken cancellationToken);

    /// <summary>
    /// Finds the user with exactly this email address, for the sharing picker.
    /// </summary>
    /// <param name="callerId">The authenticated caller; must hold <c>CanShare</c> on at least one device.</param>
    /// <param name="email">The full address. Matched for equality, never as a prefix.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>
    /// The single matching profile, or an empty list — "no match" is a valid answer.
    /// The names are blank unless the two accounts already share a device: the
    /// sharing form needs only the id. <see cref="OperationOutcome.Forbidden"/> when
    /// the caller cannot share anything and so has no use for the lookup.
    /// </returns>
    Task<OperationResult<IReadOnlyList<UserProfileDto>>> SearchByEmailAsync(
        int callerId,
        string email,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads another user's profile, but only if the caller is entitled to see it:
    /// themselves, or somebody they share a device with.
    /// </summary>
    /// <param name="callerId">The authenticated caller.</param>
    /// <param name="userId">The profile being asked for.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>
    /// The profile, or <see cref="OperationOutcome.NotFound"/> — which is also the
    /// answer for a real account the caller has no business seeing, so this endpoint
    /// cannot be scanned to enumerate the user table.
    /// </returns>
    Task<OperationResult<UserProfileDto>> GetVisibleProfileAsync(
        int callerId,
        int userId,
        CancellationToken cancellationToken);

    /// <summary>Updates a user's own names.</summary>
    /// <param name="userId">The user being edited (must be the caller).</param>
    /// <param name="request">The partial update; null members are left alone.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The updated profile, or <see cref="OperationOutcome.NotFound"/>.</returns>
    Task<OperationResult<UserProfileDto>> UpdateProfileAsync(
        int userId,
        UserUpdateRequestDto request,
        CancellationToken cancellationToken);

    /// <summary>Changes a user's password after verifying the current one.</summary>
    /// <param name="userId">The user being changed (must be the caller).</param>
    /// <param name="request">Current and new password.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>Success, or <see cref="OperationOutcome.Invalid"/> when the current password is wrong.</returns>
    Task<OperationResult<bool>> ChangePasswordAsync(
        int userId,
        ChangePasswordRequestDto request,
        CancellationToken cancellationToken);
}

namespace CarPosAPI.Services.Auth;

/// <summary>
/// The parts of a session token that decide whether it is still a live session:
/// whose it is, which stamp it was issued under, its id and when it ends.
/// Read from a validated token by <see cref="SessionClaims.TryRead"/>.
/// </summary>
/// <param name="UserId">The <c>sub</c> claim.</param>
/// <param name="SecurityStamp">The <c>stamp</c> claim — the user's stamp when the token was issued.</param>
/// <param name="TokenId">The <c>jti</c> claim.</param>
/// <param name="ExpiresAtUtc">The <c>exp</c> claim.</param>
public sealed record SessionIdentity(int UserId, string SecurityStamp, string TokenId, DateTime ExpiresAtUtc);

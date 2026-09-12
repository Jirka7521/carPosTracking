using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Auth;
using CarPosAPI.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Services.Sharing;

/// <summary>
/// Implements <see cref="IShareRedemptionService"/>.
///
/// <para>
/// <b>The order of the checks below is the security of the feature</b>, and the
/// line it draws is this: <em>nothing is admitted to anyone who has not already
/// proved they hold the link</em>. An unknown selector, a wrong verifier and a
/// malformed token all produce one identical refusal, and all three spend the same
/// time producing it. Past that point the caller demonstrably has the link, so the
/// messages become honest — "expired", "revoked", "wrong code", "wait five
/// minutes" — because at that point candour costs nothing an attacker could use,
/// and its absence costs a recipient the ability to tell a dead link from a
/// mistyped code.
/// </para>
///
/// <para>
/// The alternative, refusing everything with one message forever, sounds safer and
/// is not: it hides nothing from someone holding a valid link, and it guarantees
/// that every ordinary expiry becomes a phone call to whoever created the share.
/// </para>
///
/// Scoped — it owns a scoped <see cref="CarPosDbContext"/>.
/// </summary>
internal sealed class ShareRedemptionService : IShareRedemptionService
{
    /// <summary>
    /// Fed to the hasher on the miss path so that "no such link" costs the same
    /// wall-clock time as "wrong code". Its value is irrelevant; only the work is.
    /// </summary>
    private const string DummyPassphrase = "TIMING-EQUALISATION-ONLY";

    /// <summary>
    /// A PBKDF2 hash to verify against when there is no real one, computed once per
    /// process from the <em>injected</em> hasher — so if the hashing parameters ever
    /// change, the decoy's cost changes with them and the two paths stay level.
    ///
    /// Two threads racing to set this both store a usable hash, so the race is
    /// benign and not worth a lock on a field written once.
    /// </summary>
    private static string? s_dummyHash;

    private readonly CarPosDbContext _context;
    private readonly IShareTokenFactory _tokens;
    private readonly IPassphraseGenerator _passphrases;
    private readonly IPasswordHasher _hasher;
    private readonly ILogger<ShareRedemptionService> _logger;

    /// <summary>Creates the service.</summary>
    /// <param name="context">Scoped database context.</param>
    /// <param name="tokens">Parses and verifies the link secret.</param>
    /// <param name="passphrases">Canonicalises the typed code.</param>
    /// <param name="hasher">Verifies the code against its stored PBKDF2 hash.</param>
    /// <param name="logger">Structured logger.</param>
    public ShareRedemptionService(
        CarPosDbContext context,
        IShareTokenFactory tokens,
        IPassphraseGenerator passphrases,
        IPasswordHasher hasher,
        ILogger<ShareRedemptionService> logger)
    {
        _context = context;
        _tokens = tokens;
        _passphrases = passphrases;
        _hasher = hasher;
        _logger = logger;

        s_dummyHash ??= hasher.Hash(DummyPassphrase);
    }

    /// <inheritdoc />
    public async Task<OperationResult<ShareRedemption>> RedeemAsync(
        string token,
        string passphrase,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(passphrase);

        // Shape first, so a malformed or oversized token never becomes a query.
        if (!_tokens.TryParse(token, out string selector, out string verifier))
        {
            return NoSuchLink();
        }

        // Tracked, not AsNoTracking: a wrong code has to be recorded, and recording
        // it is what makes the cooldown work.
        ShareLink? link = await _context.ShareLinks
            .SingleOrDefaultAsync(candidate => candidate.Selector == selector, cancellationToken);

        if (link is null || !_tokens.VerifierMatches(link.VerifierHash, verifier))
        {
            return NoSuchLink();
        }

        // --- Past this line, the caller holds the link. ------------------------

        DateTime nowUtc = DateTime.UtcNow;

        if (link.RevokedAt.HasValue)
        {
            return OperationResult<ShareRedemption>.NotFound(
                "This link has been withdrawn by the person who shared it.");
        }

        if (nowUtc > link.ValidUntil)
        {
            return OperationResult<ShareRedemption>.NotFound(
                "This link has expired. Ask for a new one if you still need access.");
        }

        if (nowUtc < link.ValidFrom)
        {
            return OperationResult<ShareRedemption>.NotFound(
                "This link is not active yet. It starts working at the time it was shared for.");
        }

        if (link.LockedUntil.HasValue && link.LockedUntil.Value > nowUtc)
        {
            return OperationResult<ShareRedemption>.Forbidden(
                DescribeCooldown(link.LockedUntil.Value - nowUtc));
        }

        string normalised = _passphrases.Normalise(passphrase);
        PasswordCheckResult check = _hasher.Check(link.PassphraseHash, normalised);

        if (check == PasswordCheckResult.Failed)
        {
            return await RecordFailureAsync(link, nowUtc, cancellationToken);
        }

        if (check == PasswordCheckResult.ValidNeedsRehash)
        {
            // Same courtesy the sign-in path extends: the code was right, so quietly
            // move it onto the current hashing parameters while we hold the plaintext.
            link.PassphraseHash = _hasher.Hash(normalised);
        }

        link.FailedAttempts = 0;
        link.LockedUntil = null;
        link.SuccessfulRedeems++;
        link.LastAccessedAt = nowUtc;

        await _context.SaveChangesAsync(cancellationToken);

        // The share id, and nothing that identifies the visitor. There is nothing
        // else to log: we deliberately store no address, no agent and no fingerprint.
        _logger.LogInformation("Share link {ShareId} was opened successfully", link.Id);

        return OperationResult<ShareRedemption>.Success(
            new ShareRedemption(link.Id, link.ValidUntil, ToSession(link)));
    }

    /// <summary>
    /// Records a wrong code, extends the cooldown, and refuses.
    /// </summary>
    /// <param name="link">The link that was addressed — tracked, so the counters persist.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The refusal to hand back to the visitor.</returns>
    private async Task<OperationResult<ShareRedemption>> RecordFailureAsync(
        ShareLink link,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        link.FailedAttempts++;
        link.LockedUntil = ShareCooldownPolicy.NextUnlockUtc(link.FailedAttempts, nowUtc);

        await _context.SaveChangesAsync(cancellationToken);

        // Warning rather than Information: a rising count on one link is the only
        // signal that somebody is working on it, since nothing about the visitor is
        // recorded. It is also what the creator sees in their share list.
        _logger.LogWarning(
            "Share link {ShareId} was given a wrong code ({FailedAttempts} consecutive)",
            link.Id,
            link.FailedAttempts);

        if (link.LockedUntil.HasValue)
        {
            return OperationResult<ShareRedemption>.Forbidden(
                DescribeCooldown(link.LockedUntil.Value - nowUtc));
        }

        return OperationResult<ShareRedemption>.Forbidden(
            "That code is not right. Check it and try again.");
    }

    /// <summary>
    /// Phrases a cooldown for somebody who is probably retyping a code off a phone.
    /// </summary>
    /// <param name="remaining">How long is left on the lock.</param>
    /// <returns>A message safe to show, naming only a duration.</returns>
    private static string DescribeCooldown(TimeSpan remaining)
    {
        // Rounded up, so "try again in 1 minute" is never advice to try again and be
        // refused. Minutes only: a share page has no business counting seconds at
        // somebody.
        int minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));

        return minutes == 1
            ? "Too many wrong codes. Try again in about a minute."
            : $"Too many wrong codes. Try again in about {minutes} minutes.";
    }

    /// <summary>
    /// The single refusal used for every failure that happens before the link is
    /// proved to exist.
    ///
    /// It burns a PBKDF2 verification on the way out. Without that, "no such link"
    /// would answer in a millisecond and "wrong code" in fifty, and the difference
    /// would turn this endpoint into an oracle for which selectors are real.
    /// </summary>
    /// <returns>The opaque not-found result.</returns>
    private OperationResult<ShareRedemption> NoSuchLink()
    {
        _hasher.Check(s_dummyHash!, DummyPassphrase);

        return OperationResult<ShareRedemption>.NotFound(
            "This link is not valid. Check that you opened the whole address you were sent.");
    }

    /// <summary>Describes a share to the visitor who just opened it.</summary>
    /// <param name="link">The opened link.</param>
    /// <returns>The session description, carrying nothing identifying.</returns>
    private static ShareSessionDto ToSession(ShareLink link)
    {
        return new ShareSessionDto(
            link.Label,
            link.ValidFrom,
            link.ValidUntil,
            link.Scope == ShareScope.FullTrack ? ShareScopeNames.FullTrack : ShareScopeNames.LatestOnly,
            link.IncludeSpeed,
            link.IncludeTelemetry);
    }
}

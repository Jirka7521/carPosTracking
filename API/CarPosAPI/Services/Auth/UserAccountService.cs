using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Options;
using CarPosAPI.Services.Common;
using CarPosAPI.Services.Sharing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CarPosAPI.Services.Auth;

/// <summary>
/// Implements <see cref="IUserAccountService"/> over EF Core.
///
/// Two rules run through the whole class. First, <b>emails are normalised to lower
/// case</b> on every write and every lookup, so an account can never be duplicated
/// by capitalisation. Second, <b>authentication failures are indistinguishable</b>:
/// a wrong password and an unknown address produce the identical message, because
/// the difference between them is precisely the information an attacker is
/// probing for.
///
/// Scoped — it holds a scoped <see cref="CarPosDbContext"/>.
/// </summary>
internal sealed class UserAccountService : IUserAccountService
{
    /// <summary>The single message every failed sign-in gets, whatever went wrong.</summary>
    private const string InvalidCredentialsMessage = "Incorrect email or password.";

    private readonly CarPosDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISessionRegistry _sessions;
    private readonly IPassphraseGenerator _passphrases;
    private readonly PrivacyOptions _privacy;
    private readonly RegistrationOptions _registration;
    private readonly ILogger<UserAccountService> _logger;

    /// <summary>Creates the service.</summary>
    /// <param name="context">Scoped database context.</param>
    /// <param name="passwordHasher">Hashes and verifies passwords.</param>
    /// <param name="sessions">Told when a password change ends a user's sessions.</param>
    /// <param name="passphrases">Normalises invite codes the way they were minted.</param>
    /// <param name="privacy">Supplies the privacy-policy version registration must match.</param>
    /// <param name="registration">Who may sign up (open, invite-only or closed).</param>
    /// <param name="logger">Structured logger — never receives passwords, hashes or invite codes.</param>
    public UserAccountService(
        CarPosDbContext context,
        IPasswordHasher passwordHasher,
        ISessionRegistry sessions,
        IPassphraseGenerator passphrases,
        IOptions<PrivacyOptions> privacy,
        IOptions<RegistrationOptions> registration,
        ILogger<UserAccountService> logger)
    {
        ArgumentNullException.ThrowIfNull(privacy);
        ArgumentNullException.ThrowIfNull(registration);

        _context = context;
        _passwordHasher = passwordHasher;
        _sessions = sessions;
        _passphrases = passphrases;
        _privacy = privacy.Value;
        _registration = registration.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult<User>> RegisterAsync(
        RegisterRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Whether this caller may sign up at all is settled first, before any other
        // check is made or answered. Under invite-only sign-up that is what keeps the
        // form from telling a stranger anything — not even whether an email address
        // already has an account, which the unique-index conflict below would.
        if (_registration.Mode == RegistrationMode.Closed)
        {
            return OperationResult<User>.Forbidden(
                ErrorCodes.RegistrationClosed,
                "Registration is closed. Existing accounts can still sign in.");
        }

        string? inviteCodeHash = null;

        if (_registration.Mode == RegistrationMode.InviteOnly)
        {
            inviteCodeHash = RegistrationInviteCodes.Hash(_passphrases, request.InviteCode);

            // A read-only look first, so a missing or wrong code is refused before a
            // full PBKDF2 hash is spent on the password. The code is only claimed —
            // atomically — inside the transaction below.
            DateTime checkedAt = DateTime.UtcNow;
            bool usable = inviteCodeHash is not null
                && await _context.RegistrationInvites
                    .AsNoTracking()
                    .AnyAsync(
                        invite => invite.CodeHash == inviteCodeHash
                            && invite.UsedAt == null
                            && invite.ExpiresAt > checkedAt,
                        cancellationToken);

            if (!usable)
            {
                _logger.LogInformation("Registration refused: no usable invite code");
                return InviteCodeInvalid();
            }
        }

        string email = NormaliseEmail(request.Email);

        // The acknowledgement is checked before anything is written. A form left
        // open across a policy change echoes back the old version, and recording
        // that as consent to the new text would make the stored record a lie —
        // which is precisely the thing GDPR Art. 7(1) asks the controller to be
        // able to produce.
        if (!string.Equals(
                request.AcceptedPrivacyPolicyVersion.Trim(),
                _privacy.PolicyVersion,
                StringComparison.Ordinal))
        {
            return OperationResult<User>.Invalid(
                ErrorCodes.PrivacyPolicyChanged,
                "The privacy policy has changed since this page was opened. Please reload and read it again before registering.");
        }

        // Hashed once, outside the transaction: PBKDF2 is the expensive part, and a
        // replay of the transaction below must not pay for it again.
        string passwordHash = _passwordHasher.Hash(request.Password);

        // Claiming the code and creating the account are one unit of work: a code is
        // spent only by an account that actually came into being, and an account is
        // only created by a code nobody else claimed first. The connection retries
        // transient faults (Program.cs), so the transaction runs inside the execution
        // strategy, which may replay it whole.
        IExecutionStrategy strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(
            async (CancellationToken attemptToken) =>
            {
                // A failed earlier attempt may have left the user staged; start clean.
                _context.ChangeTracker.Clear();

                await using IDbContextTransaction transaction =
                    await _context.Database.BeginTransactionAsync(attemptToken);

                if (inviteCodeHash is not null)
                {
                    // Conditional on still being unused, so two registrations racing
                    // with one code cannot both succeed: exactly one update lands, and
                    // the loser rolls back with nothing written.
                    DateTime usedAt = DateTime.UtcNow;
                    int claimed = await _context.RegistrationInvites
                        .Where(invite => invite.CodeHash == inviteCodeHash
                            && invite.UsedAt == null
                            && invite.ExpiresAt > usedAt)
                        .ExecuteUpdateAsync(
                            setters => setters.SetProperty(invite => invite.UsedAt, (DateTime?)usedAt),
                            attemptToken);

                    if (claimed != 1)
                    {
                        _logger.LogInformation("Registration refused: the invite code was claimed or expired meanwhile");
                        return InviteCodeInvalid();
                    }
                }

                User user = new User
                {
                    Email = email,
                    PasswordHash = passwordHash,
                    SecurityStamp = SecurityStamps.New(),
                    FirstName = request.FirstName.Trim(),
                    LastName = request.LastName.Trim(),
                    PrivacyPolicyVersion = _privacy.PolicyVersion,
                    PrivacyPolicyAcceptedAt = DateTime.UtcNow,
                };

                _context.Users.Add(user);

                try
                {
                    await _context.SaveChangesAsync(attemptToken);
                }
                catch (DbUpdateException exception)
                    when (exception.InnerException is PostgresException postgres
                        && postgres.SqlState == PostgresErrorCodes.UniqueViolation)
                {
                    // The unique index is the only check performed — a pre-flight "does
                    // this email exist?" query would both race and hand out
                    // account-existence information to anyone who cared to ask. Leaving
                    // without a commit rolls the invite claim back too, so a mistyped
                    // email does not burn the code.
                    return OperationResult<User>.Conflict(ErrorCodes.EmailTaken, "An account with that email address already exists.");
                }

                await transaction.CommitAsync(attemptToken);

                _logger.LogInformation(
                    "Registered user {UserId} ({RegistrationMode})",
                    user.Id,
                    _registration.Mode);

                return OperationResult<User>.Success(user);
            },
            cancellationToken);
    }

    /// <summary>
    /// The one answer for every unusable invite code — missing, unknown, used or
    /// expired — so the response says nothing about which.
    /// </summary>
    /// <returns>A 400 result carrying <see cref="ErrorCodes.InviteCodeInvalid"/>.</returns>
    private static OperationResult<User> InviteCodeInvalid()
    {
        return OperationResult<User>.Invalid(
            ErrorCodes.InviteCodeInvalid,
            "Registration needs a valid invite code. Ask the person who runs this site for one; each code works once.");
    }

    /// <inheritdoc />
    public async Task<OperationResult<User>> AuthenticateAsync(
        LoginRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string email = NormaliseEmail(request.Email);

        // Tracked (not AsNoTracking) because a successful sign-in may need to
        // rewrite the hash — see the rehash branch below.
        User? user = await _context.Users
            .SingleOrDefaultAsync(candidate => candidate.Email == email, cancellationToken);

        if (user is null)
        {
            // Spend the same PBKDF2 work a wrong password costs. Skipping it made
            // this answer a full hash run faster than a real failure, and that gap is
            // measurable over the internet: a stopwatch was enough to tell which
            // addresses have an account. The rate limit on the endpoint slows such a
            // probe down; this removes what it was measuring.
            _passwordHasher.SimulateCheck(request.Password);

            _logger.LogInformation("Sign-in attempt for an unknown email address");
            return OperationResult<User>.Invalid(ErrorCodes.InvalidCredentials, InvalidCredentialsMessage);
        }

        PasswordCheckResult check = _passwordHasher.Check(user.PasswordHash, request.Password);

        if (check == PasswordCheckResult.Failed)
        {
            _logger.LogInformation("Failed sign-in for user {UserId}", user.Id);
            return OperationResult<User>.Invalid(ErrorCodes.InvalidCredentials, InvalidCredentialsMessage);
        }

        if (check == PasswordCheckResult.ValidNeedsRehash)
        {
            // The password is right but stored under weaker parameters than the
            // framework now uses. This is the only moment the plaintext is available
            // to upgrade it, so take it.
            user.PasswordHash = _passwordHasher.Hash(request.Password);
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Upgraded the stored password hash for user {UserId}", user.Id);
        }

        return OperationResult<User>.Success(user);
    }

    /// <inheritdoc />
    public async Task<OperationResult<UserProfileDto>> GetProfileAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        UserProfileDto? profile = await _context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new UserProfileDto(user.Id, user.Email, user.FirstName, user.LastName))
            .SingleOrDefaultAsync(cancellationToken);

        return profile is null
            ? OperationResult<UserProfileDto>.NotFound(ErrorCodes.NoSuchUser, "No such user.")
            : OperationResult<UserProfileDto>.Success(profile);
    }

    /// <inheritdoc />
    public async Task<OperationResult<UserProfileDto>> GetVisibleProfileAsync(
        int callerId,
        int userId,
        CancellationToken cancellationToken)
    {
        // Your own profile is always visible.
        if (callerId == userId)
        {
            return await GetProfileAsync(userId, cancellationToken);
        }

        // Otherwise the two accounts must have a device in common. That is exactly
        // the case the endpoint exists for — rendering the names on a device's
        // sharing list — and nothing wider. Without this check a plain integer scan
        // over /api/users/{id} dumps the whole user table, one row per request.
        //
        // The check is only as good as the way grants come into being. When a sharer
        // could grant any user id on the spot, "shares a device" was something one
        // side could create alone, and the scan worked again one grant at a time.
        // Grants now come only from an invitation the recipient accepted
        // (AccessInvitationService), so a shared device means both people agreed.
        if (!await SharesADeviceAsync(callerId, userId, cancellationToken))
        {
            // 404, not 403: a 403 would confirm the id belongs to a real account,
            // which is the same enumeration hint the device endpoints refuse to give.
            return OperationResult<UserProfileDto>.NotFound(ErrorCodes.NoSuchUser, "No such user.");
        }

        return await GetProfileAsync(userId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<UserProfileDto>> UpdateProfileAsync(
        int userId,
        UserUpdateRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        User? user = await _context.Users
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        if (user is null)
        {
            return OperationResult<UserProfileDto>.NotFound(ErrorCodes.NoSuchUser, "No such user.");
        }

        // Null means "leave it"; the DTO's StringLength(MinimumLength = 1) has
        // already rejected a present-but-empty name, so no blank-name check is
        // needed here.
        if (request.FirstName is not null)
        {
            user.FirstName = request.FirstName.Trim();
        }

        if (request.LastName is not null)
        {
            user.LastName = request.LastName.Trim();
        }

        await _context.SaveChangesAsync(cancellationToken);

        return OperationResult<UserProfileDto>.Success(
            new UserProfileDto(user.Id, user.Email, user.FirstName, user.LastName));
    }

    /// <inheritdoc />
    public async Task<OperationResult<User>> ChangePasswordAsync(
        int userId,
        ChangePasswordRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        User? user = await _context.Users
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        if (user is null)
        {
            return OperationResult<User>.NotFound(ErrorCodes.NoSuchUser, "No such user.");
        }

        PasswordCheckResult check = _passwordHasher.Check(user.PasswordHash, request.CurrentPassword);

        if (check == PasswordCheckResult.Failed)
        {
            // Proof-of-identity failed. Without this gate a stolen session cookie
            // would be upgradable into permanent account takeover.
            _logger.LogInformation("Password change refused for user {UserId}: current password did not match", userId);
            return OperationResult<User>.Invalid(ErrorCodes.WrongCurrentPassword, "Your current password is not correct.");
        }

        // A new stamp ends every session issued under the old password, in every
        // browser — the usual reason for changing a password is that someone else
        // may know it. The caller gets a fresh session from the controller.
        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        user.SecurityStamp = SecurityStamps.New();
        await _context.SaveChangesAsync(cancellationToken);
        _sessions.ForgetUser(userId);

        _logger.LogInformation("Password changed for user {UserId}; earlier sessions ended", userId);

        return OperationResult<User>.Success(user);
    }

    /// <summary>
    /// Whether two accounts both hold an active grant on the same device — the line
    /// between a stranger and somebody whose name the caller may see.
    /// </summary>
    /// <param name="callerId">The authenticated caller.</param>
    /// <param name="userId">The other account.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>True when they share at least one device.</returns>
    private Task<bool> SharesADeviceAsync(int callerId, int userId, CancellationToken cancellationToken)
    {
        return _context.Accesses
            .AsNoTracking()
            .Where(theirs => theirs.UserId == userId && theirs.IsActive)
            .AnyAsync(
                theirs => _context.Accesses.Any(mine =>
                    mine.UserId == callerId
                    && mine.IsActive
                    && mine.DeviceId == theirs.DeviceId),
                cancellationToken);
    }

    /// <summary>
    /// Normalises an email for storage and comparison. Invariant lower-casing, not
    /// culture-aware: the Turkish 'I' would otherwise fold differently depending on
    /// the server's locale, so the same address could match or miss its own row.
    /// </summary>
    /// <param name="email">The raw address as supplied.</param>
    /// <returns>The trimmed, lower-cased form.</returns>
    private static string NormaliseEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }
}

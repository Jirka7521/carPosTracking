using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Options;
using CarPosAPI.Services.Common;
using Microsoft.EntityFrameworkCore;
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
    private readonly PrivacyOptions _privacy;
    private readonly ILogger<UserAccountService> _logger;

    /// <summary>Creates the service.</summary>
    /// <param name="context">Scoped database context.</param>
    /// <param name="passwordHasher">Hashes and verifies passwords.</param>
    /// <param name="privacy">Supplies the privacy-policy version registration must match.</param>
    /// <param name="logger">Structured logger — never receives passwords or hashes.</param>
    public UserAccountService(
        CarPosDbContext context,
        IPasswordHasher passwordHasher,
        IOptions<PrivacyOptions> privacy,
        ILogger<UserAccountService> logger)
    {
        ArgumentNullException.ThrowIfNull(privacy);

        _context = context;
        _passwordHasher = passwordHasher;
        _privacy = privacy.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult<User>> RegisterAsync(
        RegisterRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

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
                "The privacy policy has changed since this page was opened. Please reload and read it again before registering.");
        }

        User user = new User
        {
            Email = email,
            PasswordHash = _passwordHasher.Hash(request.Password),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PrivacyPolicyVersion = _privacy.PolicyVersion,
            PrivacyPolicyAcceptedAt = DateTime.UtcNow,
        };

        _context.Users.Add(user);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException postgres
                && postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // The unique index is the only check performed — a pre-flight "does this
            // email exist?" query would both race and hand out account-existence
            // information to anyone who cared to ask.
            return OperationResult<User>.Conflict("An account with that email address already exists.");
        }

        _logger.LogInformation("Registered user {UserId}", user.Id);

        return OperationResult<User>.Success(user);
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
            // No password check to skip past here, so this path is faster than a
            // real failure. That timing difference is a weak enumeration signal;
            // rate limiting on the endpoint (see Program.cs) is what actually
            // closes it, since a constant-time fake hash would still leak through
            // other channels.
            _logger.LogInformation("Sign-in attempt for an unknown email address");
            return OperationResult<User>.Invalid(InvalidCredentialsMessage);
        }

        PasswordCheckResult check = _passwordHasher.Check(user.PasswordHash, request.Password);

        if (check == PasswordCheckResult.Failed)
        {
            _logger.LogInformation("Failed sign-in for user {UserId}", user.Id);
            return OperationResult<User>.Invalid(InvalidCredentialsMessage);
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
            ? OperationResult<UserProfileDto>.NotFound("No such user.")
            : OperationResult<UserProfileDto>.Success(profile);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserProfileDto>> SearchByEmailAsync(
        string email,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        string needle = NormaliseEmail(email);

        if (needle.Length == 0)
        {
            return [];
        }

        // EXACT MATCH ONLY, deliberately. This used to also offer a capped
        // three-character prefix search, which meant any signed-in account could
        // walk the alphabet and harvest every user's email address and full name —
        // the largest disclosure between accounts in the whole system, in exchange
        // for a convenience the dashboard never used (it always passed a full
        // address). Exact match reveals nothing the caller did not already know:
        // they had to type the address to ask.
        return await _context.Users
            .AsNoTracking()
            .Where(user => user.Email == needle)
            .Select(user => new UserProfileDto(user.Id, user.Email, user.FirstName, user.LastName))
            .ToListAsync(cancellationToken);
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
        bool sharesADevice = await _context.Accesses
            .AsNoTracking()
            .Where(theirs => theirs.UserId == userId && theirs.IsActive)
            .AnyAsync(
                theirs => _context.Accesses.Any(mine =>
                    mine.UserId == callerId
                    && mine.IsActive
                    && mine.DeviceId == theirs.DeviceId),
                cancellationToken);

        if (!sharesADevice)
        {
            // 404, not 403: a 403 would confirm the id belongs to a real account,
            // which is the same enumeration hint the device endpoints refuse to give.
            return OperationResult<UserProfileDto>.NotFound("No such user.");
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
            return OperationResult<UserProfileDto>.NotFound("No such user.");
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
    public async Task<OperationResult<bool>> ChangePasswordAsync(
        int userId,
        ChangePasswordRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        User? user = await _context.Users
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        if (user is null)
        {
            return OperationResult<bool>.NotFound("No such user.");
        }

        PasswordCheckResult check = _passwordHasher.Check(user.PasswordHash, request.CurrentPassword);

        if (check == PasswordCheckResult.Failed)
        {
            // Proof-of-identity failed. Without this gate a stolen session cookie
            // would be upgradable into permanent account takeover.
            _logger.LogInformation("Password change refused for user {UserId}: current password did not match", userId);
            return OperationResult<bool>.Invalid("Your current password is not correct.");
        }

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Password changed for user {UserId}", userId);

        return OperationResult<bool>.Success(true);
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

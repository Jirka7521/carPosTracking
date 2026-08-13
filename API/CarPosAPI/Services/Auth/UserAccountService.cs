using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Common;
using Microsoft.EntityFrameworkCore;
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
    /// <summary>
    /// Shortest prefix accepted by a non-exact email search, and the cap on how
    /// many matches come back. Together they stop the sharing picker from being
    /// used as a "list everyone" endpoint: one or two letters would match most of
    /// the table.
    /// </summary>
    private const int MinimumPrefixSearchLength = 3;

    /// <summary>Upper bound on rows returned by a prefix search.</summary>
    private const int MaximumSearchResults = 20;

    /// <summary>The single message every failed sign-in gets, whatever went wrong.</summary>
    private const string InvalidCredentialsMessage = "Incorrect email or password.";

    private readonly CarPosDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<UserAccountService> _logger;

    /// <summary>Creates the service.</summary>
    /// <param name="context">Scoped database context.</param>
    /// <param name="passwordHasher">Hashes and verifies passwords.</param>
    /// <param name="logger">Structured logger — never receives passwords or hashes.</param>
    public UserAccountService(
        CarPosDbContext context,
        IPasswordHasher passwordHasher,
        ILogger<UserAccountService> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult<User>> RegisterAsync(
        RegisterRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string email = NormaliseEmail(request.Email);

        User user = new User
        {
            Email = email,
            PasswordHash = _passwordHasher.Hash(request.Password),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
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
        bool exactMatch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        string needle = NormaliseEmail(email);

        if (needle.Length == 0)
        {
            return [];
        }

        if (exactMatch)
        {
            // The overwhelmingly common case: the user typed a colleague's full
            // address into the share box. Hits the unique index directly.
            return await _context.Users
                .AsNoTracking()
                .Where(user => user.Email == needle)
                .Select(user => new UserProfileDto(user.Id, user.Email, user.FirstName, user.LastName))
                .ToListAsync(cancellationToken);
        }

        if (needle.Length < MinimumPrefixSearchLength)
        {
            // Refusing to answer is deliberate: "a" would match most of the table
            // and turn this into a directory dump.
            return [];
        }

        // StartsWith rather than Contains so the query can still use the index, and
        // so a search for "@company.cz" cannot list an entire organisation.
        return await _context.Users
            .AsNoTracking()
            .Where(user => user.Email.StartsWith(needle))
            .OrderBy(user => user.Email)
            .Take(MaximumSearchResults)
            .Select(user => new UserProfileDto(user.Id, user.Email, user.FirstName, user.LastName))
            .ToListAsync(cancellationToken);
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

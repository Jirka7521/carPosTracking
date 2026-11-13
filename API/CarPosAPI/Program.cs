// Program.cs — composition root only: configuration binding, DI registrations,
// the CLI branches (schema-sync, import-device-key, rotate-master-key,
// create-registration-invite) and the HTTP pipeline. All
// behaviour lives in the layer folders (Options/, Data/, Services/), per project
// guidelines.

using System.Diagnostics;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using CarPosAPI.Data;
using CarPosAPI.Data.SchemaSync;
using CarPosAPI.Middleware;
using CarPosAPI.Options;
using CarPosAPI.Services.Auth;
using CarPosAPI.Services.Authorization;
using CarPosAPI.Services.Devices;
using CarPosAPI.Services.Health;
using CarPosAPI.Services.Ingest;
using CarPosAPI.Services.Positions;
using CarPosAPI.Services.Privacy;
using CarPosAPI.Services.Provisioning;
using CarPosAPI.Services.Scheduling;
using CarPosAPI.Services.Security;
using CarPosAPI.Services.Sharing;
using CarPosAPI.Services.Trips;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

// CLI mode: compare a database against the schema this source describes, and
// optionally bring it into line. Runs BEFORE the builder — unlike
// import-device-key below — for two reasons: it must not need the JWT key, master
// key or broker credentials, whose ValidateOnStart would refuse to boot for a task
// that touches none of them; and it takes its database from --connection so it can
// be pointed anywhere, which a DI-supplied context could not be (appsettings.Local
// .json is added last and would override any connection passed in).
if (SchemaSyncCommand.IsRequested(args))
{
    return await SchemaSyncCommand.RunAsync(args);
}

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Secrets as files. The deployment mounts each secret read-only under /run/secrets
// with the configuration key as its file name (ConnectionStrings__CarPos,
// Mqtt__Password, Jwt__SigningKey, DeviceKeyProtection__MasterKeyBase64); the
// key-per-file provider turns "__" into the section separator, so each lands on
// its key with no mapping code. Files rather than environment variables because
// Docker keeps a container's environment in plain text in its config on the
// host's disk and prints it in `docker inspect`; a mounted file is neither.
//
// Added after the environment, so a file wins over a variable of the same name —
// and optional, so a deployment that still passes environment variables (or
// development, where the directory does not exist) works exactly as before.
const string ContainerSecretsPath = "/run/secrets";
builder.Configuration.AddKeyPerFile(ContainerSecretsPath, optional: true, reloadOnChange: false);

// Local developer overrides. appsettings.json is committed and secret-free (it
// doubles as the example: every key is listed, secrets left empty); the real
// values live here. The file is git-ignored and optional, so a fresh clone still
// builds — it just fails fast at startup until the secrets are filled in.
// Added after CreateBuilder, it is the last source in the chain and therefore
// wins over user-secrets and environment variables. That is intentional for
// development; production has no such file and keeps using environment variables.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// ---------------------------------------------------------------------------
// Options — bound to typed classes and validated at startup so a missing secret
// or an insecure broker URI kills the process immediately, not on first message.
// ---------------------------------------------------------------------------
builder.Services.AddOptions<MqttOptions>()
    .BindConfiguration(MqttOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(
        static (MqttOptions options) => options.HasSupportedBrokerUri(),
        "Mqtt:BrokerUri must be an absolute ws://, wss://, mqtt:// or mqtts:// URI.")
    .ValidateOnStart();

builder.Services.AddOptions<IngestOptions>()
    .BindConfiguration(IngestOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<DeviceKeyProtectionOptions>()
    .BindConfiguration(DeviceKeyProtectionOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(
        static (DeviceKeyProtectionOptions options) => options.HasValidMasterKey(),
        $"DeviceKeyProtection:MasterKeyBase64 must be base64 of exactly {DeviceKeyProtectionOptions.MasterKeyBytes} random bytes.")
    .ValidateOnStart();

builder.Services.AddOptions<JwtOptions>()
    .BindConfiguration(JwtOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(
        static (JwtOptions options) => options.HasStrongSigningKey(),
        $"Jwt:SigningKey must be at least {JwtOptions.MinimumSigningKeyBytes} bytes. There is no default and no fallback — a deployment without a real key would issue forgeable sessions.")
    .Validate(
        static (JwtOptions options) => options.HasDistinctShareIdentity(),
        "Jwt:ShareIssuer and Jwt:ShareAudience must both differ from Jwt:Issuer and Jwt:Audience. Share tokens are signed with the same key as sessions, so those two values are the entire separation between an anonymous visitor and an account holder.")
    .ValidateOnStart();

builder.Services.AddOptions<AuthCookieOptions>()
    .BindConfiguration(AuthCookieOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<HostingOptions>()
    .BindConfiguration(HostingOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(
        static (HostingOptions options) => options.HasValidPathBase(),
        "Hosting:PathBase must be empty or an absolute path with no trailing slash, e.g. \"/carPosAPI\".")
    .ValidateOnStart();

// Which peers may say who the real client is. Validated because a typo must fail
// the deployment: silently trusting nobody puts every internet visitor in the
// tunnel's one rate-limit bucket, and the opposite lets the VLAN forge addresses.
builder.Services.AddOptions<ReverseProxyOptions>()
    .BindConfiguration(ReverseProxyOptions.SectionName)
    .Validate(
        static (ReverseProxyOptions options) => options.HasValidAddresses(),
        "ReverseProxy:TrustedProxies and ReverseProxy:CloudflareTunnelProxies must be comma-separated IP addresses (no CIDR ranges, no host names).")
    .ValidateOnStart();

// Per-account ceiling on registered devices. Policy, not a secret, like the
// sharing limits below.
builder.Services.AddOptions<DeviceRegistrationOptions>()
    .BindConfiguration(DeviceRegistrationOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Who may sign up: invite-only by default. Policy, not a secret, so a deployment can
// open or close registration without a rebuild.
builder.Services.AddOptions<RegistrationOptions>()
    .BindConfiguration(RegistrationOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(
        static (RegistrationOptions options) => Enum.IsDefined(options.Mode),
        "Registration:Mode must be InviteOnly, Open or Closed.")
    .ValidateOnStart();

// Ceilings on temporary share links. No secret here — these are policy limits, and
// they live in configuration so a deployment can tighten them without a rebuild.
builder.Services.AddOptions<SharingOptions>()
    .BindConfiguration(SharingOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Who is answerable for the personal data this system holds. The contact check runs
// only outside Development, for the same reason the JWT key check exists at all: a
// developer running this on a laptop has no data subjects to answer to, but anything
// reachable from the internet does, and a privacy policy naming nobody is worse than
// no policy — it looks like an answer.
builder.Services.AddOptions<PrivacyOptions>()
    .BindConfiguration(PrivacyOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(
        (PrivacyOptions options) => builder.Environment.IsDevelopment() || options.HasController(),
        $"Privacy:ControllerContactEmail must be a real address before this is deployed — it is where data-subject requests go, and it is published in the privacy policy. It is still set to \"{PrivacyOptions.UnsetContactPlaceholder}\".")
    .ValidateOnStart();

// ---------------------------------------------------------------------------
// Request size limit. Kestrel's own default is about 30 MB and nothing this API
// accepts comes near it: the largest legitimate body is a device registration
// carrying up to 32 share grants, roughly 11 KB, and the next is a 4 KB public
// key. Bulk data does not arrive over HTTP at all — position fixes come in over
// MQTT, capped separately by IngestOptions.
//
// Applied here rather than left to Kestrel's configuration binding, which reads
// Endpoints and Certificates from the Kestrel section but not Limits: setting it
// in appsettings.json alone looks right, changes nothing, and gives no error.
// The key is still read so a deployment can override it without a rebuild.
// ---------------------------------------------------------------------------
const long DefaultMaxRequestBodyBytes = 256L * 1024L;

builder.WebHost.ConfigureKestrel((KestrelServerOptions kestrel) =>
    kestrel.Limits.MaxRequestBodySize = builder.Configuration
        .GetValue<long?>("Kestrel:Limits:MaxRequestBodySize")
        ?? DefaultMaxRequestBodyBytes);

// ---------------------------------------------------------------------------
// Database. The connection string is a secret (user-secrets in dev, environment
// variable in prod) and must exist — refuse to start without it. Runtime uses
// the least-privilege BE role; migrations are applied manually as admin.
// AddDbContextFactory lets the singleton ingest services create short-lived
// contexts, and also registers the plain scoped DbContext for future controllers.
// ---------------------------------------------------------------------------
string? connectionString = builder.Configuration.GetConnectionString("CarPos");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'CarPos' is missing. Set it in appsettings.Local.json (or with 'dotnet user-secrets set \"ConnectionStrings:CarPos\" \"...\"') in development, or in production as the file /run/secrets/ConnectionStrings__CarPos (or the ConnectionStrings__CarPos environment variable).");
}

// Transient faults are retried in-process rather than surfacing as a 500 on the
// first blip: a failover, a reset connection or a pool timeout is a few seconds of
// bad luck, not a failed request. Three attempts inside five seconds stays well
// under any sane client timeout, and a fault that outlives that is no longer
// transient — it should be reported, not retried forever.
//
// The price is that EF Core then refuses a user-initiated transaction unless it
// runs inside the execution strategy, because it cannot know how to replay one it
// did not open. Every place that begins a transaction wraps itself in
// CreateExecutionStrategy().ExecuteAsync accordingly: DeviceService.CreateAsync,
// DeviceConfigRevisionWriter, AccountErasureService, PositionErasureService,
// DeviceStatusWriter and TripDetectionPass.
const int DatabaseRetryAttempts = 3;
const int DatabaseRetryMaxDelaySeconds = 5;

// The Npgsql default is the same thirty seconds; set explicitly so it is a
// decision rather than an inherited one. A request still waiting on a single
// statement after half a minute has already failed as far as the caller is
// concerned.
const int DatabaseCommandTimeoutSeconds = 30;

builder.Services.AddDbContextFactory<CarPosDbContext>(
    (DbContextOptionsBuilder options) => options.UseNpgsql(
        connectionString,
        (NpgsqlDbContextOptionsBuilder npgsql) =>
        {
            npgsql.EnableRetryOnFailure(
                maxRetryCount: DatabaseRetryAttempts,
                maxRetryDelay: TimeSpan.FromSeconds(DatabaseRetryMaxDelaySeconds),
                errorCodesToAdd: null);

            npgsql.CommandTimeout(DatabaseCommandTimeoutSeconds);
        }));

// ---------------------------------------------------------------------------
// Ingest services. Everything is a singleton: the pipeline is driven by one
// sequential MQTT consumer and reaches the scoped world via the context factory.
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<IMasterKeyProtector, MasterKeyProtector>();
builder.Services.AddSingleton<EnvelopeCodec>();
builder.Services.AddSingleton<PositionValidator>();
builder.Services.AddSingleton<IPayloadCryptoService, PayloadCryptoService>();
builder.Services.AddSingleton<IDeviceRegistry, DeviceRegistry>();
// The wake-up call from a position save to the trip worker. PositionWriter must stay the
// only writer of positions: the trip worker finds new rows by an id cursor that is only
// sound while inserts come from one process, one batch at a time.
builder.Services.AddSingleton<ITripDetectionSignal, TripDetectionSignal>();
builder.Services.AddSingleton<IPositionWriter, PositionWriter>();
builder.Services.AddSingleton<IAckSealer, AckSealer>();
// Singleton so the hosted service can hand it the live MQTT client while the
// pipeline (also a singleton) publishes through it.
builder.Services.AddSingleton<IAckPublisher, MqttAckPublisher>();
// Singleton for the same reason, and shared with the scoped device-config service:
// settings are published on the one broker connection this application owns.
builder.Services.AddSingleton<IConfigPublisher, MqttConfigPublisher>();
builder.Services.AddSingleton<IIngestPipeline, IngestPipeline>();
// The status topic's pipeline: same singletons upstream (registry, codec, crypto), its
// own validator and writer downstream. Fed by the same hosted service, routed by topic.
builder.Services.AddSingleton<DeviceStatusValidator>();
builder.Services.AddSingleton<IDeviceStatusWriter, DeviceStatusWriter>();
builder.Services.AddSingleton<IStatusIngestPipeline, StatusIngestPipeline>();
builder.Services.AddSingleton<MqttConnectionState>();
builder.Services.AddHostedService<MqttIngestService>();

// ---------------------------------------------------------------------------
// Provisioning. Generates a device's key pair and renders the firmware config
// block; scoped because it opens a DbContext per request. The snippet builder is
// stateless, so it is shared.
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<ConfigSnippetBuilder>();
builder.Services.AddScoped<IDeviceProvisioningService, DeviceProvisioningService>();

// ---------------------------------------------------------------------------
// Authentication. The token is validated by the framework; it is *issued* by
// JwtTokenIssuer and delivered in an HttpOnly cookie by SessionCookieWriter, so
// no script — including one injected by an XSS bug — can ever read it.
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<IJwtTokenIssuer, JwtTokenIssuer>();
// Revocable sessions: every validated session token is checked against the user's
// security stamp and the sign-out deny-list (OnTokenValidated below), through a
// short process-wide cache. Singletons, reaching the database via the factory.
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ISessionStateSource, DbSessionStateSource>();
builder.Services.AddSingleton<ISessionRegistry, SessionRegistry>();
builder.Services.AddSingleton<ISessionCookieWriter, SessionCookieWriter>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
builder.Services.AddScoped<IUserAccountService, UserAccountService>();

// The share-link credential, kept parallel to the session one at every level: its
// own issuer, its own cookie writer, its own accessor. The parallel is the point —
// nothing here is a variant of the session machinery that could be widened into it
// by an edit that looked harmless.
builder.Services.AddSingleton<IShareTokenIssuer, ShareTokenIssuer>();
builder.Services.AddSingleton<IShareCookieWriter, ShareCookieWriter>();
builder.Services.AddScoped<IShareContextAccessor, ShareContextAccessor>();

// ---------------------------------------------------------------------------
// Authorisation and the resource services. Every one of these is scoped: they
// hold the request's DbContext, and the authorizer re-reads the caller's grant
// from the database on every call so a revoked share stops working immediately
// rather than when some token expires.
// ---------------------------------------------------------------------------
builder.Services.AddScoped<IDeviceAccessAuthorizer, DeviceAccessAuthorizer>();
builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddScoped<IDeviceConfigService, DeviceConfigService>();
builder.Services.AddScoped<IPositionQueryService, PositionQueryService>();
builder.Services.AddScoped<IDeviceEventQueryService, DeviceEventQueryService>();
builder.Services.AddScoped<IAccessService, AccessService>();
// The only way a grant comes into being: an invitation its recipient accepts.
builder.Services.AddScoped<IAccessInvitationService, AccessInvitationService>();

// Temporary share links. The two stateless helpers are shared; the three services
// are scoped like every other database-touching one.
//
// ShareViewService is registered beside PositionQueryService and is emphatically
// not a variant of it: it takes a share id where the other takes a user id, and
// keeping them as separate types is what makes it impossible for one to be handed
// the other's notion of who is asking.
builder.Services.AddSingleton<IShareTokenFactory, ShareTokenFactory>();
builder.Services.AddSingleton<IPassphraseGenerator, PassphraseGenerator>();
builder.Services.AddScoped<IShareLinkService, ShareLinkService>();
builder.Services.AddScoped<IShareRedemptionService, ShareRedemptionService>();
builder.Services.AddScoped<IShareViewService, ShareViewService>();

// The GDPR data-subject services. Scoped like everything else that writes through
// the request's DbContext. The erasure service is the one place in the application
// that physically deletes rows, which is why it lives behind its own interface
// rather than as another method on the device or account services — it should be
// obvious in the DI graph that this capability exists and where it is used.
builder.Services.AddScoped<IPositionErasureService, PositionErasureService>();
builder.Services.AddScoped<IDataExportService, DataExportService>();
builder.Services.AddScoped<IAccountErasureService, AccountErasureService>();
builder.Services.AddScoped<IMapsConsentService, MapsConsentService>();

// ---------------------------------------------------------------------------
// Settings schedules. The evaluator is pure arithmetic over a set of rules — no
// database, no clock of its own — so it is shared. Everything around it is scoped
// because it writes through the request's DbContext, including the revision
// writer, which both the manual settings save and the scheduler go through so
// there is exactly one code path that appends a revision and publishes it.
//
// The worker is the only piece that has no request to belong to: it opens a scope
// per pass rather than capturing one, which is what keeps a scoped DbContext from
// living for the lifetime of the process.
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<ScheduleEvaluator>();
// Stateless like the evaluator, and shared for the same reason. It takes the DbContext
// per call rather than by injection, which is what lets both the scoped bundle
// publisher and the singleton MqttConfigPublisher use the one instance.
builder.Services.AddSingleton<ScheduleBundleBuilder>();
builder.Services.AddScoped<IScheduleBundlePublisher, ScheduleBundlePublisher>();
builder.Services.AddScoped<IDeviceConfigRevisionWriter, DeviceConfigRevisionWriter>();
builder.Services.AddScoped<IDeviceScheduleResolver, DeviceScheduleResolver>();
builder.Services.AddScoped<IDeviceConfigScheduleService, DeviceConfigScheduleService>();
builder.Services.AddScoped<IScheduleReconciler, ScheduleReconciler>();
// The map's overdue pin: how long a device may stay quiet under the plan in force.
// The arithmetic is pure and shared like the evaluator; the loader is scoped.
builder.Services.AddSingleton<ReportIntervalCalculator>();
builder.Services.AddScoped<IDeviceReportIntervalService, DeviceReportIntervalService>();
// Shared between the worker and its health check, exactly as MqttConnectionState is
// above: the worker swallows its failures by design, so this is the only way one
// becomes visible from outside the log.
builder.Services.AddSingleton<ScheduleWorkerState>();
builder.Services.AddHostedService<DeviceConfigScheduleWorker>();

// ---------------------------------------------------------------------------
// Trips. The worker runs when PositionWriter signals a save (not on a timer) and opens
// a scope per pass, like the schedule worker; its state is shared with /health.
// ---------------------------------------------------------------------------
builder.Services.AddScoped<ITripFixReader, TripFixReader>();
builder.Services.AddScoped<ITripDetectionPass, TripDetectionPass>();
builder.Services.AddScoped<ITripStatsCalculator, TripStatsCalculator>();
builder.Services.AddScoped<ITripReadService, TripReadService>();
builder.Services.AddScoped<ITripAssignmentService, TripAssignmentService>();
builder.Services.AddScoped<IJourneyService, JourneyService>();
builder.Services.AddScoped<ITripSettingsService, TripSettingsService>();
builder.Services.AddSingleton<TripWorkerState>();
builder.Services.AddHostedService<TripDetectionWorker>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer((JwtBearerOptions options) =>
    {
        // Reading the raw "sub" claim rather than letting the handler rename it to
        // the long ClaimTypes.NameIdentifier URI. CurrentUserAccessor looks for
        // "sub"; if the mapping were left on, it would find nothing and every
        // request would be "authenticated but nobody".
        options.MapInboundClaims = false;

        JwtOptions jwtOptions = builder.Configuration
            .GetSection(JwtOptions.SectionName)
            .Get<JwtOptions>() ?? new JwtOptions();

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            // The default five minutes of slack means an "expired" session keeps
            // working for another five. Zero is the honest value.
            ClockSkew = TimeSpan.Zero,
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = (MessageReceivedContext context) =>
            {
                // The token lives in a cookie, not an Authorization header — this is
                // the whole point of the cookie scheme, and the one line that makes
                // the standard JWT handler read it from there.
                AuthCookieOptions cookieOptions = context.HttpContext.RequestServices
                    .GetRequiredService<IOptions<AuthCookieOptions>>().Value;

                context.Token = context.Request.Cookies[cookieOptions.SessionCookieName];
                return Task.CompletedTask;
            },
            // A valid signature is not enough any more: the token must still be a
            // live session — not signed out, not issued before the last password
            // change, not for an account that has been erased. Failing here makes
            // the request anonymous, so [Authorize] answers 401 as for no cookie.
            OnTokenValidated = async (TokenValidatedContext context) =>
            {
                ISessionRegistry sessions = context.HttpContext.RequestServices
                    .GetRequiredService<ISessionRegistry>();

                if (context.Principal is null
                    || !await sessions.IsCurrentAsync(context.Principal, context.HttpContext.RequestAborted))
                {
                    context.Fail("The session has ended.");
                }
            },
        };
    })
    // ---------------------------------------------------------------------------
    // The share scheme, registered *alongside* the default one rather than instead
    // of it. That arrangement is the wall between an anonymous share visitor and an
    // account holder, and it holds in both directions without anyone remembering to
    // check anything:
    //
    //   * a plain [Authorize] binds to the default scheme, so every existing
    //     endpoint rejects share tokens — including endpoints written later;
    //   * this scheme validates a different issuer and audience, so a session token
    //     presented here fails validation outright.
    //
    // The two are signed with the same key, which is what makes the issuer and
    // audience load-bearing rather than decorative — see JwtOptions.ShareIssuer, and
    // the startup check that refuses to let them be configured alike.
    // ---------------------------------------------------------------------------
    .AddJwtBearer(ShareAuthenticationDefaults.Scheme, (JwtBearerOptions options) =>
    {
        // Same reasoning as above: keep the raw claim names, since ShareContextAccessor
        // looks for "share" and the inbound mapping would rename what it finds.
        options.MapInboundClaims = false;

        JwtOptions jwtOptions = builder.Configuration
            .GetSection(JwtOptions.SectionName)
            .Get<JwtOptions>() ?? new JwtOptions();

        // Built in ShareTokenValidation so the tests can hold the real parameters to
        // account rather than a restatement of them.
        options.TokenValidationParameters = ShareTokenValidation.CreateParameters(jwtOptions);

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = (MessageReceivedContext context) =>
            {
                AuthCookieOptions cookieOptions = context.HttpContext.RequestServices
                    .GetRequiredService<IOptions<AuthCookieOptions>>().Value;

                // Its own cookie, so a browser holding both credentials at once — an
                // account holder checking a link they created — presents each only
                // where it is asked for.
                context.Token = context.Request.Cookies[cookieOptions.ShareCookieName];
                return Task.CompletedTask;
            },
        };
    });

// Deny by default. An endpoint with no [Authorize] or [AllowAnonymous] of its own
// now requires a signed-in user instead of being quietly public, so forgetting the
// attribute on a new controller fails closed. The anonymous surface is spelled out
// where it lives: AuthController, PrivacyController, the share redeem/leave
// actions and /health. The share view names its own scheme in its [Authorize], so
// the fallback (which uses the default, session scheme) never applies to it.
builder.Services.AddAuthorization((AuthorizationOptions options) =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

// ---------------------------------------------------------------------------
// Rate limiting on the unauthenticated front door. Everything else needs a valid
// session first, which is its own limit; sign-in is where an attacker gets free
// guesses. Partitioned by client address so one attacker cannot lock out the
// whole world.
// ---------------------------------------------------------------------------
// Rejections are logged under their own category rather than Program's, so a
// deployment can turn the noise up or down without touching anything else.
const string RateLimitLoggerCategory = "CarPosAPI.RateLimiting";

builder.Services.AddRateLimiter((RateLimiterOptions options) =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // A rejection is a security event. A burst of them is what credential
    // stuffing, a hunt for valid share links, or a runaway client looks like from
    // in here, and until now they left no trace in the log at all.
    //
    // Logged with the policy and the path but never the partition key: that key is
    // a client address or an account id, and neither belongs in a log line that
    // exists to count events. The body is left empty on purpose — UseStatusCodePages
    // fills it with the same ProblemDetails shape every other error uses.
    options.OnRejected = (OnRejectedContext context, CancellationToken cancellationToken) =>
    {
        ILogger logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(RateLimitLoggerCategory);

        logger.LogWarning(
            "Rate limit rejected {Method} {Path}",
            context.HttpContext.Request.Method,
            context.HttpContext.Request.Path);

        // Tells a well-behaved client when it is worth coming back, instead of
        // leaving it to hammer a door that is going to stay shut for the rest of
        // the window. Only set when the limiter actually knows the answer.
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds)
                .ToString(CultureInfo.InvariantCulture);
        }

        return ValueTask.CompletedTask;
    };

    options.AddPolicy(RateLimitPolicies.Authentication, (HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(
            // Behind the reverse proxy this is the real client address only because
            // ForwardedClientMiddleware runs first — see the pipeline below.
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: static _ => new FixedWindowRateLimiterOptions
            {
                // Twenty attempts a minute is far above what a human typing a
                // password needs, and far below what makes guessing worthwhile.
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    // The GDPR endpoints on /api/me. Authenticated, so partitioned by account id
    // rather than by address — the account is what is being abused, and users behind
    // one address must not be able to exhaust each other's budget.
    //
    // This reads HttpContext.User, which only exists because UseRateLimiter runs
    // AFTER UseAuthentication in the pipeline below. Move it back above and this
    // silently degrades to the address fallback with no error anywhere: every user
    // behind one NAT would share a single bucket again.
    options.AddPolicy(RateLimitPolicies.PrivacyOperations, (HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: AccountPartitionKey(context),
            factory: static _ => new FixedWindowRateLimiterOptions
            {
                // An export is a full history dump and a deletion is final: nobody
                // legitimately needs either more than a handful of times running.
                // Loose enough that a retry after a failed download still works.
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
            }));

    // Redeeming a share link. The second unauthenticated front door, and the one
    // with no account behind it to lock or notify.
    //
    // This is the outer of two limits. The per-link cooldown in ShareCooldownPolicy
    // makes guessing the code of one share progressively hopeless; this caps how
    // fast one address can work through many of them, which is the shape a hunt for
    // valid links would take. Partitioned by address because a visitor has no
    // identity here to partition by.
    options.AddPolicy(RateLimitPolicies.ShareRedemption, (HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: static _ => new FixedWindowRateLimiterOptions
            {
                // Generous for a person typing one code and refreshing a map, mean for
                // anything working through a list. The map itself polls this
                // controller's read action on a thirty-second timer, so the ceiling
                // has to leave room for that as well as for the redeem.
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    // Device invitations: create, preview, accept, decline. Per account for the same
    // reason as the GDPR policy above. Thirty in five minutes is a busy afternoon of
    // sharing several devices, and a crawl for anything looping over tokens — which
    // the 256-bit verifier makes pointless anyway.
    options.AddPolicy(RateLimitPolicies.Invitations, (HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: AccountPartitionKey(context),
            factory: static _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
            }));

    // Registering a device: an RSA-3072 key generation per call. Ten an hour is more
    // than anyone provisioning real hardware needs, including the retries after a
    // taken device id, and caps the CPU one account can burn at a few seconds a
    // minute.
    options.AddPolicy(RateLimitPolicies.DeviceRegistration, (HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: AccountPartitionKey(context),
            factory: static _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0,
            }));
});

// ---------------------------------------------------------------------------
// Error handling: one handler, ProblemDetails out, nothing internal leaked.
//
// CustomizeProblemDetails runs for every problem response this API produces — the
// 500 from GlobalExceptionHandler, the 4xx from ApiControllerBase.Failure, the
// automatic 400 from [ApiController] validation, and the bodies UseStatusCodePages
// fills in for 401/403/404/405/429 — so every error carries the same correlation
// id under the same name.
//
// The id is safe to publish. It identifies one request and nothing else: not the
// server, not the build, not the caller. It is also what the logging provider
// stamps on the matching log line, which is the whole point — it turns "the site
// broke this morning" into a single grep.
//
// The same hook gives every response an error `code` the frontend translates,
// derived from the status wherever the producer did not set a specific one — see
// ProblemCodeDefaults.
// ---------------------------------------------------------------------------
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails((ProblemDetailsOptions options) =>
    options.CustomizeProblemDetails = (ProblemDetailsContext context) =>
    {
        context.ProblemDetails.Extensions["traceId"] =
            Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;

        ProblemCodeDefaults.Apply(context.ProblemDetails);
    });

// A BackgroundService that throws stops the whole host by default, which would
// mean an MQTT fault taking the REST API down with it — the opposite of what the
// health model here says should happen, where a broker outage is Degraded and
// self-healing while only the database can be Unhealthy.
//
// Both workers already supervise themselves (see MqttIngestService.ExecuteAsync
// and DeviceConfigScheduleWorker.RunPassAsync), so this is the second line of
// defence rather than the first. If one ever does escape its own guards, HTTP
// keeps serving and /health is what reports the loss: MqttIngestHealthCheck goes
// Degraded the moment the connection is gone, however it went.
builder.Services.Configure<HostOptions>((HostOptions options) =>
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

// ---------------------------------------------------------------------------
// Health. One check per dependency, each reporting separately in the JSON body
// written by HealthReportWriter. Only the database can answer Unhealthy (503):
// everything else here is either self-healing or unfixable by a restart, and a
// container that restarts on a broker blip is worse than one that reports it.
// The migration check is a singleton because it memoises its answer.
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<MigrationHealthCheck>();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database")
    .AddCheck<MqttIngestHealthCheck>("mqtt")
    .AddCheck<MigrationHealthCheck>("migrations")
    .AddCheck<ScheduleWorkerHealthCheck>("scheduler")
    .AddCheck<TripWorkerHealthCheck>("trips")
    .AddCheck<ProcessHealthCheck>("process");

builder.Services.AddControllers();
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

// CLI mode: provision a device key and exit — the host (Kestrel, MQTT) never starts.
if (DeviceKeyImportCommand.IsRequested(args))
{
    return await DeviceKeyImportCommand.RunAsync(app.Services, args);
}

// CLI mode: re-encrypt every stored device key under a new master key and exit. Same
// placement and reasoning as import-device-key: it needs the configured (current)
// master key and the database exactly as the web path sees them, and nothing else.
if (MasterKeyRotationCommand.IsRequested(args))
{
    return await MasterKeyRotationCommand.RunAsync(app.Services, args);
}

// CLI mode: mint one single-use registration invite code, print it and exit. A
// command rather than an endpoint, because there is no administrator role to guard one.
if (RegistrationInviteCommand.IsRequested(args))
{
    return await RegistrationInviteCommand.RunAsync(app.Services, args);
}

// Turns any unhandled exception into a ProblemDetails 500 (see GlobalExceptionHandler).
//
// First in the pipeline, above even UsePathBase and the forwarded-client resolver.
// Those can throw — a malformed request is the realistic way — and anything
// thrown above this line escapes to Kestrel instead, which answers a bare 500 and
// logs it somewhere this application never sees. The whole point of the handler is
// that no failure is invisible, so it has to be the outermost thing there is.
app.UseExceptionHandler();

// Gives a body to the error statuses that are produced without one: 401 from the
// JWT handler, 403 from [Authorize], 404 for an unmatched route, 405, and 429 from
// the rate limiter. AddProblemDetails above makes that body a ProblemDetails, so
// the frontend has exactly one error shape to parse however a request failed.
//
// The text it generates is generic, and that is deliberate rather than lazy: a 404
// from an unmatched route and a 404 from "that device exists but is not yours"
// must stay indistinguishable, which is the same enumeration defence the service
// layer implements by answering 404 where 403 would be the honest answer.
//
// It only fills a response that has no body of its own, so /health (which writes
// its own JSON report) and every Problem() response are left exactly as they are.
app.UseStatusCodePages();

// Strips the prefix the API is published under, before anything downstream looks
// at the path. The Cloudflare tunnel routes /carPosAPI/* here and forwards the
// prefix as part of the path, so without this every request would match no route
// and 404. UsePathBase only strips the prefix when a request actually carries
// it, so /health and /carPosAPI/health both work — which is the point: the
// tunnel and the compose network address the same API differently.
//
// Generated URLs (a Created response's Location header) get the prefix back
// automatically, because ASP.NET Core keeps it in HttpRequest.PathBase.
string configuredPathBase = app.Services
    .GetRequiredService<IOptions<HostingOptions>>().Value.PathBase;

if (configuredPathBase.Length > 0)
{
    app.UsePathBase(configuredPathBase);
}

// Runs next so everything downstream — rate-limiting partitions, the HSTS decision,
// generated URLs — sees the browser's real address and scheme rather than the
// reverse proxy's. Forwarding headers are believed only from the proxies listed in
// ReverseProxyOptions (the frontend's nginx and the Cloudflare tunnel connector);
// this container sits on a macvlan VLAN where any host can reach the port, so a
// header from anyone else is ignored. See ForwardedClientResolver for the rules.
app.UseMiddleware<ForwardedClientMiddleware>();

// Baseline response headers on everything the API answers, error pages included.
// After the resolver, because HSTS depends on the scheme it settles.
app.UseMiddleware<SecurityHeadersMiddleware>();

if (app.Environment.IsDevelopment())
{
    // The OpenAPI document describes every endpoint including request shapes, so
    // it stays in Development. It is also not proxied publicly by the frontend's
    // nginx — belt and braces. Anonymous explicitly, or the fallback policy would
    // hide it from the developer it exists for.
    app.MapOpenApi().AllowAnonymous();
}
else
{
    // TLS is terminated at the proxy, so inside the container the API speaks plain
    // HTTP on purpose; redirecting there would bounce a request that is already
    // secure. In any non-container deployment this is still wanted, hence the
    // environment split rather than deleting it.
    app.UseHttpsRedirection();
}

// Order matters: authentication first (so the session cookie is turned into a
// principal), then rate limiting, then the CSRF check, then authorisation. Putting
// CSRF ahead of authentication would be just as safe but harder to read in the
// logs, since the rejection would carry no user.
//
// The limiter sits below authentication because the PrivacyOperations policy
// partitions by the caller's account id. Read any earlier and HttpContext.User is
// still the empty default principal, so that policy falls through to its address
// fallback and every user behind one NAT shares a single export budget — the exact
// failure its comment says must not happen, and one that shows up nowhere but in a
// complaint. The two anonymous policies partition by address and do not care.
//
// Endpoint metadata is resolved by routing before any of this, so [EnableRateLimiting]
// still selects the right policy from down here.
app.UseAuthentication();
app.UseRateLimiter();
app.UseMiddleware<CsrfProtectionMiddleware>();
app.UseAuthorization();

app.MapControllers();

// Health endpoint (unauthenticated by design, hence AllowAnonymous against the
// fallback policy). The tunnel publishes it as /carPosAPI/health, so what a stranger
// gets is the overall status and nothing else; the full report, one entry per
// dependency with versions, counters and pending migrations, goes only to a caller
// inside the container itself — the compose healthcheck and `docker exec`. See
// HealthDetailPolicy. Even the full report copies only what this codebase wrote,
// never an exception message.
//
// The default ResultStatusCodes are left alone on purpose: Healthy and Degraded both
// answer 200 and only Unhealthy answers 503, which is the contract the container
// healthcheck reads — and which the summary carries just as well as the report.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = HealthReportWriter.WriteAsync,
    AllowCachingResponses = false,
})
    .AllowAnonymous();

await app.RunAsync();
return 0;

// The partition for the per-account rate limits: the session's user id, or — on the
// off chance one of those endpoints is reached without a session — the address the
// request was attributed to.
static string AccountPartitionKey(HttpContext context)
{
    return context.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";
}

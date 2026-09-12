// Program.cs — composition root only: configuration binding, DI registrations,
// the two CLI branches (schema-sync, import-device-key) and the HTTP pipeline. All
// behaviour lives in the layer folders (Options/, Data/, Services/), per project
// guidelines.

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
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

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
        "Connection string 'CarPos' is missing. Set it in appsettings.Local.json (or with 'dotnet user-secrets set \"ConnectionStrings:CarPos\" \"...\"') in development, or as the ConnectionStrings__CarPos environment variable in production.");
}

builder.Services.AddDbContextFactory<CarPosDbContext>(
    (DbContextOptionsBuilder options) => options.UseNpgsql(connectionString));

// ---------------------------------------------------------------------------
// Ingest services. Everything is a singleton: the pipeline is driven by one
// sequential MQTT consumer and reaches the scoped world via the context factory.
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<IMasterKeyProtector, MasterKeyProtector>();
builder.Services.AddSingleton<EnvelopeCodec>();
builder.Services.AddSingleton<PositionValidator>();
builder.Services.AddSingleton<IPayloadCryptoService, PayloadCryptoService>();
builder.Services.AddSingleton<IDeviceRegistry, DeviceRegistry>();
builder.Services.AddSingleton<IPositionWriter, PositionWriter>();
builder.Services.AddSingleton<IAckSealer, AckSealer>();
// Singleton so the hosted service can hand it the live MQTT client while the
// pipeline (also a singleton) publishes through it.
builder.Services.AddSingleton<IAckPublisher, MqttAckPublisher>();
// Singleton for the same reason, and shared with the scoped device-config service:
// settings are published on the one broker connection this application owns.
builder.Services.AddSingleton<IConfigPublisher, MqttConfigPublisher>();
builder.Services.AddSingleton<IIngestPipeline, IngestPipeline>();
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
builder.Services.AddScoped<IAccessService, AccessService>();

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
// Shared between the worker and its health check, exactly as MqttConnectionState is
// above: the worker swallows its failures by design, so this is the only way one
// becomes visible from outside the log.
builder.Services.AddSingleton<ScheduleWorkerState>();
builder.Services.AddHostedService<DeviceConfigScheduleWorker>();

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

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.ShareIssuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.ShareAudience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ClockSkew = TimeSpan.Zero,
        };

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

builder.Services.AddAuthorization();

// ---------------------------------------------------------------------------
// Rate limiting on the unauthenticated front door. Everything else needs a valid
// session first, which is its own limit; sign-in is where an attacker gets free
// guesses. Partitioned by client address so one attacker cannot lock out the
// whole world.
// ---------------------------------------------------------------------------
builder.Services.AddRateLimiter((RateLimiterOptions options) =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(RateLimitPolicies.Authentication, (HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(
            // Behind the reverse proxy this is the real client address only because
            // UseForwardedHeaders runs first — see the pipeline below.
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
    options.AddPolicy(RateLimitPolicies.PrivacyOperations, (HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",
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
});

// ---------------------------------------------------------------------------
// Error handling: one handler, ProblemDetails out, nothing internal leaked.
// ---------------------------------------------------------------------------
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

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
    .AddCheck<ProcessHealthCheck>("process");

builder.Services.AddControllers();
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

// CLI mode: provision a device key and exit — the host (Kestrel, MQTT) never starts.
if (DeviceKeyImportCommand.IsRequested(args))
{
    return await DeviceKeyImportCommand.RunAsync(app.Services, args);
}

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

// Runs next so everything downstream — rate-limiting partitions, cookie Secure
// decisions, generated URLs — sees the browser's real address and scheme rather
// than the reverse proxy's. KnownNetworks/Proxies are cleared because the only
// thing that can reach this container's port is the proxy in front of it; leaving
// the default loopback-only list would make it ignore the headers entirely.
ForwardedHeadersOptions forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
};
forwardedHeaders.KnownIPNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeaders);

// Turns any unhandled exception into a ProblemDetails 500 (see GlobalExceptionHandler).
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    // The OpenAPI document describes every endpoint including request shapes, so
    // it stays in Development. It is also not proxied publicly by the frontend's
    // nginx — belt and braces.
    app.MapOpenApi();
}
else
{
    // TLS is terminated at the proxy, so inside the container the API speaks plain
    // HTTP on purpose; redirecting there would bounce a request that is already
    // secure. In any non-container deployment this is still wanted, hence the
    // environment split rather than deleting it.
    app.UseHttpsRedirection();
}

app.UseRateLimiter();

// Order matters: authentication first (so the session cookie is turned into a
// principal), then the CSRF check, then authorisation. Putting CSRF ahead of
// authentication would be just as safe but harder to read in the logs, since the
// rejection would carry no user.
app.UseAuthentication();
app.UseMiddleware<CsrfProtectionMiddleware>();
app.UseAuthorization();

app.MapControllers();

// Health endpoint (unauthenticated by design). The body is a JSON report with one
// entry per dependency, written by HealthReportWriter — which copies only what this
// codebase wrote, never an exception message, precisely because nothing authenticates
// here. Not proxied to the public internet either; see the frontend's nginx.conf.
//
// The default ResultStatusCodes are left alone on purpose: Healthy and Degraded both
// answer 200 and only Unhealthy answers 503, which is the contract the container
// healthcheck reads.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = HealthReportWriter.WriteAsync,
    AllowCachingResponses = false,
});

await app.RunAsync();
return 0;

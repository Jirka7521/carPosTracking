// Program.cs — composition root only: configuration binding, DI registrations,
// the import-device-key CLI branch and the HTTP pipeline. All behaviour lives in
// the layer folders (Options/, Data/, Services/), per project guidelines.

using System.Text;
using System.Threading.RateLimiting;
using CarPosAPI.Data;
using CarPosAPI.Middleware;
using CarPosAPI.Options;
using CarPosAPI.Services.Auth;
using CarPosAPI.Services.Authorization;
using CarPosAPI.Services.Devices;
using CarPosAPI.Services.Ingest;
using CarPosAPI.Services.Positions;
using CarPosAPI.Services.Provisioning;
using CarPosAPI.Services.Security;
using CarPosAPI.Services.Sharing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

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

// ---------------------------------------------------------------------------
// Authorisation and the resource services. Every one of these is scoped: they
// hold the request's DbContext, and the authorizer re-reads the caller's grant
// from the database on every call so a revoked share stops working immediately
// rather than when some token expires.
// ---------------------------------------------------------------------------
builder.Services.AddScoped<IDeviceAccessAuthorizer, DeviceAccessAuthorizer>();
builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddScoped<IPositionQueryService, PositionQueryService>();
builder.Services.AddScoped<IAccessService, AccessService>();

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
});

// ---------------------------------------------------------------------------
// Error handling: one handler, ProblemDetails out, nothing internal leaked.
// ---------------------------------------------------------------------------
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<CarPosDbContext>("database")
    .AddCheck<MqttIngestHealthCheck>("mqtt");

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

// Liveness endpoint (unauthenticated by design; contains no data, only status).
// Not proxied to the public internet — see the frontend's nginx.conf.
app.MapHealthChecks("/health");

await app.RunAsync();
return 0;

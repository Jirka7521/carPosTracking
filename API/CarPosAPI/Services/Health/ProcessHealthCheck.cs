using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CarPosAPI.Services.Health;

/// <summary>
/// Reports which build is running and for how long. Always Healthy — it checks
/// nothing; it answers the question that comes immediately after every other
/// check: <em>is this even the version I deployed?</em>
///
/// <para>
/// Uptime is the other half of that. A dependency that looks fine on a process
/// three minutes old means something very different from the same reading on one
/// that has been up for a week, and without this the reader cannot tell a healthy
/// system from one in a restart loop.
/// </para>
///
/// <para>
/// <b>Nothing here is a secret.</b> The version and the environment name are build
/// facts; the machine name, paths and environment variables are deliberately not
/// included, because this endpoint answers anyone who can reach the port.
/// </para>
/// </summary>
internal sealed class ProcessHealthCheck : IHealthCheck
{
    /// <summary>
    /// Process start time, read once. <see cref="Process.GetCurrentProcess"/>
    /// walks the OS process table, which is more than a probe should do thirty
    /// times a minute for a value that cannot change.
    /// </summary>
    private static readonly DateTime s_startedAtUtc =
        Process.GetCurrentProcess().StartTime.ToUniversalTime();

    /// <summary>
    /// The informational version — the one carrying the git hash when the build
    /// stamps one — falling back to the assembly version.
    /// </summary>
    private static readonly string s_version = ReadVersion();

    private readonly IHostEnvironment _environment;

    /// <summary>Creates the check.</summary>
    /// <param name="environment">Supplies the environment name (Development, Production, …).</param>
    public ProcessHealthCheck(IHostEnvironment environment)
    {
        _environment = environment;
    }

    /// <summary>Reports build and uptime facts.</summary>
    /// <param name="context">Health-check context (unused).</param>
    /// <param name="cancellationToken">Not used — every value is already in memory.</param>
    /// <returns>Always Healthy.</returns>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, object> data = new Dictionary<string, object>
        {
            ["version"] = s_version,
            ["environment"] = _environment.EnvironmentName,
            ["startedAtUtc"] = s_startedAtUtc,
            ["uptimeSeconds"] = Math.Round((DateTime.UtcNow - s_startedAtUtc).TotalSeconds, 0),
            ["runtime"] = RuntimeInformation.FrameworkDescription,
        };

        return Task.FromResult(HealthCheckResult.Healthy("running", data));
    }

    /// <summary>Reads this build's version from the entry assembly's attributes.</summary>
    /// <returns>The informational version, the assembly version, or "unknown".</returns>
    private static string ReadVersion()
    {
        Assembly? assembly = Assembly.GetEntryAssembly();
        if (assembly is null)
        {
            return "unknown";
        }

        AssemblyInformationalVersionAttribute? informational =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        if (informational is not null && !string.IsNullOrWhiteSpace(informational.InformationalVersion))
        {
            return informational.InformationalVersion;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}

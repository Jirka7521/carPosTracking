<#
.SYNOPSIS
    Builds the self-contained CarPos deployment bundle (API + frontend) under
    Application/, collecting every secret interactively.

.DESCRIPTION
    Option 1 of the Ctrl+Shift+M menu (scripts/Dev-QuickMenu.ps1). For each
    configuration value it shows the current value taken from the dev environment
    (or the previous run) and lets you keep it, type a new one, or — for the two
    crypto keys — generate a fresh one.

    Four values are DERIVED, not asked for: FE_BIND_ADDR and BE_BIND_ADDR are
    FE_HOST/BE_HOST resolved to an IP (Docker rejects a hostname in a port
    binding), BE_URL is BE_HOST plus the API's container port, and FE_URL is
    FE_HOST plus FE_PORT. You are only prompted for one of those when it cannot
    be derived or when this PC's DNS disagrees with the previous bundle.

    It only READS the dev config (appsettings.Local.json, FE/public/config.js,
    the Container/*/.env files); it never writes back to them. The output is an
    Application/ folder you copy whole to the Raspberry Pi and bring up with
    `docker compose up -d --build`. Application/ is git-ignored because its .env
    holds real secrets.

.PARAMETER SkipSourceCopy
    Write the .env, compose file and README but skip copying api-src/ + fe-src/.
    Faster when you only want to test the prompts and the generated .env.
#>
[CmdletBinding()]
param(
    [switch]$SkipSourceCopy
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Masking, .env/JSON parsing and the Keep/New/Generate prompt live in the shared
# library, because the database schema sync (Ctrl+Shift+M option 2) needs the same
# primitives. Dot-sourcing this script still brings them into scope for a test.
. (Join-Path $PSScriptRoot 'lib\DevMenu.Common.ps1')

# Paths are derived from the script's own location, so the task can run it from
# any working directory.
$RepoRoot       = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$TemplateDir    = Join-Path $PSScriptRoot 'deploy-template'
$DeployDir      = Join-Path $RepoRoot 'Application'
$ApiSrc         = Join-Path $RepoRoot 'API'
$FeSrc          = Join-Path $RepoRoot 'FE'
$LocalSettings  = Join-Path $RepoRoot 'API\CarPosAPI\appsettings.Local.json'
$ConfigJs       = Join-Path $RepoRoot 'FE\public\config.js'
$PgEnvPath      = Join-Path $RepoRoot 'Container\Postgres\.env'
$MqttEnvPath    = Join-Path $RepoRoot 'Container\MQTTBroker\.env'
$ExampleEnvPath = Join-Path $TemplateDir '.env.example'
$PriorEnvPath   = Join-Path $DeployDir '.env'

# The ports the two containers listen on *inside* the network. Fixed by the
# images (ASPNETCORE_HTTP_PORTS=8080; nginx on 80), not configurable, and not the
# same thing as the published BE_PORT/FE_PORT — a network alias reaches these.
$ApiContainerPort = 8080
$FeContainerPort  = 80

# The addresses the two containers are PINNED to by the bundle's docker-compose.yml
# (ipv4_address). MQTTpublic is macvlan, so these are real LAN addresses; pinning is
# what stops a reboot from handing them out in start order. Mirrored here only so the
# wizard can warn when DNS says something different — edit the compose file, not this.
$ApiPinnedIp = '192.168.124.5'
$FePinnedIp  = '192.168.124.6'

# The keys whose values must never be echoed in full.
$SecretKeys = @('BE_PASSWORD', 'MQTT_PASSWORD', 'DEVICE_KEY_MASTER_KEY', 'JWT_SIGNING_KEY')

# Accounts the broker's ACL actually authorizes (Container/MQTTBroker/mosquitto/acl).
$KnownMqttAccounts = @('admin', 'dashboard', 'GNSS01', 'healthcheck')

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

# Pull googleMapsApiKey out of the dev config.js by regex.
function Get-MapsKeyFromConfigJs {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $match = [regex]::Match((Get-Content -LiteralPath $Path -Raw), 'googleMapsApiKey\s*:\s*"([^"]*)"')
    if ($match.Success -and -not [string]::IsNullOrWhiteSpace($match.Groups[1].Value)) {
        return $match.Groups[1].Value
    }
    return $null
}

# Resolve a hostname to an IPv4 address, or $null. An IP handed in comes back
# unchanged. IPv4 is preferred because the bindings are LAN addresses in
# practice, and a v6 answer would silently bind the wrong interface.
function Resolve-HostToIp {
    param([string]$HostName)
    $parsed = [System.Net.IPAddress]::Loopback
    if ([System.Net.IPAddress]::TryParse($HostName, [ref]$parsed)) { return $HostName }
    try { $addresses = [System.Net.Dns]::GetHostAddresses($HostName) } catch { return $null }
    $v4 = $addresses | Where-Object { $_.AddressFamily -eq 'InterNetwork' } | Select-Object -First 1
    if ($v4) { return $v4.IPAddressToString }
    if ($addresses.Count -gt 0) { return $addresses[0].IPAddressToString }
    return $null
}

# ---------------------------------------------------------------------------
# Validators — mirror the API's own start-up checks, so a bad value is caught on
# the PC instead of on the Pi. Script scope (before Main) so a dot-source can
# reach them for unit tests.
# ---------------------------------------------------------------------------

# Mirrors MqttOptions.HasSupportedBrokerUri. Plaintext is accepted because the
# deployed broker sits on the same container network as the API, so the hop never
# leaves the host; the scheme list still catches a typo like http://.
$validateUri    = { param($v) $v -match '^(ws|wss|mqtt|mqtts)://' }
$validateMaster = { param($v) try { ([Convert]::FromBase64String($v)).Length -eq 32 } catch { $false } }
$validateJwt    = { param($v) [System.Text.Encoding]::UTF8.GetByteCount($v) -ge 32 }
$validatePort   = { param($v) $n = 0; [int]::TryParse($v, [ref]$n) -and $n -ge 1 -and $n -le 65535 }
# Lower-case only: this lands verbatim in an environment variable that .NET's
# configuration binder parses, and it accepts "true"/"false" - not "True"/"1".
$validateBool   = { param($v) $v -ceq 'true' -or $v -ceq 'false' }
# A published port's host side must be an IP literal — Docker does not resolve
# names there. Catching a hostname here beats the container failing to start on
# the Pi with "cannot assign requested address".
$validateBindAddr = { param($v) $ip = [System.Net.IPAddress]::Loopback; [System.Net.IPAddress]::TryParse($v, [ref]$ip) }
# A hostname or an IP. Deliberately permissive about the name itself (".local"
# names are the normal case here); whether it resolves is checked separately,
# because that answer differs between this PC and the Pi.
$validateHostName = { param($v) $v -match '^[A-Za-z0-9]([A-Za-z0-9\-\.]*[A-Za-z0-9])?$' -or $v -match '^[0-9a-fA-F:]+$' }

# Derives a *_BIND_ADDR from a *_HOST. Docker parses the host side of a published
# port as an IP literal and rejects a name, so the address cannot simply be the
# hostname — it has to be resolved here.
#
# Resolution happens on the dev PC, which is not necessarily on the same network
# as the Pi and may well use a different resolver. So this only asks when the
# answer is genuinely ambiguous: DNS and the previous bundle disagreeing, or
# neither producing anything at all.
function Resolve-BindAddress {
    param([string]$Name, [string]$HostName, [string]$Prior)

    $resolved = Resolve-HostToIp $HostName
    Write-Host ''

    if ($resolved -and (-not $Prior -or $resolved -eq $Prior)) {
        Write-Host ("{0} = {1}  ({2} resolved from this PC)" -f $Name, $resolved, $HostName) -ForegroundColor DarkGray
        return $resolved
    }

    if ($resolved) {
        Write-Warning ("{0}: this PC resolves {1} to {2}, but the previous bundle used {3}. Pick the one the Pi knows it by." -f $Name, $HostName, $resolved, $Prior)
    } elseif ($Prior) {
        Write-Warning ("{0}: {1} did not resolve from this PC - it may simply be on another network. Keeping the previous {2}; confirm it is what the Pi knows." -f $Name, $HostName, $Prior)
        return $Prior
    } else {
        Write-Warning ("{0}: {1} did not resolve from this PC and there is no previous value. Enter the address the Pi knows it by (`ip -4 addr` there)." -f $Name, $HostName)
    }

    return Resolve-Setting -Name $Name `
        -Description ("IP behind {0}, used by the compose port binding (Docker rejects a hostname there)." -f $HostName) `
        -Current $resolved -IsSecret $false `
        -Validator $validateBindAddr -ValidatorMessage 'Must be an IP address literal - Docker does not resolve hostnames in a port binding.'
}

# ---------------------------------------------------------------------------
# Output helpers
# ---------------------------------------------------------------------------

# robocopy wrapper: single /XD and /XF lists, and treats robocopy's success codes
# (< 8) as success rather than letting a "1 = copied" leak out as failure.
#
# /MIR rather than deleting the destination first: the bundle contains a
# .sln/.csproj, so VS Code's C# language server loads it and holds a handle on
# Application\api-src — a wholesale Remove-Item then fails with "being used by
# another process" and every re-run dies. Mirroring syncs in place without
# removing the tree. /R:1 /W:1 keeps a genuinely locked file from stalling.
function Invoke-Robocopy {
    param([string]$Source, [string]$Dest, [string[]]$ExcludeDirs = @(), [string[]]$ExcludeFiles = @())
    $roboArgs = @($Source, $Dest, '/MIR', '/NFL', '/NDL', '/NJH', '/NJS', '/NP', '/R:1', '/W:1')
    if ($ExcludeDirs.Count -gt 0)  { $roboArgs += '/XD'; $roboArgs += $ExcludeDirs }
    if ($ExcludeFiles.Count -gt 0) { $roboArgs += '/XF'; $roboArgs += $ExcludeFiles }
    robocopy @roboArgs | Out-Null
    $code = $LASTEXITCODE
    $global:LASTEXITCODE = 0
    if ($code -ge 8) {
        throw ("robocopy failed (exit {0}) copying {1} -> {2}" -f $code, $Source, $Dest)
    }
}

# Sweep build output that appeared in the bundle *after* a copy: an editor that
# opens the bundled solution restores/builds it in place, and robocopy's /XD both
# keeps those directories out of the copy and shields them from /MIR's purge — so
# without this they accumulate and get shipped. Best-effort by design; a handle
# held by an editor must never fail the publish, and Docker ignores these anyway.
function Remove-StaleBuildOutput {
    param([string]$Root, [string[]]$Names)
    if (-not (Test-Path -Path $Root)) { return }
    foreach ($stale in @(Get-ChildItem -Path $Root -Recurse -Directory -Include $Names -ErrorAction SilentlyContinue)) {
        Remove-Item -Path $stale.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# UTF-8 without BOM, LF line endings - what a Linux .env / compose expects.
function Write-Utf8NoBom {
    param([string]$Path, [string]$Content)
    [System.IO.File]::WriteAllText($Path, $Content, (New-Object System.Text.UTF8Encoding($false)))
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

# When dot-sourced (e.g. to unit-test the helpers above) rather than executed,
# stop here so the interactive flow does not start.
if ($MyInvocation.InvocationName -eq '.') { return }

try {
    Write-Host ''
    Write-Host 'CarPos deployment publisher' -ForegroundColor Green
    Write-Host ('Repo:   {0}' -f $RepoRoot) -ForegroundColor DarkGray
    Write-Host ('Output: {0}' -f $DeployDir) -ForegroundColor DarkGray

    if (-not (Test-Path -LiteralPath $TemplateDir)) {
        throw ("Template folder missing: {0}" -f $TemplateDir)
    }

    # --- Load every "current value" source once --------------------------
    $priorEnv   = Read-DotEnv $PriorEnvPath
    $pgEnv      = Read-DotEnv $PgEnvPath
    $mqttEnv    = Read-DotEnv $MqttEnvPath
    $exampleEnv = Read-DotEnv $ExampleEnvPath
    $localJson  = Read-JsonFile $LocalSettings
    $devMapsKey = Get-MapsKeyFromConfigJs $ConfigJs

    # The dev connection string supplies the database values so they can simply be
    # kept. Heads-up: in dev that login is the admin/owner account, whereas the
    # deployed stack is written for the DML-only BE role — keeping it deploys the
    # API with superuser rights on the database.
    $devConn = ConvertFrom-ConnectionString ([string](Get-JsonPath $localJson 'ConnectionStrings.CarPos'))

    if ($priorEnv.Count -gt 0) {
        Write-Host 'Found a previous Application/.env - its values are offered as the current ones.' -ForegroundColor DarkGray
    }

    # Current value per key, by precedence: previous bundle, then the dev config,
    # then the example's default.
    $cur = @{}
    $cur.PostgresDb = Get-FirstNonEmpty @((Get-TableValue $priorEnv 'POSTGRES_DB'), (Get-TableValue $pgEnv 'POSTGRES_DB'), (Get-TableValue $devConn 'database'), (Get-TableValue $exampleEnv 'POSTGRES_DB'))
    $cur.BeUser     = Get-FirstNonEmpty @((Get-TableValue $priorEnv 'BE_USER'), (Get-TableValue $pgEnv 'BE_USER'), (Get-TableValue $devConn 'username'), (Get-TableValue $exampleEnv 'BE_USER'))
    $cur.BePass     = Get-FirstNonEmpty @((Get-TableValue $priorEnv 'BE_PASSWORD'), (Get-TableValue $pgEnv 'BE_PASSWORD'), (Get-TableValue $devConn 'password'))
    $cur.MqttUri    = Get-FirstNonEmpty @((Get-TableValue $priorEnv 'MQTT_BROKER_URI'), (Get-JsonPath $localJson 'Mqtt.BrokerUri'), (Get-TableValue $exampleEnv 'MQTT_BROKER_URI'))
    $cur.MqttUser   = Get-FirstNonEmpty @((Get-TableValue $priorEnv 'MQTT_USERNAME'), (Get-JsonPath $localJson 'Mqtt.Username'), (Get-TableValue $exampleEnv 'MQTT_USERNAME'))
    $cur.MqttPass   = Get-FirstNonEmpty @((Get-TableValue $priorEnv 'MQTT_PASSWORD'), (Get-JsonPath $localJson 'Mqtt.Password'), (Get-TableValue $mqttEnv 'DASHBOARD_PASSWORD'))
    $cur.MasterKey  = Get-FirstNonEmpty @((Get-TableValue $priorEnv 'DEVICE_KEY_MASTER_KEY'), (Get-JsonPath $localJson 'DeviceKeyProtection.MasterKeyBase64'))
    $cur.JwtKey     = Get-FirstNonEmpty @((Get-TableValue $priorEnv 'JWT_SIGNING_KEY'), (Get-JsonPath $localJson 'Jwt.SigningKey'))
    $cur.MapsKey    = Get-FirstNonEmpty @((Get-TableValue $priorEnv 'CARPOS_GOOGLE_MAPS_API_KEY'), $devMapsKey)
    $cur.FeHost     = Get-FirstNonEmpty @((Get-TableValue $priorEnv 'FE_HOST'), (Get-TableValue $exampleEnv 'FE_HOST'))
    $cur.BeHost     = Get-FirstNonEmpty @((Get-TableValue $priorEnv 'BE_HOST'), (Get-TableValue $exampleEnv 'BE_HOST'))
    $cur.FePort     = Get-FirstNonEmpty @((Get-TableValue $priorEnv 'FE_PORT'), (Get-TableValue $exampleEnv 'FE_PORT'))
    $cur.BePort     = Get-FirstNonEmpty @((Get-TableValue $priorEnv 'BE_PORT'), (Get-TableValue $exampleEnv 'BE_PORT'))
    $cur.SecureCook = Get-FirstNonEmpty @((Get-TableValue $priorEnv 'AUTH_SECURE_COOKIES'), (Get-TableValue $exampleEnv 'AUTH_SECURE_COOKIES'))
    # The two bind addresses are derived below; the previous ones only serve as a
    # fallback for when this PC cannot resolve the names.
    $cur.FeBindAddr = Get-TableValue $priorEnv 'FE_BIND_ADDR'
    $cur.BeBindAddr = Get-TableValue $priorEnv 'BE_BIND_ADDR'

    # --- Drive the wizard ------------------------------------------------
    $values = [ordered]@{}
    $values['POSTGRES_DB'] = Resolve-Setting -Name 'POSTGRES_DB' `
        -Description 'Database name; must match the Postgres stack.' -Current $cur.PostgresDb -IsSecret $false
    $values['BE_USER'] = Resolve-Setting -Name 'BE_USER' `
        -Description 'Backend DB login. Container/Postgres/.env, else the dev connection string (whose login is admin, not the DML-only BE role).' `
        -Current $cur.BeUser -IsSecret $false
    $values['BE_PASSWORD'] = Resolve-Setting -Name 'BE_PASSWORD' `
        -Description 'Password for BE_USER (Container/Postgres/.env, else the dev connection string).' `
        -Current $cur.BePass -IsSecret $true
    $values['MQTT_BROKER_URI'] = Resolve-Setting -Name 'MQTT_BROKER_URI' `
        -Description 'The one broker address, used for all MQTT. Also written into device firmware, so a container-internal address must be fixed up by hand there.' `
        -Current $cur.MqttUri -IsSecret $false `
        -Validator $validateUri -ValidatorMessage 'Must start with ws://, wss://, mqtt:// or mqtts://.'
    $values['MQTT_USERNAME'] = Resolve-Setting -Name 'MQTT_USERNAME' `
        -Description 'Broker account the API logs in as; needs read on devices/#.' -Current $cur.MqttUser -IsSecret $false
    $values['MQTT_PASSWORD'] = Resolve-Setting -Name 'MQTT_PASSWORD' `
        -Description 'Password for MQTT_USERNAME.' -Current $cur.MqttPass -IsSecret $true
    $values['DEVICE_KEY_MASTER_KEY'] = Resolve-Setting -Name 'DEVICE_KEY_MASTER_KEY' `
        -Description 'Base64 of exactly 32 bytes. Rotating it makes existing device keys undecryptable.' `
        -Current $cur.MasterKey -IsSecret $true -GenerateBytes 32 `
        -Validator $validateMaster -ValidatorMessage 'Must be base64 decoding to exactly 32 bytes.'
    $values['JWT_SIGNING_KEY'] = Resolve-Setting -Name 'JWT_SIGNING_KEY' `
        -Description 'At least 32 bytes. Rotating it invalidates every active session.' `
        -Current $cur.JwtKey -IsSecret $true -GenerateBytes 48 `
        -Validator $validateJwt -ValidatorMessage 'Must be at least 32 bytes (characters).'
    $values['CARPOS_GOOGLE_MAPS_API_KEY'] = Resolve-Setting -Name 'CARPOS_GOOGLE_MAPS_API_KEY' `
        -Description 'Google Maps JS API key (served to the browser; restrict it by HTTP referrer).' `
        -Current $cur.MapsKey -IsSecret $false
    $values['FE_HOST'] = Resolve-Setting -Name 'FE_HOST' `
        -Description 'LAN name of the dashboard, e.g. carposfe.local. Also registered as a Docker network alias, so it resolves inside the containers too. localhost keeps it off the LAN entirely.' `
        -Current $cur.FeHost -IsSecret $false `
        -Validator $validateHostName -ValidatorMessage 'Must be a hostname or an IP address.'
    $values['FE_PORT'] = Resolve-Setting -Name 'FE_PORT' `
        -Description ("Host port for the dashboard (<FE_BIND_ADDR>:<port>:{0}). Keep it {0} so FE_HOST means the same thing from the LAN and from inside Docker." -f $FeContainerPort) `
        -Current $cur.FePort -IsSecret $false `
        -Validator $validatePort -ValidatorMessage 'Must be a port number between 1 and 65535.'
    $values['BE_HOST'] = Resolve-Setting -Name 'BE_HOST' `
        -Description 'LAN name of the API, e.g. carposbe.local. Also a Docker network alias, which is what lets the frontend proxy and the healthcheck use this same name from inside the network.' `
        -Current $cur.BeHost -IsSecret $false `
        -Validator $validateHostName -ValidatorMessage 'Must be a hostname or an IP address.'
    $values['BE_PORT'] = Resolve-Setting -Name 'BE_PORT' `
        -Description ("Host port the API is published on (<BE_BIND_ADDR>:<port>:{0}), for /health and /openapi - the SPA goes through the frontend proxy. Keep it {0} so BE_HOST means the same thing on both sides." -f $ApiContainerPort) `
        -Current $cur.BePort -IsSecret $false `
        -Validator $validatePort -ValidatorMessage 'Must be a port number between 1 and 65535.'
    $values['AUTH_SECURE_COOKIES'] = Resolve-Setting -Name 'AUTH_SECURE_COOKIES' `
        -Description 'Secure flag on the session cookie. Must be false when the dashboard is served over plain http, or every request after sign-in 401s.' `
        -Current $cur.SecureCook -IsSecret $false `
        -Validator $validateBool -ValidatorMessage 'Must be true or false.'

    # --- Derived values ---------------------------------------------------
    Write-Host ''
    Write-Host 'Derived addresses' -ForegroundColor Cyan

    $values['FE_BIND_ADDR'] = Resolve-BindAddress -Name 'FE_BIND_ADDR' -HostName $values['FE_HOST'] -Prior $cur.FeBindAddr
    $values['BE_BIND_ADDR'] = Resolve-BindAddress -Name 'BE_BIND_ADDR' -HostName $values['BE_HOST'] -Prior $cur.BeBindAddr

    # BE_URL is what nginx proxies /api/ to and what the API healthcheck probes,
    # and both run INSIDE the network - where BE_HOST is a Docker alias pointing at
    # the container. So the port is the container's, not the published BE_PORT.
    $values['BE_URL'] = ('http://{0}:{1}' -f $values['BE_HOST'], $ApiContainerPort)

    # FE_URL is browser-facing and documentation-only. https when the Secure
    # cookie flag is on, since that only makes sense behind a TLS terminator.
    $feScheme = if ($values['AUTH_SECURE_COOKIES'] -ceq 'true') { 'https' } else { 'http' }
    $feSuffix = if ($values['FE_PORT'] -in @('80', '443')) { '' } else { ':' + $values['FE_PORT'] }
    $values['FE_URL'] = ('{0}://{1}{2}' -f $feScheme, $values['FE_HOST'], $feSuffix)

    Write-Host ("BE_URL = {0}  (in-network alias -> the API container)" -f $values['BE_URL']) -ForegroundColor DarkGray
    Write-Host ("FE_URL = {0}  (documentation only)" -f $values['FE_URL']) -ForegroundColor DarkGray

    # --- Post-collection checks -----------------------------------------
    # The bundle pins both containers to fixed addresses on the macvlan network, so
    # what DNS answers for FE_HOST/BE_HOST has to agree with the pin - otherwise the
    # name reaches one container while .env describes another. A mismatch means
    # either the DNS record moved or the compose pin did; the wizard cannot tell
    # which, so it points at both rather than silently picking one.
    foreach ($pin in @(
        @{ Name = 'FE'; HostName = $values['FE_HOST']; Addr = $values['FE_BIND_ADDR']; Pinned = $FePinnedIp },
        @{ Name = 'BE'; HostName = $values['BE_HOST']; Addr = $values['BE_BIND_ADDR']; Pinned = $ApiPinnedIp }
    )) {
        if ($pin.Addr -notin @('127.0.0.1', '::1') -and $pin.Addr -ne $pin.Pinned) {
            Write-Warning ("{0}_HOST ({1}) resolves to {2}, but docker-compose.yml pins the container to {3}. Update the DNS record or the ipv4_address in the bundle's docker-compose.yml so the two agree." -f $pin.Name, $pin.HostName, $pin.Addr, $pin.Pinned)
        }
    }

    # A LAN-published dashboard is plain HTTP, where a Secure cookie is silently
    # dropped by the browser — the deployment comes up healthy and then fails at
    # the first request after sign-in, which is a miserable thing to debug on the
    # Pi. Warn either way round rather than picking for the operator, since the
    # safe choice depends on whether a TLS terminator is actually in front.
    $isLoopbackFe = $values['FE_BIND_ADDR'] -in @('127.0.0.1', '::1')
    if (-not $isLoopbackFe -and $values['AUTH_SECURE_COOKIES'] -ceq 'true') {
        Write-Warning ("FE_BIND_ADDR is {0} (not loopback) but AUTH_SECURE_COOKIES is true. If that interface is reached over plain http, sign-in will succeed and every request after it will 401. Set AUTH_SECURE_COOKIES=false, or keep TLS in front." -f $values['FE_BIND_ADDR'])
    }
    if ($isLoopbackFe -and $values['AUTH_SECURE_COOKIES'] -ceq 'false') {
        Write-Warning 'AUTH_SECURE_COOKIES is false while the dashboard is loopback-only, which implies TLS in front of it. The Secure flag costs nothing there - consider setting it back to true.'
    }

    # Publishing the API off loopback is a bigger step than it looks: nginx is what
    # normally keeps /openapi and the raw endpoints off the network.
    if ($values['BE_BIND_ADDR'] -notin @('127.0.0.1', '::1')) {
        Write-Warning ("BE_BIND_ADDR is {0} (not loopback): the entire API, sign-in endpoints included, is published on that interface over plain HTTP - not just /health. Keep it to a trusted network." -f $values['BE_BIND_ADDR'])
    }

    # A published port that differs from the container port makes the host name
    # ambiguous: the Docker alias reaches the container port, a browser reaches
    # this one. Legal, but worth saying out loud.
    if ($values['BE_PORT'] -ne [string]$ApiContainerPort) {
        Write-Warning ("BE_PORT is {0}, not {1}: from the LAN the API is http://{2}:{0}, but inside Docker the same name is {3}. Two ports for one host name." -f $values['BE_PORT'], $ApiContainerPort, $values['BE_HOST'], $values['BE_URL'])
    }
    if ($values['FE_PORT'] -ne [string]$FeContainerPort) {
        Write-Warning ("FE_PORT is {0}, not {1}: from the LAN the dashboard is on port {0}, but inside Docker the FE_HOST alias reaches port {1}." -f $values['FE_PORT'], $FeContainerPort)
    }

    if ($values['MQTT_USERNAME'] -notin $KnownMqttAccounts) {
        Write-Warning ("MQTT_USERNAME '{0}' is not one of the broker's known accounts ({1}). Add it to Container/MQTTBroker/mosquitto/acl (read on devices/#) or the API cannot connect." -f $values['MQTT_USERNAME'], ($KnownMqttAccounts -join ', '))
    }

    # Final validation, so a kept-but-invalid dev value is caught before writing.
    $problems = @()
    if (-not (& $validateUri      $values['MQTT_BROKER_URI']))      { $problems += 'MQTT_BROKER_URI is not ws/wss/mqtt/mqtts' }
    if (-not (& $validateMaster   $values['DEVICE_KEY_MASTER_KEY'])) { $problems += 'DEVICE_KEY_MASTER_KEY does not decode to 32 bytes' }
    if (-not (& $validateJwt      $values['JWT_SIGNING_KEY']))      { $problems += 'JWT_SIGNING_KEY is shorter than 32 bytes' }
    if (-not (& $validateHostName $values['FE_HOST']))              { $problems += 'FE_HOST is not a hostname or IP' }
    if (-not (& $validateHostName $values['BE_HOST']))              { $problems += 'BE_HOST is not a hostname or IP' }
    if (-not (& $validateBindAddr $values['FE_BIND_ADDR']))         { $problems += 'FE_BIND_ADDR is not an IP address' }
    if (-not (& $validateBindAddr $values['BE_BIND_ADDR']))         { $problems += 'BE_BIND_ADDR is not an IP address' }
    if (-not (& $validatePort     $values['FE_PORT']))              { $problems += 'FE_PORT is not a valid port' }
    if (-not (& $validatePort     $values['BE_PORT']))              { $problems += 'BE_PORT is not a valid port' }
    if (-not (& $validateBool     $values['AUTH_SECURE_COOKIES']))  { $problems += 'AUTH_SECURE_COOKIES must be lower-case true or false' }
    # Both names are network aliases on the same network, so one name cannot mean
    # both containers - Docker would answer with whichever it feels like.
    if ($values['FE_HOST'] -eq $values['BE_HOST']) {
        $problems += ("FE_HOST and BE_HOST are both '{0}' - they are Docker network aliases and must be distinct" -f $values['FE_HOST'])
    }
    # Docker only rejects an overlapping binding when the second container starts,
    # on the Pi, with a message that names the port and nothing else. The same port
    # on two different addresses is fine.
    if ($values['FE_BIND_ADDR'] -eq $values['BE_BIND_ADDR'] -and $values['FE_PORT'] -eq $values['BE_PORT']) {
        $problems += ("FE and BE are both bound to {0}:{1} - Docker will refuse to start the second container" -f $values['FE_BIND_ADDR'], $values['FE_PORT'])
    }
    foreach ($key in $values.Keys) {
        if ([string]::IsNullOrWhiteSpace($values[$key])) { $problems += ("{0} is empty" -f $key) }
    }
    if ($problems.Count -gt 0) {
        Write-Host ''
        Write-Host 'Cannot write - fix these first:' -ForegroundColor Red
        foreach ($problem in $problems) { Write-Host ("  - " + $problem) -ForegroundColor Red }
        throw 'Validation failed.'
    }

    # Warn (don't block) on characters some .env parsers treat specially.
    foreach ($key in $values.Keys) {
        $v = [string]$values[$key]
        if ($v.Contains('$') -or $v.Contains('#') -or $v.Contains('"') -or $v.Contains("'") -or ($v -ne $v.Trim())) {
            Write-Warning ("{0} contains a character ($, #, quote or surrounding space) that some .env parsers treat specially. It is written literally - verify the container reads it as intended." -f $key)
        }
    }

    # --- Review + confirm ------------------------------------------------
    Write-Host ''
    Write-Host 'Summary (secrets masked):' -ForegroundColor Cyan
    foreach ($key in $values.Keys) {
        Write-Host ('  {0,-28} {1}' -f $key, (Format-Masked $values[$key] ($key -in $SecretKeys)))
    }
    Write-Host ''
    $confirm = Read-Host 'Write this bundle to Application/ ? [y/N]'
    if ($confirm.Trim().ToUpper() -ne 'Y') {
        Write-Host 'Cancelled - nothing was written.' -ForegroundColor Yellow
        exit 0
    }

    # --- Write the bundle ------------------------------------------------
    New-Item -ItemType Directory -Force -Path $DeployDir | Out-Null

    $envLines = @(
        '# Generated by scripts/Publish-Deployment.ps1 - do NOT commit (holds real secrets).',
        ('# Written ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')),
        '# FE_BIND_ADDR, BE_BIND_ADDR, BE_URL and FE_URL are derived from the host',
        '# names and ports above them - edit the name, re-run the wizard.',
        ''
    )
    foreach ($key in $values.Keys) { $envLines += ('{0}={1}' -f $key, $values[$key]) }
    Write-Utf8NoBom -Path $PriorEnvPath -Content (($envLines -join "`n") + "`n")

    Copy-Item -LiteralPath (Join-Path $TemplateDir 'docker-compose.yml') -Destination (Join-Path $DeployDir 'docker-compose.yml') -Force
    Copy-Item -LiteralPath (Join-Path $TemplateDir 'README.md')          -Destination (Join-Path $DeployDir 'README.md') -Force
    Copy-Item -LiteralPath $ExampleEnvPath                               -Destination (Join-Path $DeployDir '.env.example') -Force

    if ($SkipSourceCopy) {
        Write-Host 'Skipped source copy (-SkipSourceCopy). api-src/ and fe-src/ were NOT refreshed.' -ForegroundColor Yellow
    } else {
        Write-Host ''
        Write-Host 'Copying sources (excluding build output, node_modules and dev secrets)...' -ForegroundColor DarkGray
        # No pre-delete: Invoke-Robocopy mirrors in place, which is what keeps a
        # re-run working while an editor holds the old bundle open.
        $apiDest = Join-Path $DeployDir 'api-src'
        $feDest  = Join-Path $DeployDir 'fe-src'

        Invoke-Robocopy -Source $ApiSrc -Dest $apiDest `
            -ExcludeDirs  @('bin', 'obj', '.vs', '.vscode', '.idea', '.git') `
            -ExcludeFiles @((Join-Path $ApiSrc 'CarPosAPI\appsettings.Local.json'))

        Invoke-Robocopy -Source $FeSrc -Dest $feDest `
            -ExcludeDirs  @('node_modules', 'dist', 'dist-ssr', '.vs', '.vscode', '.idea', '.git') `
            -ExcludeFiles @((Join-Path $FeSrc 'public\config.js'), '*.log')

        Remove-StaleBuildOutput -Root $apiDest -Names @('bin', 'obj')
        Remove-StaleBuildOutput -Root $feDest  -Names @('node_modules', 'dist', 'dist-ssr')
    }

    # --- Done ------------------------------------------------------------
    Write-Host ''
    Write-Host ('Bundle written to {0}' -f $DeployDir) -ForegroundColor Green
    Write-Host ''
    Write-Host 'Addresses:' -ForegroundColor Cyan
    Write-Host ('  Dashboard       {0}   -> {1}:{2}' -f $values['FE_URL'], $values['FE_BIND_ADDR'], $values['FE_PORT'])
    Write-Host ('  API health      http://{0}:{1}/health   -> {2}:{1}' -f $values['BE_HOST'], $values['BE_PORT'], $values['BE_BIND_ADDR'])
    Write-Host ('  API in-network  {0}   (nginx proxy target + healthcheck, via the Docker alias)' -f $values['BE_URL'])
    Write-Host ''
    Write-Host 'Next steps:' -ForegroundColor Cyan
    Write-Host '  1. Copy the whole Application/ folder to the Raspberry Pi.'
    Write-Host ('  2. Confirm {0} and {1} are free for the containers to claim - the compose file pins them.' -f $values['FE_BIND_ADDR'], $values['BE_BIND_ADDR'])
    Write-Host '  3. Make sure the MQTTpublic network + the Postgres and MQTTBroker stacks are up.'
    Write-Host '     MQTTpublic must be macvlan with a configured subnet, or the pinned addresses'
    Write-Host '     are rejected:  docker network inspect MQTTpublic --format ''{{.Driver}} {{json .IPAM.Config}}'''
    Write-Host '  4. Inside the copied folder:  docker compose up -d --build'
    Write-Host '  See Application/README.md for the full checklist.'
    Write-Host ''
    Write-Host 'Reminder: Application/ is git-ignored - its .env holds real secrets.' -ForegroundColor DarkGray

    exit 0
}
catch {
    Write-Host ''
    Write-Host ('ERROR: ' + $_.Exception.Message) -ForegroundColor Red
    exit 1
}

<#
.SYNOPSIS
    Helpers shared by the Ctrl+Shift+M scripts (scripts/Dev-QuickMenu.ps1 and the
    two tools it dispatches to).

.DESCRIPTION
    Dot-source it:  . (Join-Path $PSScriptRoot 'lib\DevMenu.Common.ps1')

    Everything here was factored out of Publish-Deployment.ps1 when the database
    schema sync gained a second use for the same prompt, masking and config-parsing
    primitives. The bodies are unchanged from that script; the only reason they
    moved is that two callers now need them, and one copy is better than two.

    Nothing here writes a file or opens a connection - these are pure helpers plus
    the interactive Resolve-Setting prompt.
#>

# ---------------------------------------------------------------------------
# Masking and secret entry
# ---------------------------------------------------------------------------

# Show only the last 4 characters of a secret; never reveal its length.
function Format-Masked {
    param([string]$Value, [bool]$IsSecret)
    if ([string]::IsNullOrEmpty($Value)) { return '(not set)' }
    if (-not $IsSecret) { return $Value }
    if ($Value.Length -le 4) { return '****' }
    return '******' + $Value.Substring($Value.Length - 4)
}

# Turn a SecureString from Read-Host into plain text (callers must write it to a
# .env, or hand it to a tool, so it cannot stay secure all the way down).
function ConvertFrom-SecureStringPlain {
    param([System.Security.SecureString]$Secure)
    $cred = New-Object System.Management.Automation.PSCredential('x', $Secure)
    return $cred.GetNetworkCredential().Password
}

# N random bytes, base64-encoded. Uses the framework RNG so no openssl is needed.
function New-Base64Key {
    param([int]$Bytes)
    $buffer = [byte[]]::new($Bytes)
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($buffer) } finally { $rng.Dispose() }
    return [Convert]::ToBase64String($buffer)
}

# ---------------------------------------------------------------------------
# Reading existing configuration
# ---------------------------------------------------------------------------

# Parse a KEY=VALUE .env file into a hashtable. Missing file -> empty hashtable.
function Read-DotEnv {
    param([string]$Path)
    $table = @{}
    if (-not (Test-Path -LiteralPath $Path)) { return $table }
    foreach ($line in (Get-Content -LiteralPath $Path)) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }
        $idx = $trimmed.IndexOf('=')
        if ($idx -lt 1) { continue }
        $key = $trimmed.Substring(0, $idx).Trim()
        $val = $trimmed.Substring($idx + 1).Trim()
        # Strip one layer of matching surrounding quotes, if present.
        if ($val.Length -ge 2) {
            $first = $val.Substring(0, 1)
            $last  = $val.Substring($val.Length - 1, 1)
            if (($first -eq '"' -and $last -eq '"') -or ($first -eq "'" -and $last -eq "'")) {
                $val = $val.Substring(1, $val.Length - 2)
            }
        }
        $table[$key] = $val
    }
    return $table
}

# Read a value from a hashtable, returning $null (not throwing) when absent.
function Get-TableValue {
    param([hashtable]$Table, [string]$Key)
    if ($Table.ContainsKey($Key)) { return $Table[$Key] }
    return $null
}

# Split an ADO.NET / Npgsql connection string into a hashtable keyed by
# lower-cased option name (they are case-insensitive in Npgsql), so a dev
# connection string can supply values as keep-able current ones.
function ConvertFrom-ConnectionString {
    param([string]$ConnectionString)
    $table = @{}
    if ([string]::IsNullOrWhiteSpace($ConnectionString)) { return $table }
    foreach ($part in $ConnectionString.Split(';')) {
        $trimmed = $part.Trim()
        if ($trimmed -eq '') { continue }
        $idx = $trimmed.IndexOf('=')
        if ($idx -lt 1) { continue }
        $key = $trimmed.Substring(0, $idx).Trim().ToLowerInvariant()
        $table[$key] = $trimmed.Substring($idx + 1).Trim()
    }
    return $table
}

# Parse a JSON file to an object. Missing/invalid -> $null (with a warning).
function Read-JsonFile {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    try {
        return (Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json)
    } catch {
        Write-Warning ("Could not parse {0}: {1}" -f $Path, $_.Exception.Message)
        return $null
    }
}

# Walk a dotted path (e.g. 'Mqtt.Username') without tripping StrictMode on a
# missing property. Blank strings count as absent.
function Get-JsonPath {
    param([object]$Object, [string]$DottedPath)
    if ($null -eq $Object) { return $null }
    $current = $Object
    foreach ($segment in $DottedPath.Split('.')) {
        if ($null -eq $current) { return $null }
        $property = $current.PSObject.Properties[$segment]
        if ($null -eq $property) { return $null }
        $current = $property.Value
    }
    if ($current -is [string] -and [string]::IsNullOrWhiteSpace($current)) { return $null }
    return $current
}

# First non-empty string from an ordered list of candidate sources.
function Get-FirstNonEmpty {
    param([object[]]$Values)
    foreach ($value in $Values) {
        if ($null -eq $value) { continue }
        $text = [string]$value
        if (-not [string]::IsNullOrWhiteSpace($text)) { return $text }
    }
    return $null
}

# ---------------------------------------------------------------------------
# The per-key wizard: prints the current value and drives Keep / New / Generate.
# ---------------------------------------------------------------------------
function Resolve-Setting {
    param(
        [string]$Name,
        [string]$Description,
        [string]$Current,
        [bool]$IsSecret,
        [int]$GenerateBytes = 0,
        [scriptblock]$Validator = $null,
        [string]$ValidatorMessage = 'Invalid value.'
    )

    Write-Host ''
    Write-Host ("=== {0} ===" -f $Name) -ForegroundColor Cyan
    if ($Description) { Write-Host $Description -ForegroundColor DarkGray }

    $hasCurrent = -not [string]::IsNullOrWhiteSpace($Current)
    if ($hasCurrent) {
        Write-Host ("Current: {0}" -f (Format-Masked $Current $IsSecret)) -ForegroundColor Gray
    } else {
        Write-Host "Current: (not set - you must provide a value)" -ForegroundColor Yellow
    }

    $labels = @{ K = '[K]eep'; N = '[N]ew value'; G = '[G]enerate' }

    while ($true) {
        $options = @()
        if ($hasCurrent) { $options += 'K' }
        $options += 'N'
        if ($GenerateBytes -gt 0) { $options += 'G' }

        $default = if ($hasCurrent) { 'K' } else { 'N' }
        $promptText = (($options | ForEach-Object { $labels[$_] }) -join '  ')
        $answer = Read-Host ("{0}  (default {1})" -f $promptText, $default)
        if ([string]::IsNullOrWhiteSpace($answer)) { $answer = $default }
        $answer = $answer.Trim().ToUpper()

        if ($answer -eq 'K' -and $hasCurrent) { return $Current }

        if ($answer -eq 'G' -and $GenerateBytes -gt 0) {
            Write-Host ("Generated a fresh {0}-byte key." -f $GenerateBytes) -ForegroundColor Green
            return (New-Base64Key $GenerateBytes)
        }

        if ($answer -eq 'N') {
            if ($IsSecret) {
                $entered = ConvertFrom-SecureStringPlain (Read-Host "  Enter new value" -AsSecureString)
            } else {
                $entered = Read-Host "  Enter new value"
            }
            if ([string]::IsNullOrWhiteSpace($entered)) {
                Write-Host "  Empty value - try again." -ForegroundColor Red
                continue
            }
            $entered = $entered.Trim()
            if ($null -ne $Validator -and -not (& $Validator $entered)) {
                Write-Host ("  " + $ValidatorMessage) -ForegroundColor Red
                continue
            }
            return $entered
        }

        Write-Host "  Choose one of the shown options." -ForegroundColor Red
    }
}

<#
.SYNOPSIS
    Compares a database against the schema the source code describes, shows every
    difference, and applies only what you pick.

.DESCRIPTION
    Option 2 of the Ctrl+Shift+M menu (scripts/Dev-QuickMenu.ps1).

    Reads the target database's real tables and columns, compares them with the
    compiled EF model plus the migrations in API/CarPosAPI/Data/Migrations/, and
    prints every difference as a numbered change. You choose which to apply - all,
    none, or specific numbers - and anything that would destroy data is listed again
    with its row count and needs the word APPLY typed in full.

    The comparison and the changes themselves are done by the API's own schema-sync
    CLI mode, which has both the model and Npgsql to hand; this script picks the
    database, drives the prompts and holds the gate. Nothing runs that was not
    listed and confirmed.

.PARAMETER Connection
    Skip the connection prompt and use this connection string. Intended for
    re-running against the same database; the menu path prompts instead.

.PARAMETER SkipBuild
    Reuse the previous scratch build of the API instead of rebuilding it. Faster on
    a re-run when no C# has changed.
#>
[CmdletBinding()]
param(
    [string]$Connection,
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'lib\DevMenu.Common.ps1')

$RepoRoot      = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ApiProject    = Join-Path $RepoRoot 'API\CarPosAPI\CarPosAPI.csproj'
$LocalSettings = Join-Path $RepoRoot 'API\CarPosAPI\appsettings.Local.json'

# The API is built into a scratch folder rather than its own bin/obj: the debugger
# very often has the normal output locked (the API is usually running while you
# reach for this), and a schema check that cannot run because a build failed is a
# schema check nobody does.
$ScratchDir   = Join-Path $env:TEMP 'carpos-schema-sync'
$BuildOutput  = Join-Path $ScratchDir 'build'
$SummaryPath  = Join-Path $ScratchDir 'summary.json'
$ToolDll      = Join-Path $BuildOutput 'Debug\net10.0\CarPosAPI.dll'

# Logins that own the schema and may run DDL. Container/Postgres/README.md: admin is
# the owner, BE is deliberately DML-only and cannot ALTER anything.
$DdlCapableUsers = @('admin', 'postgres')

# ---------------------------------------------------------------------------
# Connection selection
# ---------------------------------------------------------------------------

# Builds a connection string from answers typed at the prompt. The password is read
# as a SecureString and converted only at the end - it is never echoed and never
# written to a file.
function Read-ConnectionFromPrompts {
    param([hashtable]$Defaults)

    $hostName = Read-Host ("  Host      [{0}]" -f (Get-TableValue $Defaults 'host'))
    if ([string]::IsNullOrWhiteSpace($hostName)) { $hostName = Get-TableValue $Defaults 'host' }

    $port = Read-Host ("  Port      [{0}]" -f (Get-FirstNonEmpty @((Get-TableValue $Defaults 'port'), '5432')))
    if ([string]::IsNullOrWhiteSpace($port)) { $port = Get-FirstNonEmpty @((Get-TableValue $Defaults 'port'), '5432') }

    $database = Read-Host ("  Database  [{0}]" -f (Get-TableValue $Defaults 'database'))
    if ([string]::IsNullOrWhiteSpace($database)) { $database = Get-TableValue $Defaults 'database' }

    $username = Read-Host "  Username  [admin]"
    if ([string]::IsNullOrWhiteSpace($username)) { $username = 'admin' }

    $password = ConvertFrom-SecureStringPlain (Read-Host "  Password" -AsSecureString)

    $parts = @(
        ('Host={0}' -f $hostName),
        ('Port={0}' -f $port),
        ('Database={0}' -f $database),
        ('Username={0}' -f $username),
        ('Password={0}' -f $password)
    )

    # The remote server is reached over TLS with a pinned root (see the README's
    # migration command); offered rather than assumed, because the LAN database is
    # plain and demanding a certificate there would just fail.
    $sslMode = Read-Host "  SSL Mode  [leave empty for none, or e.g. VerifyFull]"
    if (-not [string]::IsNullOrWhiteSpace($sslMode)) {
        $parts += ('SSL Mode={0}' -f $sslMode.Trim())
        $rootCert = Read-Host "  Root Certificate path [optional]"
        if (-not [string]::IsNullOrWhiteSpace($rootCert)) {
            $parts += ('Root Certificate={0}' -f $rootCert.Trim())
        }
    }

    return ($parts -join ';')
}

# Offers the dev connection string or a hand-typed one. Returns the chosen string.
function Select-TargetConnection {
    $localJson = Read-JsonFile $LocalSettings
    $devConn   = [string](Get-JsonPath $localJson 'ConnectionStrings.CarPos')
    $devParts  = ConvertFrom-ConnectionString $devConn

    Write-Host ''
    Write-Host 'Which database?' -ForegroundColor Cyan

    $hasDev = -not [string]::IsNullOrWhiteSpace($devConn)
    if ($hasDev) {
        Write-Host ('  [1] dev  -  {0} @ {1}  as {2}' -f `
            (Get-TableValue $devParts 'database'), `
            (Get-TableValue $devParts 'host'), `
            (Get-TableValue $devParts 'username')) -ForegroundColor Gray
        Write-Host '            (from API/CarPosAPI/appsettings.Local.json)' -ForegroundColor DarkGray
    } else {
        Write-Host '  [1] dev  -  unavailable: appsettings.Local.json has no ConnectionStrings:CarPos' -ForegroundColor DarkGray
    }
    Write-Host '  [2] enter host / database / login by hand' -ForegroundColor Gray

    while ($true) {
        $default = if ($hasDev) { '1' } else { '2' }
        $answer = Read-Host ("Choose  (default {0})" -f $default)
        if ([string]::IsNullOrWhiteSpace($answer)) { $answer = $default }

        switch ($answer.Trim()) {
            '1' {
                if ($hasDev) { return $devConn }
                Write-Host '  No dev connection string to use - choose 2.' -ForegroundColor Red
            }
            '2' {
                Write-Host ''
                Write-Host 'Enter the connection (press Enter to accept a shown default):' -ForegroundColor Cyan
                return (Read-ConnectionFromPrompts -Defaults $devParts)
            }
            default { Write-Host '  Choose 1 or 2.' -ForegroundColor Red }
        }
    }
}

# ---------------------------------------------------------------------------
# Selection and confirmation
# ---------------------------------------------------------------------------

# Reads "A" / "N" / a list of numbers and returns the chosen report items.
# $null means the operator chose to change nothing.
function Read-Selection {
    param([object[]]$Items)

    $applicable = @($Items | Where-Object { -not $_.BlockedReason })
    if ($applicable.Count -eq 0) {
        Write-Host ''
        Write-Host 'None of the differences above can be applied automatically - see the reasons given.' -ForegroundColor Yellow
        return $null
    }

    $valid = @($applicable | ForEach-Object { $_.Number })

    while ($true) {
        Write-Host ''
        $answer = Read-Host 'Select what to apply:  [A]ll  [N]one  or numbers e.g. 1,2,4'
        if ([string]::IsNullOrWhiteSpace($answer)) { $answer = 'N' }
        $answer = $answer.Trim()

        if ($answer.ToUpper() -eq 'N') { return $null }
        if ($answer.ToUpper() -eq 'A') { return $applicable }

        $chosen = @()
        $bad = $false
        foreach ($part in $answer.Split(',')) {
            $trimmed = $part.Trim()
            if ($trimmed -eq '') { continue }

            $number = 0
            if (-not [int]::TryParse($trimmed, [ref]$number)) {
                Write-Host ("  '{0}' is not a number." -f $trimmed) -ForegroundColor Red
                $bad = $true
                break
            }
            if ($valid -notcontains $number) {
                Write-Host ("  {0} is not an applicable change in the list above." -f $number) -ForegroundColor Red
                $bad = $true
                break
            }

            $chosen += @($applicable | Where-Object { $_.Number -eq $number })
        }

        if (-not $bad -and $chosen.Count -gt 0) { return $chosen }
        if (-not $bad) { Write-Host '  Nothing selected - type A, N or some numbers.' -ForegroundColor Red }
    }
}

# The gate. A plain change takes y/N; anything that destroys data is re-listed with
# its row count and takes only the literal word APPLY, so the confirmation cannot be
# given by reflex.
function Confirm-Selection {
    param([object[]]$Selected)

    $destructive = @($Selected | Where-Object { $_.IsDataLoss })

    if ($destructive.Count -eq 0) {
        Write-Host ''
        $answer = Read-Host ("Apply {0} change(s)? [y/N]" -f $Selected.Count)
        return ($answer.Trim().ToUpper() -eq 'Y')
    }

    Write-Host ''
    Write-Host 'These selected changes DESTROY DATA:' -ForegroundColor Red
    foreach ($item in $destructive) {
        $rows = if ($null -eq $item.RowCount) { 'row count unavailable - assume data is at stake' } else { ('{0} row(s) affected' -f $item.RowCount) }
        Write-Host ('  [{0}] {1}' -f $item.Number, $item.Description) -ForegroundColor Red
        if ($item.Sql) { Write-Host ('        {0}' -f $item.Sql) -ForegroundColor DarkGray }
        Write-Host ('        {0}' -f $rows) -ForegroundColor Red
    }

    Write-Host ''
    Write-Host 'This cannot be undone by re-running anything.' -ForegroundColor Red
    $answer = Read-Host 'Type APPLY in full to go ahead, or anything else to cancel'
    return ($answer.Trim() -ceq 'APPLY')
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

if ($MyInvocation.InvocationName -eq '.') { return }

try {
    Write-Host ''
    Write-Host 'CarPos database schema sync' -ForegroundColor Green
    Write-Host ('Source: {0}' -f (Join-Path $RepoRoot 'API\CarPosAPI')) -ForegroundColor DarkGray

    if (-not (Test-Path -LiteralPath $ApiProject)) {
        throw ("API project not found: {0}" -f $ApiProject)
    }

    $targetConnection = if ([string]::IsNullOrWhiteSpace($Connection)) {
        Select-TargetConnection
    } else {
        $Connection
    }

    $parts = ConvertFrom-ConnectionString $targetConnection
    $user  = [string](Get-TableValue $parts 'username')

    Write-Host ''
    Write-Host ('Target: {0} @ {1}  as {2}' -f `
        (Get-TableValue $parts 'database'), (Get-TableValue $parts 'host'), $user) -ForegroundColor Cyan

    # Worth saying before anything is attempted: the deployed API's own login is
    # deliberately DML-only, so a sync run as that role fails on the first ALTER
    # with a bare "permission denied" that reads like a bug rather than a choice.
    if ($DdlCapableUsers -notcontains $user) {
        Write-Warning ("'{0}' is not one of the DDL-capable logins ({1}). Container/Postgres/README.md gives the BE role DML rights only, so changing the schema as it will fail. Migrations are applied as admin." -f $user, ($DdlCapableUsers -join ', '))
    }

    New-Item -ItemType Directory -Force -Path $ScratchDir | Out-Null

    if ($SkipBuild -and (Test-Path -LiteralPath $ToolDll)) {
        Write-Host 'Reusing the previous build (-SkipBuild).' -ForegroundColor DarkGray
    } else {
        Write-Host ''
        Write-Host 'Building the schema tool...' -ForegroundColor DarkGray
        # BaseOutputPath keeps this away from API\CarPosAPI\bin, which the debugger
        # holds open whenever the API is running - the usual state when you reach
        # for this menu.
        dotnet build $ApiProject -p:BaseOutputPath="$BuildOutput\" --nologo -v q | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Build failed - fix the API build first.' }
    }

    if (-not (Test-Path -LiteralPath $ToolDll)) {
        throw ("Built, but the tool was not where expected: {0}" -f $ToolDll)
    }

    # --- Report -----------------------------------------------------------
    if (Test-Path -LiteralPath $SummaryPath) { Remove-Item -LiteralPath $SummaryPath -Force }

    dotnet $ToolDll schema-sync report --connection $targetConnection --summary $SummaryPath
    $reportExit = $LASTEXITCODE

    if ($reportExit -eq 1) { throw 'The comparison failed - see the error above.' }
    if ($reportExit -eq 0) {
        Write-Host ''
        Write-Host 'Nothing to do.' -ForegroundColor Green
        exit 0
    }

    if (-not (Test-Path -LiteralPath $SummaryPath)) {
        throw 'The comparison reported differences but wrote no summary - cannot offer a safe selection.'
    }

    $report = Get-Content -LiteralPath $SummaryPath -Raw | ConvertFrom-Json
    # PowerShell unwraps a one-element JSON array, so force it back to a collection
    # before anything counts or iterates it.
    $items = @($report.Items)

    # --- Select + confirm -------------------------------------------------
    $selected = Read-Selection -Items $items
    if ($null -eq $selected -or $selected.Count -eq 0) {
        Write-Host ''
        Write-Host 'Nothing selected - the database was not changed.' -ForegroundColor Yellow
        exit 0
    }

    if (-not (Confirm-Selection -Selected $selected)) {
        Write-Host ''
        Write-Host 'Cancelled - the database was not changed.' -ForegroundColor Yellow
        exit 0
    }

    # --- Apply ------------------------------------------------------------
    $numbers = ($selected | ForEach-Object { $_.Number }) -join ','
    $allowDataLoss = @($selected | Where-Object { $_.IsDataLoss }).Count -gt 0

    Write-Host ''
    Write-Host ('Applying change(s) {0}...' -f $numbers) -ForegroundColor Cyan

    # --verify hands the tool the very report that was confirmed above. It rebuilds
    # the plan before applying (so it never acts on a stale picture), and that check
    # is what stops the rebuilt numbers meaning something the operator never agreed
    # to if the database moved in between.
    if ($allowDataLoss) {
        dotnet $ToolDll schema-sync apply --connection $targetConnection --select $numbers --verify $SummaryPath --allow-data-loss
    } else {
        dotnet $ToolDll schema-sync apply --connection $targetConnection --select $numbers --verify $SummaryPath
    }

    if ($LASTEXITCODE -ne 0) { throw 'Applying the changes failed - see the error above.' }

    Write-Host ''
    Write-Host 'Done.' -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ''
    Write-Host ('ERROR: ' + $_.Exception.Message) -ForegroundColor Red
    exit 1
}

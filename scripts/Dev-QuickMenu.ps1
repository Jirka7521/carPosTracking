<#
.SYNOPSIS
    The Ctrl+Shift+M menu: build the Docker deployment output, or sync a database's
    schema with the source code.

.DESCRIPTION
    Bound to Ctrl+Shift+M through the "Dev Quick Menu" task in .vscode/tasks.json.
    It only chooses and dispatches - both chores live in their own scripts:

      [1] scripts/Publish-Deployment.ps1   the Application/ bundle for the Pi
      [2] scripts/Sync-DatabaseSchema.ps1  compare + apply the database schema

    Each choice runs as a CHILD powershell.exe rather than being dot-sourced or
    called in-process. Publish-Deployment.ps1 ends in `exit 0` / `exit 1`, and a
    child process is what keeps those exit codes meaningful without them tearing
    down the menu itself. It also isolates the two scripts' StrictMode and
    ErrorActionPreference from each other.

    After a chore finishes the menu comes back, so both can be done in one press.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

# The same PowerShell that is hosting this script, so a child behaves identically.
# MainModule is unavailable in some hosts; powershell.exe from PATH is the fallback.
$PowerShellExe = try {
    [System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
} catch {
    'powershell.exe'
}

$Choices = @(
    @{
        Key         = '1'
        Title       = 'Build deployment output for Docker'
        Description = 'Collect every secret, then write the self-contained Application/ bundle to copy to the Pi.'
        Script      = Join-Path $PSScriptRoot 'Publish-Deployment.ps1'
    },
    @{
        Key         = '2'
        Title       = 'Sync database schema from source'
        Description = 'Compare a database with the schema the source describes, then apply only what you pick.'
        Script      = Join-Path $PSScriptRoot 'Sync-DatabaseSchema.ps1'
    }
)

# Runs one chore and reports how it ended. Never throws: a failing chore must return
# the operator to the menu, not kill it.
function Invoke-Choice {
    param([hashtable]$Choice)

    if (-not (Test-Path -LiteralPath $Choice.Script)) {
        Write-Host ''
        Write-Host ('Script not found: {0}' -f $Choice.Script) -ForegroundColor Red
        return
    }

    Write-Host ''
    Write-Host ('--- {0} ---' -f $Choice.Title) -ForegroundColor Cyan

    & $PowerShellExe -NoProfile -ExecutionPolicy Bypass -File $Choice.Script
    $code = $LASTEXITCODE

    Write-Host ''
    if ($code -eq 0) {
        Write-Host ('--- {0}: finished ---' -f $Choice.Title) -ForegroundColor Green
    } else {
        Write-Host ('--- {0}: exited with code {1} ---' -f $Choice.Title, $code) -ForegroundColor Yellow
    }
}

Write-Host ''
Write-Host 'CarPos dev quick menu' -ForegroundColor Green
Write-Host ('Repo: {0}' -f $RepoRoot) -ForegroundColor DarkGray

while ($true) {
    Write-Host ''
    foreach ($choice in $Choices) {
        Write-Host ('  [{0}] {1}' -f $choice.Key, $choice.Title) -ForegroundColor Cyan
        Write-Host ('      {0}' -f $choice.Description) -ForegroundColor DarkGray
    }
    Write-Host '  [Q] Quit' -ForegroundColor Cyan

    Write-Host ''
    $answer = Read-Host 'Choose'
    if ($null -eq $answer) { $answer = '' }
    $answer = $answer.Trim()

    if ($answer.ToUpper() -eq 'Q' -or $answer -eq '') {
        Write-Host 'Bye.' -ForegroundColor DarkGray
        exit 0
    }

    $selected = $Choices | Where-Object { $_.Key -eq $answer } | Select-Object -First 1
    if ($null -eq $selected) {
        Write-Host ('  No option "{0}".' -f $answer) -ForegroundColor Red
        continue
    }

    Invoke-Choice -Choice $selected
}

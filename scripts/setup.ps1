# One-command onboarding for school-search (Windows PowerShell 5.1 or PowerShell 7).
# Numbered steps; mirror of scripts/setup.sh: keep them in step.
#
#   .\scripts\setup.ps1            run every step
#   .\scripts\setup.ps1 -Check     list missing tools by name and exit (no changes made)
#   .\scripts\setup.ps1 --check    the same, spelled as in setup.sh
#
# Exit status: 0 when every required tool is present; 1 when one is missing.

[CmdletBinding()]
param(
    [switch]$Check,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Remaining
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Remaining -contains '--check') { $Check = $true }

$Root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $Root

$AppHost = 'src/SchoolSearch.AppHost/SchoolSearch.AppHost.csproj'

function Say([string]$Text)  { Write-Host "`n== $Text" }
function Ok([string]$Text)   { Write-Host "  [ok]      $Text" -ForegroundColor Green }
function Miss([string]$Text) { Write-Host "  [missing] $Text" -ForegroundColor Yellow }
function Skip([string]$Text) { Write-Host "  [skip]    $Text" }

function Test-Tool([string]$Command) {
    return [bool](Get-Command $Command -ErrorAction SilentlyContinue)
}

function Read-EnvValue([string]$Key) {
    if (-not (Test-Path '.env')) { return '' }
    $line = Get-Content '.env' | Where-Object { $_ -match "^$Key=" } | Select-Object -Last 1
    if (-not $line) { return '' }
    return ($line -replace "^$Key=", '')
}

$script:RequiredMissing = @()
$script:OptionalMissing = @()

function Test-ToolEntry([string]$Name, [string]$Command, [bool]$Required, [string]$Hint) {
    if (Test-Tool $Command) {
        Ok $Name
    } else {
        Miss "$Name  ->  $Hint"
        if ($Required) { $script:RequiredMissing += $Name } else { $script:OptionalMissing += $Name }
    }
}

# Step 1: prerequisites, with install pointers. Also what -Check runs.
function Step-Prerequisites {
    Say '1/5 Prerequisites'
    Test-ToolEntry 'git'                       'git'      $true  'https://git-scm.com/downloads'
    Test-ToolEntry '.NET 10 SDK (dotnet)'      'dotnet'   $true  'https://dotnet.microsoft.com/download/dotnet/10.0'
    if ((Test-Tool 'dotnet') -and -not ((& dotnet --version) -like '10.*')) {
        Miss 'dotnet is present but not a 10.x SDK; global.json pins the band  ->  https://dotnet.microsoft.com/download/dotnet/10.0'
        $script:RequiredMissing += '.NET 10 SDK (wrong version)'
    }
    Test-ToolEntry 'Node.js 20+ (node)'        'node'     $true  'https://nodejs.org/en/download'
    Test-ToolEntry 'pnpm (pnpm)'               'pnpm'     $true  "run 'corepack enable' (ships with Node) or https://pnpm.io/installation"
    Test-ToolEntry 'gitleaks 8.19+'            'gitleaks' $true  'https://github.com/gitleaks/gitleaks#installing (the pre-commit hook needs it)'
    Test-ToolEntry 'container engine (docker)' 'docker'   $false 'https://docs.docker.com/get-docker/ (needed only for image builds)'
    Test-ToolEntry 'GitHub CLI (gh)'           'gh'       $false 'https://cli.github.com (optional, for PRs and workflow runs)'
}

function Write-Summary {
    Write-Host ''
    if ($script:RequiredMissing.Count -gt 0) {
        Write-Host ('Required tools missing: ' + ($script:RequiredMissing -join '; '))
    } else {
        Write-Host 'Required tools: all present.'
    }
    if ($script:OptionalMissing.Count -gt 0) {
        Write-Host ('Optional tools missing: ' + ($script:OptionalMissing -join '; '))
    }
}

# Step 2: the pre-commit secret scan. Fails closed without gitleaks.
function Step-Hooks {
    Say '2/5 Secret-scan pre-commit hook'
    & git rev-parse --git-dir *> $null
    if ($LASTEXITCODE -ne 0) { Skip 'not a git checkout; hook not installed'; return }
    & git config core.hooksPath scripts/hooks
    Ok 'core.hooksPath = scripts/hooks (every commit runs scripts/scan-secrets.sh --staged)'
}

# Step 3: the local .env, gitignored, created from the committed template.
function Step-Env {
    Say '3/5 Local .env (gitignored)'
    if (Test-Path '.env') {
        Ok '.env exists; left unchanged'
    } else {
        Copy-Item 'secrets.env.example' '.env'
        Ok 'created .env from secrets.env.example; every value is optional'
    }
}

# Step 4: optional integrations, each labelled with what it enables.
function Step-Optional {
    Say '4/5 Optional integrations'
    $key = Read-EnvValue 'ANTHROPIC_API_KEY'
    if (-not $key -and [Environment]::UserInteractive) {
        $secure = Read-Host 'ANTHROPIC_API_KEY (optional, needed for AI features; Enter to skip)' -AsSecureString
        $key = [System.Net.NetworkCredential]::new('', $secure).Password
    }
    if (-not $key) {
        Skip 'ANTHROPIC_API_KEY not set: AI features disabled, /health lists ai as disabled'
        return
    }
    if ((Test-Path $AppHost) -and (Test-Tool 'dotnet')) {
        & dotnet user-secrets set 'Parameters:anthropic-api-key' $key --project $AppHost | Out-Null
        Ok 'stored ANTHROPIC_API_KEY in user-secrets'
    } else {
        Skip 'ANTHROPIC_API_KEY read but no AppHost or dotnet yet; run setup again once the solution is in place'
    }
}

# Step 5: scan the tree once, then say what to run next.
function Step-ScanAndNext {
    Say '5/5 Scan the working tree and next steps'
    if (Test-Tool 'gitleaks') {
        & gitleaks dir --redact --no-banner --config .gitleaks.toml . *> $null
        if ($LASTEXITCODE -eq 0) { Ok 'gitleaks: no secrets in the working tree' }
        else { Miss 'gitleaks reported findings; run gitleaks dir --redact --config .gitleaks.toml . and fix them before committing' }
    } else {
        Miss 'gitleaks not installed; the tree scan did not run'
    }
    Write-Host ''
    Write-Host "Next: dotnet run --project $AppHost"
    Write-Host '      then open the URL the AppHost dashboard prints, and GET /health on the API.'
    Write-Host 'Variables and their tiers: scripts/README.md'
}

Step-Prerequisites
if ($Check) {
    Write-Summary
    if ($script:RequiredMissing.Count -gt 0) { exit 1 } else { exit 0 }
}

Step-Hooks
Step-Env
Step-Optional
Step-ScanAndNext
Write-Summary
if ($script:RequiredMissing.Count -gt 0) { exit 1 } else { exit 0 }

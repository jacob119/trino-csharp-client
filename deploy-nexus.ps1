# deploy-nexus.ps1 — Build and push Trino C# NuGet packages to a Nexus repository.
#
# Usage:
#   .\deploy-nexus.ps1 push     Build Release packages and push to Nexus
#   .\deploy-nexus.ps1 source   Register Nexus as a local NuGet source (for dotnet restore)
#   .\deploy-nexus.ps1 list     List packages that would be pushed (dry-run)
#
# Required environment variables for push / source:
#   NEXUS_URL        Base URL of your Nexus instance  e.g. http://nexus.company.com:8081
#   NEXUS_REPO       Hosted repository name            e.g. nuget-hosted
#   NEXUS_USERNAME   Nexus username
#   NEXUS_PASSWORD   Nexus password
#
# Optional environment variables:
#   VERSION          Override package version          e.g. 1.2.3.0  (default: today yyyy.M.d.1)
#   BUILD_CONFIG     Release (default) or Debug
#   SOURCE_NAME      NuGet source alias                (default: nexus-trino)
#   SKIP_TESTS       Set to 1 to skip unit tests before push
#
# Example:
#   $env:NEXUS_URL      = "http://nexus.company.com:8081"
#   $env:NEXUS_REPO     = "nuget-hosted"
#   $env:NEXUS_USERNAME = "admin"
#   $env:NEXUS_PASSWORD = "yourpassword"
#   .\deploy-nexus.ps1 push

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet("push", "source", "list")]
    [string]$Command = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Paths ─────────────────────────────────────────────────────────────────────
$ScriptDir    = Split-Path -Parent $MyInvocation.MyCommand.Path
$SolutionDir  = Join-Path $ScriptDir "trino-csharp"
$SolutionFile = Join-Path $SolutionDir "TrinoDriver.sln"

# Projects to publish (relative to SolutionDir)
$PublishProjects = @(
    "Trino.Client\Trino.Client.csproj",
    "Trino.Client.Auth\Trino.Client.Auth.csproj",
    "Trino.Data.ADO\Trino.Data.ADO.csproj"
)

# ── Config from environment ───────────────────────────────────────────────────
$NexusUrl     = $env:NEXUS_URL
$NexusRepo    = $env:NEXUS_REPO
$NexusUser    = $env:NEXUS_USERNAME
$NexusPass    = $env:NEXUS_PASSWORD
$BuildConfig  = if ($env:BUILD_CONFIG)  { $env:BUILD_CONFIG  } else { "Release" }
$SourceName   = if ($env:SOURCE_NAME)   { $env:SOURCE_NAME   } else { "nexus-trino" }
$SkipTests    = $env:SKIP_TESTS -eq "1"
$Version      = if ($env:VERSION) {
    $env:VERSION
} else {
    $now = Get-Date
    "$($now.Year).$($now.Month).$($now.Day).1"
}

# ── Helpers ───────────────────────────────────────────────────────────────────
function Info  ([string]$msg) { Write-Host "  [INFO]  $msg" -ForegroundColor Cyan }
function Ok    ([string]$msg) { Write-Host "  [ OK ]  $msg" -ForegroundColor Green }
function Err   ([string]$msg) { Write-Host "  [ERR]   $msg" -ForegroundColor Red }

function Assert-Env {
    param([string[]]$Names)
    $missing = $Names | Where-Object { -not (Get-Item "Env:\$_" -ErrorAction SilentlyContinue) }
    if ($missing) {
        Err "Missing required environment variables: $($missing -join ', ')"
        Err "Set them with:  `$env:VARIABLE_NAME = 'value'"
        exit 1
    }
}

function Get-PushUrl   { "$($NexusUrl.TrimEnd('/'))/repository/$NexusRepo/" }
function Get-GroupUrl  {
    $group = $NexusRepo -replace "-hosted$", "-group"
    "$($NexusUrl.TrimEnd('/'))/repository/$group/"
}

function Get-PackageId ([string]$ProjPath) {
    $xml = [xml](Get-Content $ProjPath)
    $id  = $xml.Project.PropertyGroup.PackageId | Where-Object { $_ -and $_ -notmatch '^\$\(' } | Select-Object -First 1
    if (-not $id) { $id = [System.IO.Path]::GetFileNameWithoutExtension($ProjPath) }
    return $id
}

function Invoke-Cmd {
    param([string]$Exe, [string[]]$Args)
    & $Exe @Args
    if ($LASTEXITCODE -ne 0) {
        throw "'$Exe $($Args -join ' ')' exited with code $LASTEXITCODE"
    }
}

# ── Commands ──────────────────────────────────────────────────────────────────

function Invoke-List {
    Info "Packages that would be pushed (dry-run):"
    Write-Host ""
    foreach ($proj in $PublishProjects) {
        $path = Join-Path $SolutionDir $proj
        $id   = Get-PackageId $path
        Write-Host "    * $id  ($proj)"
    }
    Write-Host ""
    $urlDisplay = if ($NexusUrl -and $NexusRepo) { Get-PushUrl } else { "(set NEXUS_URL + NEXUS_REPO)" }
    Info "Push URL : $urlDisplay"
    Info "Version  : $Version"
    Info "Config   : $BuildConfig"
}

function Invoke-Push {
    Assert-Env @("NEXUS_URL", "NEXUS_REPO", "NEXUS_USERNAME", "NEXUS_PASSWORD")

    $pushUrl    = Get-PushUrl
    $outputDir  = Join-Path $SolutionDir "artifacts"

    Write-Host ""
    Info "=== Trino C# Nexus Deployment ==="
    Info "URL     : $pushUrl"
    Info "Version : $Version"
    Info "Config  : $BuildConfig"
    Write-Host ""

    # 1. Run unit tests
    if (-not $SkipTests) {
        Info "Running unit tests..."
        $testProj = Join-Path $SolutionDir "Trino.Client.Test\Trino.Client.Test.csproj"
        Invoke-Cmd dotnet @("test", $testProj, "--configuration", $BuildConfig, "--verbosity", "minimal")
        Ok "Tests passed"
    } else {
        Info "Skipping tests (SKIP_TESTS=1)"
    }

    # 2. Clean artifacts directory
    if (Test-Path $outputDir) { Remove-Item $outputDir -Recurse -Force }
    New-Item -ItemType Directory -Path $outputDir | Out-Null

    # 3. Pack each project
    Info "Building and packing packages..."
    foreach ($proj in $PublishProjects) {
        $projPath = Join-Path $SolutionDir $proj
        $projName = [System.IO.Path]::GetFileNameWithoutExtension($projPath)

        Info "  Packing $projName..."
        Invoke-Cmd dotnet @(
            "pack", $projPath,
            "--configuration", $BuildConfig,
            "--output", $outputDir,
            "/p:Version=$Version",
            "--verbosity", "minimal"
        )
        Ok "  $projName packed"
    }

    # 4. Push each .nupkg
    Info "Pushing packages to Nexus..."
    $apiKey = "${NexusUser}:${NexusPass}"

    $nupkgs = Get-ChildItem -Path $outputDir -Filter "*.nupkg"
    if (-not $nupkgs) { throw "No .nupkg files found in $outputDir" }

    foreach ($nupkg in $nupkgs) {
        Info "  Pushing $($nupkg.Name)..."
        Invoke-Cmd dotnet @(
            "nuget", "push", $nupkg.FullName,
            "--source", $pushUrl,
            "--api-key", $apiKey,
            "--skip-duplicate"
        )
        Ok "  $($nupkg.Name) pushed"
    }

    Write-Host ""
    Ok "=== All packages pushed successfully ==="
    Info "Packages available at: $pushUrl"
    Write-Host ""
    Info "To consume from another project, run:"
    Write-Host "    .\deploy-nexus.ps1 source"
    Write-Host "  or copy nuget.config.example to nuget.config and fill in your Nexus URL."
}

function Invoke-Source {
    Assert-Env @("NEXUS_URL", "NEXUS_REPO", "NEXUS_USERNAME", "NEXUS_PASSWORD")

    $sourceUrl = Get-GroupUrl

    Info "Registering NuGet source: $SourceName"
    Info "URL: $sourceUrl"
    Write-Host ""

    # Remove existing entry if present
    $existing = & dotnet nuget list source 2>$null | Select-String $SourceName
    if ($existing) {
        Info "Source '$SourceName' already exists — removing old entry..."
        Invoke-Cmd dotnet @("nuget", "remove", "source", $SourceName)
    }

    Invoke-Cmd dotnet @(
        "nuget", "add", "source", $sourceUrl,
        "--name", $SourceName,
        "--username", $NexusUser,
        "--password", $NexusPass,
        "--store-password-in-clear-text"
    )

    Ok "Source '$SourceName' registered."
    Write-Host ""
    Info "You can now restore packages with:"
    Write-Host "    dotnet restore --source $SourceName"
    Write-Host ""
    Info "Or copy nuget.config.example to nuget.config and fill in your Nexus URL."
}

# ── Entry point ───────────────────────────────────────────────────────────────
if (-not $Command) {
    Write-Host "Usage: .\deploy-nexus.ps1 {push|source|list}"
    Write-Host ""
    Write-Host "  push    Build Release packages and push to Nexus"
    Write-Host "  source  Register Nexus as a local NuGet source for dotnet restore"
    Write-Host "  list    List packages that would be pushed (no network calls)"
    Write-Host ""
    Write-Host "Environment variables:"
    Write-Host "  NEXUS_URL        e.g. http://nexus.company.com:8081"
    Write-Host "  NEXUS_REPO       e.g. nuget-hosted"
    Write-Host "  NEXUS_USERNAME   Nexus login"
    Write-Host "  NEXUS_PASSWORD   Nexus password"
    Write-Host "  VERSION          Package version override (default: yyyy.M.d.1)"
    Write-Host "  BUILD_CONFIG     Release (default) or Debug"
    Write-Host "  SKIP_TESTS       Set to 1 to skip unit tests before push"
    exit 1
}

switch ($Command) {
    "push"   { Invoke-Push }
    "source" { Invoke-Source }
    "list"   { Invoke-List }
}

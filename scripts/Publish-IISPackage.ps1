[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$SolutionPath = ".\QuizAPI.sln",
    [string]$ProjectPath = ".\QuizAPI.csproj",
    [string]$TestProjectPath = ".\QuizAPI.Tests\QuizAPI.Tests.csproj",
    [string]$PublishRoot = ".\publish",
    [string]$BundleRoot = ".\DeploymentBundle",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Resolve-AbsolutePath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

function Invoke-NativeCommand {
    param(
        [string]$FilePath,
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE`: $FilePath $($Arguments -join ' ')"
    }
}

$projectFullPath = Resolve-AbsolutePath -Path $ProjectPath
$solutionFullPath = Resolve-AbsolutePath -Path $SolutionPath
$testProjectFullPath = Resolve-AbsolutePath -Path $TestProjectPath
$publishRootFullPath = Resolve-AbsolutePath -Path $PublishRoot
$bundleRootFullPath = Resolve-AbsolutePath -Path $BundleRoot

if (-not (Test-Path $projectFullPath)) {
    throw "Project path not found: $projectFullPath"
}

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$publishPath = Join-Path $publishRootFullPath $timestamp
$zipName = "QuizAPI_IIS_Production_$timestamp.zip"
$zipPath = Join-Path $bundleRootFullPath $zipName

New-Item -ItemType Directory -Path $publishRootFullPath -Force | Out-Null
New-Item -ItemType Directory -Path $bundleRootFullPath -Force | Out-Null

Write-Step "Restoring packages"
Invoke-NativeCommand -FilePath "dotnet" -Arguments @("restore", $projectFullPath)

Write-Step "Building application project"
Invoke-NativeCommand -FilePath "dotnet" -Arguments @("build", $projectFullPath, "-c", $Configuration, "--no-restore")

if (-not $SkipTests -and (Test-Path $testProjectFullPath)) {
    Write-Step "Running tests"
    Invoke-NativeCommand -FilePath "dotnet" -Arguments @("test", $testProjectFullPath, "-c", $Configuration)
}
elseif (-not $SkipTests) {
    Write-Step "Skipping tests"
    Write-Host "Test project not found: $testProjectFullPath"
}

Write-Step "Publishing IIS package contents"
if (Test-Path $publishPath) {
    Remove-Item -LiteralPath $publishPath -Recurse -Force
}

Invoke-NativeCommand -FilePath "dotnet" -Arguments @("publish", $projectFullPath, "-c", $Configuration, "--no-build", "-o", $publishPath)

Write-Step "Creating deployment zip"
if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $publishPath "*") -DestinationPath $zipPath -CompressionLevel Optimal

Write-Step "Package ready"
Write-Host "Publish folder: $publishPath"
Write-Host "Deployment zip: $zipPath"

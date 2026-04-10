[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$SolutionPath = ".\QuizAPI.sln",
    [string]$ProjectPath = ".\QuizAPI.csproj",
    [string]$PublishRoot = ".\publish",
    [string]$BundleRoot = ".\DeploymentBundle",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Resolve-AbsolutePath {
    param([string]$Path)
    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Path))
}

$projectFullPath = Resolve-AbsolutePath -Path $ProjectPath
$solutionFullPath = Resolve-AbsolutePath -Path $SolutionPath
$publishRootFullPath = Resolve-AbsolutePath -Path $PublishRoot
$bundleRootFullPath = Resolve-AbsolutePath -Path $BundleRoot

if (-not (Test-Path $projectFullPath)) {
    throw "Project path not found: $projectFullPath"
}

if (-not (Test-Path $solutionFullPath)) {
    throw "Solution path not found: $solutionFullPath"
}

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$publishPath = Join-Path $publishRootFullPath $timestamp
$zipName = "QuizAPI_IIS_Production_$timestamp.zip"
$zipPath = Join-Path $bundleRootFullPath $zipName

New-Item -ItemType Directory -Path $publishRootFullPath -Force | Out-Null
New-Item -ItemType Directory -Path $bundleRootFullPath -Force | Out-Null

Write-Step "Restoring packages"
dotnet restore $solutionFullPath

Write-Step "Building solution"
dotnet build $solutionFullPath -c $Configuration --no-restore

if (-not $SkipTests) {
    Write-Step "Running tests"
    dotnet test $solutionFullPath -c $Configuration --no-build
}

Write-Step "Publishing IIS package contents"
if (Test-Path $publishPath) {
    Remove-Item -LiteralPath $publishPath -Recurse -Force
}

dotnet publish $projectFullPath -c $Configuration --no-build -o $publishPath

Write-Step "Creating deployment zip"
if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $publishPath "*") -DestinationPath $zipPath -CompressionLevel Optimal

Write-Step "Package ready"
Write-Host "Publish folder: $publishPath"
Write-Host "Deployment zip: $zipPath"

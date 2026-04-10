#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$ZipPath = "C:\Deploy\DeploymentBundle\QuizAPI_IIS_Production_20260318_182000.zip",
    [string]$SiteName = "QuizAPI",
    [string]$SitePath = "C:\sites\QuizAPI\current",
    [string]$HostName = "oumwqapptst02.oumed.net",
    [string]$AppPoolName = "QuizAppPool",
    [ValidateSet("http", "https")]
    [string]$Protocol = "https",
    [int]$Port = 443,
    [string]$ConnectionString = "Server=.\SQLEXPRESS;Database=QuizDB;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=True;",
    [string]$JwtIssuer = "QuizAPI",
    [string]$JwtAudience = "QuizAPIUsers",
    [string]$JwtKey,
    [int]$JwtAccessTokenMinutes = 60,
    [string]$CertificateThumbprint = "",
    [switch]$UseRootHttpsBinding,
    [switch]$RunMigrations
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Ensure-Directory {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Set-IisAspNetCoreEnvVar {
    param(
        [string]$Location,
        [string]$Name,
        [string]$Value
    )

    $filterBase = "system.webServer/aspNetCore/environmentVariables"
    $itemFilter = "$filterBase/add[@name='$Name']"
    $existing = Get-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Location $Location -Filter $itemFilter -Name "value" -ErrorAction SilentlyContinue

    if ($null -ne $existing) {
        Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Location $Location -Filter $itemFilter -Name "value" -Value $Value | Out-Null
    }
    else {
        Add-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Location $Location -Filter $filterBase -Name "." -Value @{ name = $Name; value = $Value } | Out-Null
    }
}

if ([string]::IsNullOrWhiteSpace($JwtKey)) {
    throw "JwtKey is required. Generate one first and pass it with -JwtKey."
}

if ($JwtKey.Length -lt 32) {
    throw "JwtKey must be at least 32 characters."
}

if (-not (Test-Path $ZipPath)) {
    throw "ZipPath not found: $ZipPath"
}

if ($Port -lt 1 -or $Port -gt 65535) {
    throw "Port must be between 1 and 65535."
}

if ($Protocol -eq "https" -and [string]::IsNullOrWhiteSpace($HostName)) {
    throw "HostName is required when using HTTPS."
}

$effectiveHostName = if ([string]::IsNullOrWhiteSpace($HostName)) { "localhost" } else { $HostName }

Import-Module WebAdministration

Write-Step "Ensuring deployment path exists"
Ensure-Directory -Path $SitePath

Write-Step "Expanding deployment package"
Expand-Archive -Path $ZipPath -DestinationPath $SitePath -Force

Write-Step "Ensuring runtime folders exist"
@(
    "$SitePath\App_Data",
    "$SitePath\App_Data\keys",
    "$SitePath\App_Data\uploads",
    "$SitePath\wwwroot\uploads",
    "$SitePath\logs"
) | ForEach-Object { Ensure-Directory -Path $_ }

Write-Step "Ensuring IIS app pool exists and is configured"
if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) {
    New-WebAppPool -Name $AppPoolName | Out-Null
}

Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedRuntimeVersion -Value ""
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedPipelineMode -Value "Integrated"
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.identityType -Value "ApplicationPoolIdentity"
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name autoStart -Value $true

Write-Step "Ensuring dedicated IIS site exists at root"
if (-not (Test-Path "IIS:\Sites\$SiteName")) {
    New-Website -Name $SiteName -PhysicalPath $SitePath -ApplicationPool $AppPoolName -Port $Port -Protocol $Protocol -HostHeader $HostName | Out-Null
}
else {
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $SitePath
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $AppPoolName

    $existingBindings = Get-WebBinding -Name $SiteName
    foreach ($binding in $existingBindings) {
        $bindingParts = $binding.bindingInformation.Split(":")
        $bindingPort = [int]$bindingParts[1]
        $bindingHostHeader = if ($bindingParts.Count -gt 2) { $bindingParts[2] } else { "" }
        Remove-WebBinding -Name $SiteName -Protocol $binding.protocol -Port $bindingPort -HostHeader $bindingHostHeader
    }

    New-WebBinding -Name $SiteName -Protocol $Protocol -Port $Port -HostHeader $HostName | Out-Null
}

Write-Step "Setting IIS ASP.NET Core environment variables at '$SiteName'"
Set-IisAspNetCoreEnvVar -Location $SiteName -Name "ASPNETCORE_ENVIRONMENT" -Value "Production"
Set-IisAspNetCoreEnvVar -Location $SiteName -Name "ConnectionStrings__DefaultConnection" -Value $ConnectionString
Set-IisAspNetCoreEnvVar -Location $SiteName -Name "Jwt__Issuer" -Value $JwtIssuer
Set-IisAspNetCoreEnvVar -Location $SiteName -Name "Jwt__Audience" -Value $JwtAudience
Set-IisAspNetCoreEnvVar -Location $SiteName -Name "Jwt__Key" -Value $JwtKey
Set-IisAspNetCoreEnvVar -Location $SiteName -Name "Jwt__AccessTokenMinutes" -Value $JwtAccessTokenMinutes.ToString()
Set-IisAspNetCoreEnvVar -Location $SiteName -Name "Cors__AllowedOrigins__0" -Value ("$Protocol://" + $effectiveHostName + $(if (($Protocol -eq "http" -and $Port -ne 80) -or ($Protocol -eq "https" -and $Port -ne 443)) { ":" + $Port } else { "" }))

if ($Protocol -eq "https" -and -not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    Write-Step "Ensuring HTTPS certificate binding exists for '$HostName'"
    $bindingPath = if ($UseRootHttpsBinding) {
        "IIS:\SslBindings\0.0.0.0!$Port"
    }
    else {
        "IIS:\SslBindings\0.0.0.0!$Port!$HostName"
    }

    if (Test-Path $bindingPath) {
        Remove-Item $bindingPath -Force
    }

    New-Item $bindingPath -Thumbprint $CertificateThumbprint -SSLFlags $(if ($UseRootHttpsBinding) { 0 } else { 1 }) | Out-Null
}
else {
    if ($Protocol -eq "https") {
        Write-Step "Leaving HTTPS certificate binding unchanged because no thumbprint was supplied"
    }
    else {
        Write-Step "HTTP binding selected; certificate binding skipped"
    }
}

Write-Step "Granting file permissions to the IIS app pool identity"
$appPoolIdentity = "IIS AppPool\${AppPoolName}"
& icacls $SitePath /grant "${appPoolIdentity}:(RX)" /t | Out-Null
& icacls "$SitePath\App_Data" /grant "${appPoolIdentity}:(M)" /t | Out-Null
& icacls "$SitePath\wwwroot\uploads" /grant "${appPoolIdentity}:(M)" /t | Out-Null
& icacls "$SitePath\logs" /grant "${appPoolIdentity}:(M)" /t | Out-Null

if ($RunMigrations) {
    Write-Step "Skipping EF migration execution in published output"
    Write-Warning "Published IIS output does not contain the project file for 'dotnet ef'. Use the restored QuizDB backup or run migrations from the source project."
}
else {
    Write-Step "Database migration step skipped"
}

Write-Step "Restarting IIS app pool and site"
Restart-WebAppPool -Name $AppPoolName
Start-Website -Name $SiteName

Write-Step "Deployment complete"
Write-Host "Site Name: $SiteName"
Write-Host "Application URL: $Protocol://$effectiveHostName$(if (($Protocol -eq 'http' -and $Port -ne 80) -or ($Protocol -eq 'https' -and $Port -ne 443)) { ':' + $Port } else { '' })/"
Write-Host "Physical Path: $SitePath"

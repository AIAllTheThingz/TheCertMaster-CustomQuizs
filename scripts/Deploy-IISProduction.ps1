#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$ZipPath = "C:\Deploy\DeploymentBundle\QuizAPI_IIS_Production_20260318_182000.zip",
    [string]$SiteName = "QuizAPI",
    [string]$SitePath = "C:\sites\QuizAPI\current",
    [string]$HostName = "WIN2K22IIS01",
    [string]$AppPoolName = "QuizAppPool",
    [ValidateSet("http", "https")]
    [string]$Protocol = "http",
    [int]$Port = 80,
    [string]$ConnectionString = "Server=.\SQLEXPRESS;Database=TheCertMasterCorporateDB;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=True;",
    [string]$JwtIssuer = "QuizAPI",
    [string]$JwtAudience = "QuizAPIUsers",
    [string]$JwtKey,
    [int]$JwtAccessTokenMinutes = 60,
    [bool]$EnableHttpsRedirection = $true,
    [string]$BootstrapAdminEmail = "",
    [string]$BootstrapAdminPassword = "",
    [string]$BootstrapAdminFirstName = "",
    [string]$BootstrapAdminLastName = "",
    [bool]$ActiveDirectoryEnabled = $false,
    [string]$ActiveDirectoryDomain = "",
    [string]$ActiveDirectoryContainer = "",
    [string]$ActiveDirectoryNetBiosDomain = "",
    [string]$ActiveDirectoryUserPrincipalSuffix = "",
    [bool]$ActiveDirectoryRequireMappedRole = $false,
    [string]$ActiveDirectoryDefaultRole = "User",
    [string[]]$ActiveDirectoryAdminGroups = @(),
    [string[]]$ActiveDirectoryUserGroups = @(),
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

function Clear-DirectoryContents {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return
    }

    Get-ChildItem -LiteralPath $Path -Force | ForEach-Object {
        Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction Stop
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

function Remove-IisAspNetCoreEnvVarsByPrefix {
    param(
        [string]$Location,
        [string]$Prefix
    )

    $filterBase = "system.webServer/aspNetCore/environmentVariables"
    $existing = Get-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Location $Location -Filter $filterBase -Name "." -ErrorAction SilentlyContinue

    if ($null -eq $existing) {
        return
    }

    foreach ($item in @($existing.Collection)) {
        if ($item -and $item.Attributes["name"] -and $item.Attributes["name"].Value -like "$Prefix*") {
            Remove-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Location $Location -Filter $filterBase -Name "." -AtElement @{ name = $item.Attributes["name"].Value } | Out-Null
        }
    }
}

function Resolve-ConflictingBindings {
    param(
        [string]$TargetSiteName,
        [string]$TargetProtocol,
        [int]$TargetPort,
        [string]$TargetHostHeader
    )

    $normalizedHostHeader = if ([string]::IsNullOrWhiteSpace($TargetHostHeader)) { "" } else { $TargetHostHeader }

    $conflicts = @()

    foreach ($site in Get-Website) {
        if ($site.Name -eq $TargetSiteName) {
            continue
        }

        $bindings = Get-WebBinding -Name $site.Name -ErrorAction SilentlyContinue
        foreach ($binding in @($bindings)) {
            if ($binding.protocol -ne $TargetProtocol) {
                continue
            }

            $bindingParts = $binding.bindingInformation.Split(":")
            $bindingPort = [int]$bindingParts[1]
            $bindingHostHeader = if ($bindingParts.Count -gt 2) { $bindingParts[2] } else { "" }

            if ($bindingPort -eq $TargetPort -and $bindingHostHeader -eq $normalizedHostHeader) {
                $conflicts += [pscustomobject]@{
                    SiteName = $site.Name
                    Protocol = $binding.protocol
                    Port = $bindingPort
                    HostHeader = $bindingHostHeader
                }
            }
        }
    }

    if ($conflicts.Count -eq 0) {
        return
    }

    $canTakeDefaultHttpBinding =
        $TargetProtocol -eq 'http' -and
        $TargetPort -eq 80 -and
        [string]::IsNullOrWhiteSpace($normalizedHostHeader) -and
        $conflicts.Count -eq 1 -and
        $conflicts[0].SiteName -eq 'Default Web Site'

    if ($canTakeDefaultHttpBinding) {
        Write-Warning "Reclaiming the default HTTP binding from 'Default Web Site' so '$TargetSiteName' can own port 80."
        Remove-WebBinding -Name 'Default Web Site' -Protocol 'http' -Port 80 -HostHeader ''

        $defaultSite = Get-Website -Name 'Default Web Site' -ErrorAction SilentlyContinue
        if ($null -ne $defaultSite) {
            $remainingBindings = @(Get-WebBinding -Name 'Default Web Site' -ErrorAction SilentlyContinue)
            if ($remainingBindings.Count -eq 0 -and $defaultSite.State -eq 'Started') {
                Stop-Website -Name 'Default Web Site'
            }
        }

        return
    }

    $conflictSummary = $conflicts | ForEach-Object {
        $hostDisplay = if ([string]::IsNullOrWhiteSpace($_.HostHeader)) { '(no host header)' } else { $_.HostHeader }
        "'$($_.SiteName)' [$($_.Protocol) :$($_.Port) $hostDisplay]"
    }

    throw "Binding conflict detected for $TargetProtocol port $TargetPort host '$normalizedHostHeader'. Resolve these existing IIS bindings before deployment: $($conflictSummary -join ', ')"
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

Write-Step "Stopping IIS site and app pool for deployment"
if (Test-Path "IIS:\Sites\$SiteName") {
    $siteState = (Get-Website -Name $SiteName).State
    if ($siteState -eq "Started") {
        Stop-Website -Name $SiteName
    }
}

if (Test-Path "IIS:\AppPools\$AppPoolName") {
    $appPoolState = (Get-WebAppPoolState -Name $AppPoolName).Value
    if ($appPoolState -eq "Started") {
        Stop-WebAppPool -Name $AppPoolName
    }
}

Write-Step "Ensuring deployment path exists"
Ensure-Directory -Path $SitePath

Write-Step "Expanding deployment package"
$stagingPath = Join-Path ([System.IO.Path]::GetTempPath()) ("QuizAPI_Deploy_" + [System.Guid]::NewGuid().ToString("N"))
Ensure-Directory -Path $stagingPath

try {
    Expand-Archive -LiteralPath $ZipPath -DestinationPath $stagingPath -Force
    Clear-DirectoryContents -Path $SitePath
    Copy-Item -Path (Join-Path $stagingPath '*') -Destination $SitePath -Recurse -Force
}
finally {
    if (Test-Path $stagingPath) {
        Remove-Item -LiteralPath $stagingPath -Recurse -Force -ErrorAction SilentlyContinue
    }
}

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
Resolve-ConflictingBindings -TargetSiteName $SiteName -TargetProtocol $Protocol -TargetPort $Port -TargetHostHeader $HostName

if (-not (Test-Path "IIS:\Sites\$SiteName")) {
    New-Website -Name $SiteName -PhysicalPath $SitePath -ApplicationPool $AppPoolName -Port $Port -HostHeader $HostName | Out-Null

    if ($Protocol -ne "http") {
        Remove-WebBinding -Name $SiteName -Protocol "http" -Port $Port -HostHeader $HostName -ErrorAction SilentlyContinue
        New-WebBinding -Name $SiteName -Protocol $Protocol -Port $Port -HostHeader $HostName | Out-Null
    }
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
Set-IisAspNetCoreEnvVar -Location $SiteName -Name "HttpsRedirection__Enabled" -Value $EnableHttpsRedirection.ToString().ToLowerInvariant()
Set-IisAspNetCoreEnvVar -Location $SiteName -Name "Cors__AllowedOrigins__0" -Value ("${Protocol}://" + $effectiveHostName + $(if (($Protocol -eq "http" -and $Port -ne 80) -or ($Protocol -eq "https" -and $Port -ne 443)) { ":" + $Port } else { "" }))

if (-not [string]::IsNullOrWhiteSpace($BootstrapAdminEmail)) {
    Set-IisAspNetCoreEnvVar -Location $SiteName -Name "BootstrapAdmin__Email" -Value $BootstrapAdminEmail
    Set-IisAspNetCoreEnvVar -Location $SiteName -Name "BootstrapAdmin__Password" -Value $BootstrapAdminPassword

    if (-not [string]::IsNullOrWhiteSpace($BootstrapAdminFirstName)) {
        Set-IisAspNetCoreEnvVar -Location $SiteName -Name "BootstrapAdmin__FirstName" -Value $BootstrapAdminFirstName
    }

    if (-not [string]::IsNullOrWhiteSpace($BootstrapAdminLastName)) {
        Set-IisAspNetCoreEnvVar -Location $SiteName -Name "BootstrapAdmin__LastName" -Value $BootstrapAdminLastName
    }
}

Set-IisAspNetCoreEnvVar -Location $SiteName -Name "ActiveDirectory__Enabled" -Value $ActiveDirectoryEnabled.ToString().ToLowerInvariant()
Set-IisAspNetCoreEnvVar -Location $SiteName -Name "ActiveDirectory__Domain" -Value $ActiveDirectoryDomain
Set-IisAspNetCoreEnvVar -Location $SiteName -Name "ActiveDirectory__Container" -Value $ActiveDirectoryContainer
Set-IisAspNetCoreEnvVar -Location $SiteName -Name "ActiveDirectory__NetBiosDomain" -Value $ActiveDirectoryNetBiosDomain
Set-IisAspNetCoreEnvVar -Location $SiteName -Name "ActiveDirectory__UserPrincipalSuffix" -Value $ActiveDirectoryUserPrincipalSuffix
Set-IisAspNetCoreEnvVar -Location $SiteName -Name "ActiveDirectory__RequireMappedRole" -Value $ActiveDirectoryRequireMappedRole.ToString().ToLowerInvariant()
Set-IisAspNetCoreEnvVar -Location $SiteName -Name "ActiveDirectory__DefaultRole" -Value $ActiveDirectoryDefaultRole

Remove-IisAspNetCoreEnvVarsByPrefix -Location $SiteName -Prefix "ActiveDirectory__AdminGroups__"
for ($i = 0; $i -lt $ActiveDirectoryAdminGroups.Count; $i++) {
    Set-IisAspNetCoreEnvVar -Location $SiteName -Name ("ActiveDirectory__AdminGroups__" + $i) -Value $ActiveDirectoryAdminGroups[$i]
}

Remove-IisAspNetCoreEnvVarsByPrefix -Location $SiteName -Prefix "ActiveDirectory__UserGroups__"
for ($i = 0; $i -lt $ActiveDirectoryUserGroups.Count; $i++) {
    Set-IisAspNetCoreEnvVar -Location $SiteName -Name ("ActiveDirectory__UserGroups__" + $i) -Value $ActiveDirectoryUserGroups[$i]
}

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
    Write-Warning "Published IIS output does not contain the project file for 'dotnet ef'. Use the restored repository seed backup or run migrations from the source project."
}
else {
    Write-Step "Database migration step skipped"
}

Write-Step "Restarting IIS app pool and site"
$appPoolState = (Get-WebAppPoolState -Name $AppPoolName).Value
if ($appPoolState -ne "Started") {
    Start-WebAppPool -Name $AppPoolName
}

$siteState = (Get-Website -Name $SiteName).State
if ($siteState -ne "Started") {
    Start-Website -Name $SiteName
}

Write-Step "Deployment complete"
Write-Host "Site Name: $SiteName"
Write-Host "Application URL: ${Protocol}://$effectiveHostName$(if (($Protocol -eq 'http' -and $Port -ne 80) -or ($Protocol -eq 'https' -and $Port -ne 443)) { ':' + $Port } else { '' })/"
Write-Host "Physical Path: $SitePath"

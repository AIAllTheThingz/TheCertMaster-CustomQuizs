#requires -Version 5.1
#requires -RunAsAdministrator

[CmdletBinding()]
param(
    [string]$SettingsFile = '',
    [string]$DeploymentRoot = 'C:\Deployment',
    [string]$SiteName = 'QuizAPI',
    [string]$AppPoolName = 'QuizAppPool',
    [string]$SitePath = 'C:\sites\QuizAPI\current',
    [ValidateSet('http', 'https')]
    [string]$Protocol = 'http',
    [int]$HttpPort = 80,
    [int]$HttpsPort = 443,
    [string]$HostName = '',
    [string]$CertificateThumbprint = '',
    [string]$SqlInstance = '.\SQLEXPRESS',
    [string]$DatabaseName = 'QuizDB',
    [string]$ConnectionString = '',
    [string]$PublicBaseUrl = '',
    [Nullable[bool]]$EnableHttpsRedirection = $null,
    [string]$JwtKey = '',
    [string]$JwtIssuer = 'QuizAPI',
    [string]$JwtAudience = 'QuizAPIUsers',
    [int]$JwtAccessTokenMinutes = 60,
    [string]$BootstrapAdminEmail = '',
    [string]$BootstrapAdminPassword = '',
    [string]$BootstrapAdminFirstName = '',
    [string]$BootstrapAdminLastName = '',
    [switch]$EnableSmokeTest
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

if (-not [string]::IsNullOrWhiteSpace($SettingsFile)) {
    if (-not (Test-Path -LiteralPath $SettingsFile)) {
        throw "Settings file was not found: $SettingsFile"
    }

    $settings = Import-PowerShellDataFile -Path (Resolve-Path -LiteralPath $SettingsFile).Path
    foreach ($entry in $settings.GetEnumerator()) {
        if ($PSBoundParameters.ContainsKey($entry.Key)) {
            continue
        }

        Set-Variable -Name $entry.Key -Value $entry.Value -Scope Script
    }
}

function Write-Step {
    param([string]$Message)
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Read-HostWithDefault {
    param(
        [string]$Prompt,
        [string]$DefaultValue = ''
    )

    $fullPrompt = if ([string]::IsNullOrWhiteSpace($DefaultValue)) { $Prompt } else { "$Prompt [$DefaultValue]" }
    $value = Read-Host -Prompt $fullPrompt
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $DefaultValue
    }

    return $value.Trim()
}

function Ensure-Directory {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Resolve-SourceRoot {
    $candidates = @(
        (Split-Path -Path $PSScriptRoot -Parent),
        (Join-Path $DeploymentRoot 'source')
    ) | Select-Object -Unique

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath (Join-Path $candidate 'QuizAPI.csproj'))) {
            return $candidate
        }
    }

    throw 'Could not locate QuizAPI.csproj.'
}

function Ensure-DotNetInstalled {
    $dotnetExe = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (-not (Test-Path -LiteralPath $dotnetExe)) {
        throw 'dotnet.exe was not found. Run ensure-server-prerequisites.ps1 first.'
    }
    return $dotnetExe
}

function Initialize-DotNetEnvironment {
    param([string]$DotNetExe)

    $dotnetRoot = Split-Path -Path $DotNetExe -Parent
    $pathEntries = @($env:Path -split ';') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    if ($pathEntries -notcontains $dotnetRoot) {
        $env:Path = $dotnetRoot + ';' + ($pathEntries -join ';')
    }

    $env:DOTNET_ROOT = $dotnetRoot
}

function Ensure-DotNetEfInstalled {
    $dotnetEfExe = Join-Path (Join-Path $DeploymentRoot 'tools') 'dotnet-ef.exe'
    if (-not (Test-Path -LiteralPath $dotnetEfExe)) {
        throw "dotnet-ef was not found at $dotnetEfExe. Run ensure-server-prerequisites.ps1 first."
    }
    return $dotnetEfExe
}

function New-RandomJwtKey {
    $bytes = New-Object byte[] 64
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    return [Convert]::ToBase64String($bytes)
}

function Build-ConnectionString {
    return "Server=$SqlInstance;Database=$DatabaseName;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=True;"
}

function Get-SqlCmdPath {
    $command = Get-Command sqlcmd.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    throw 'sqlcmd.exe was not found. Install SSMS or SQL command-line tooling.'
}

function Invoke-SqlQuery {
    param([string]$Query, [string]$Database = 'master')
    $sqlcmd = Get-SqlCmdPath
    & $sqlcmd -S $SqlInstance -d $Database -E -b -W -Q $Query
}

function Ensure-AppPoolSqlAccess {
    $principal = "IIS APPPOOL\$AppPoolName"
    $query = @"
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$principal')
BEGIN
    CREATE LOGIN [$principal] FROM WINDOWS;
END;
USE [$DatabaseName];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$principal')
BEGIN
    CREATE USER [$principal] FOR LOGIN [$principal];
END;
IF NOT EXISTS
(
    SELECT 1
    FROM sys.database_role_members drm
    INNER JOIN sys.database_principals role_principal ON drm.role_principal_id = role_principal.principal_id
    INNER JOIN sys.database_principals member_principal ON drm.member_principal_id = member_principal.principal_id
    WHERE role_principal.name = N'db_owner'
      AND member_principal.name = N'$principal'
)
BEGIN
    ALTER ROLE [db_owner] ADD MEMBER [$principal];
END;
"@
    Invoke-SqlQuery -Query $query | Out-Host
}

function Test-BootstrapAdminExists {
    param([string]$Email)

    if ([string]::IsNullOrWhiteSpace($Email)) {
        return $false
    }

    $safeEmail = $Email.Replace("'", "''")
    $query = "SET NOCOUNT ON; SELECT COUNT(1) AS UserCount FROM [dbo].[AspNetUsers] WHERE [Email] = N'$safeEmail';"
    $result = Invoke-SqlQuery -Query $query -Database $DatabaseName
    $text = ($result | Out-String).Trim()

    foreach ($line in ($text -split "`r?`n")) {
        $trimmed = $line.Trim()
        if ($trimmed -match '^\d+$') {
            return ([int]$trimmed) -gt 0
        }
    }

    return $false
}

function Resolve-Inputs {
    if ([string]::IsNullOrWhiteSpace($PublicBaseUrl)) {
        $defaultBaseUrl = if ([string]::IsNullOrWhiteSpace($HostName)) { "http://localhost" } else { "${Protocol}://$HostName" }
        $script:PublicBaseUrl = Read-HostWithDefault -Prompt 'Public base URL' -DefaultValue $defaultBaseUrl
    }

    if ([string]::IsNullOrWhiteSpace($HostName) -and $Protocol -eq 'https') {
        $script:HostName = Read-HostWithDefault -Prompt 'FQDN / host name'
    }

    if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
        $script:ConnectionString = Build-ConnectionString
    }

    if ($null -eq $EnableHttpsRedirection) {
        $script:EnableHttpsRedirection = ($Protocol -eq 'https')
    }

    if ([string]::IsNullOrWhiteSpace($JwtKey)) {
        $script:JwtKey = New-RandomJwtKey
    }

    if ([string]::IsNullOrWhiteSpace($BootstrapAdminEmail)) {
        $script:BootstrapAdminEmail = Read-HostWithDefault -Prompt 'Bootstrap admin email' -DefaultValue 'admin@quizapi.local'
    }

    if ([string]::IsNullOrWhiteSpace($BootstrapAdminPassword)) {
        $script:BootstrapAdminPassword = Read-HostWithDefault -Prompt 'Bootstrap admin password' -DefaultValue 'Admin@123'
    }
}

Resolve-Inputs

Write-Step 'Resolving source and tool locations'
$sourceRoot = Resolve-SourceRoot
$dotnetExe = Ensure-DotNetInstalled
Initialize-DotNetEnvironment -DotNetExe $dotnetExe
$dotnetEfExe = Ensure-DotNetEfInstalled
$publishScript = Join-Path $PSScriptRoot 'Publish-IISPackage.ps1'
$deployScript = Join-Path $PSScriptRoot 'Deploy-IISProduction.ps1'
$smokeTestScript = Join-Path $PSScriptRoot 'post-deploy-smoke-test.ps1'

Write-Step 'Running EF Core database migrations'
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ConnectionStrings__DefaultConnection = $ConnectionString
$env:Jwt__Key = $JwtKey
$env:Jwt__Issuer = $JwtIssuer
$env:Jwt__Audience = $JwtAudience
$env:BootstrapAdmin__Email = $BootstrapAdminEmail
$env:BootstrapAdmin__Password = $BootstrapAdminPassword
$env:BootstrapAdmin__FirstName = $BootstrapAdminFirstName
$env:BootstrapAdmin__LastName = $BootstrapAdminLastName
try {
    Push-Location $sourceRoot
    & $dotnetEfExe database update --project (Join-Path $sourceRoot 'QuizAPI.csproj') --startup-project (Join-Path $sourceRoot 'QuizAPI.csproj')
    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet ef database update failed.'
    }
}
finally {
    Pop-Location
    Remove-Item Env:\ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue
    Remove-Item Env:\ConnectionStrings__DefaultConnection -ErrorAction SilentlyContinue
    Remove-Item Env:\Jwt__Key -ErrorAction SilentlyContinue
    Remove-Item Env:\Jwt__Issuer -ErrorAction SilentlyContinue
    Remove-Item Env:\Jwt__Audience -ErrorAction SilentlyContinue
    Remove-Item Env:\BootstrapAdmin__Email -ErrorAction SilentlyContinue
    Remove-Item Env:\BootstrapAdmin__Password -ErrorAction SilentlyContinue
    Remove-Item Env:\BootstrapAdmin__FirstName -ErrorAction SilentlyContinue
    Remove-Item Env:\BootstrapAdmin__LastName -ErrorAction SilentlyContinue
}

Write-Step 'Granting SQL access to the IIS app pool'
Ensure-AppPoolSqlAccess

Write-Step 'Building deployment package'
& $publishScript -Configuration 'Release'
if ($LASTEXITCODE -ne 0) {
    throw 'Publish script failed.'
}

$deploymentZip = Get-ChildItem -Path (Join-Path $sourceRoot 'DeploymentBundle') -Filter 'QuizAPI_IIS_Production_*.zip' |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1

if ($null -eq $deploymentZip) {
    throw 'No deployment bundle was found after publish.'
}

$deployPort = if ($Protocol -eq 'https') { $HttpsPort } else { $HttpPort }

Write-Step 'Deploying application to IIS'
& $deployScript `
    -ZipPath $deploymentZip.FullName `
    -SiteName $SiteName `
    -SitePath $SitePath `
    -HostName $HostName `
    -AppPoolName $AppPoolName `
    -Protocol $Protocol `
    -Port $deployPort `
    -ConnectionString $ConnectionString `
    -JwtIssuer $JwtIssuer `
    -JwtAudience $JwtAudience `
    -JwtKey $JwtKey `
    -JwtAccessTokenMinutes $JwtAccessTokenMinutes `
    -EnableHttpsRedirection:$EnableHttpsRedirection `
    -BootstrapAdminEmail $BootstrapAdminEmail `
    -BootstrapAdminPassword $BootstrapAdminPassword `
    -BootstrapAdminFirstName $BootstrapAdminFirstName `
    -BootstrapAdminLastName $BootstrapAdminLastName `
    -CertificateThumbprint $CertificateThumbprint

if ($LASTEXITCODE -ne 0) {
    throw 'Deploy script failed.'
}

Write-Step 'Verifying bootstrap admin exists in the deployed database'
if (-not (Test-BootstrapAdminExists -Email $BootstrapAdminEmail)) {
    throw "Bootstrap admin '$BootstrapAdminEmail' was not found in database '$DatabaseName' after deployment."
}

if ($EnableSmokeTest) {
    Write-Step 'Running post-deploy smoke tests'
    & $smokeTestScript -BaseUrl $PublicBaseUrl -AdminEmail $BootstrapAdminEmail -AdminPassword $BootstrapAdminPassword
}

Write-Step 'Installation summary'
Write-Host "Source root: $sourceRoot" -ForegroundColor Green
Write-Host "Deployment root: $DeploymentRoot" -ForegroundColor Green
Write-Host "SQL connection: $ConnectionString" -ForegroundColor Green
Write-Host "Site path: $SitePath" -ForegroundColor Green
Write-Host "Public base URL: $PublicBaseUrl" -ForegroundColor Green
Write-Host "Bootstrap admin: $BootstrapAdminEmail" -ForegroundColor Green
Write-Host "HTTPS redirection enabled: $EnableHttpsRedirection" -ForegroundColor Green

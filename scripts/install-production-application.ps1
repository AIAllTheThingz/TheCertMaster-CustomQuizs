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
    [string]$DatabaseName = 'TheCertMasterCorporateDB',
    [string]$ConnectionString = '',
    [string]$PublicBaseUrl = '',
    [Nullable[bool]]$EnableHttpsRedirection = $null,
    [bool]$RestoreSeedDatabase = $true,
    [string]$DatabaseBackupPath = '',
    [string]$JwtKey = '',
    [string]$JwtIssuer = 'QuizAPI',
    [string]$JwtAudience = 'QuizAPIUsers',
    [int]$JwtAccessTokenMinutes = 60,
    [string]$BootstrapAdminEmail = '',
    [string]$BootstrapAdminPassword = '',
    [string]$BootstrapAdminFirstName = '',
    [string]$BootstrapAdminLastName = '',
    [bool]$ActiveDirectoryEnabled = $false,
    [string]$ActiveDirectoryDomain = '',
    [string]$ActiveDirectoryContainer = '',
    [string]$ActiveDirectoryNetBiosDomain = '',
    [string]$ActiveDirectoryUserPrincipalSuffix = '',
    [bool]$ActiveDirectoryRequireMappedRole = $false,
    [string]$ActiveDirectoryDefaultRole = 'User',
    [string[]]$ActiveDirectoryAdminGroups = @(),
    [string[]]$ActiveDirectoryUserGroups = @(),
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

function New-RandomBootstrapAdminPassword {
    $upper = 'ABCDEFGHJKLMNPQRSTUVWXYZ'
    $lower = 'abcdefghijkmnopqrstuvwxyz'
    $digits = '23456789'
    $symbols = '!@$%&*-_=+?'
    $allCharacters = ($upper + $lower + $digits + $symbols).ToCharArray()

    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    function Get-CryptoRandomIndex {
        param(
            [System.Security.Cryptography.RandomNumberGenerator]$Generator,
            [int]$ExclusiveMaximum
        )

        if ($ExclusiveMaximum -le 0) {
            throw 'ExclusiveMaximum must be greater than zero.'
        }

        $buffer = New-Object byte[] 4
        $maximum = [uint32]::MaxValue
        $limit = $maximum - ($maximum % [uint32]$ExclusiveMaximum)

        do {
            $Generator.GetBytes($buffer)
            $value = [BitConverter]::ToUInt32($buffer, 0)
        } while ($value -ge $limit)

        return [int]($value % [uint32]$ExclusiveMaximum)
    }

    $passwordCharacters = New-Object System.Collections.Generic.List[char]
    try {
        $requiredCharacters = @(
            $upper[(Get-CryptoRandomIndex -Generator $rng -ExclusiveMaximum $upper.Length)]
            $lower[(Get-CryptoRandomIndex -Generator $rng -ExclusiveMaximum $lower.Length)]
            $digits[(Get-CryptoRandomIndex -Generator $rng -ExclusiveMaximum $digits.Length)]
            $symbols[(Get-CryptoRandomIndex -Generator $rng -ExclusiveMaximum $symbols.Length)]
        )

        foreach ($character in $requiredCharacters) {
            $passwordCharacters.Add([char]$character)
        }

        while ($passwordCharacters.Count -lt 24) {
            $passwordCharacters.Add($allCharacters[(Get-CryptoRandomIndex -Generator $rng -ExclusiveMaximum $allCharacters.Length)])
        }

        for ($i = $passwordCharacters.Count - 1; $i -gt 0; $i--) {
            $j = Get-CryptoRandomIndex -Generator $rng -ExclusiveMaximum ($i + 1)
            $temp = $passwordCharacters[$i]
            $passwordCharacters[$i] = $passwordCharacters[$j]
            $passwordCharacters[$j] = $temp
        }
    }
    finally {
        $rng.Dispose()
    }

    return -join $passwordCharacters
}

function Test-IsDefaultBootstrapPassword {
    param([string]$Password)
    return -not [string]::IsNullOrWhiteSpace($Password) -and $Password -eq 'Admin@123'
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

function Invoke-SqlQueryScalar {
    param([string]$Query, [string]$Database = 'master')
    $sqlcmd = Get-SqlCmdPath
    $result = & $sqlcmd -S $SqlInstance -d $Database -E -b -W -h -1 -Q $Query
    $text = ($result | Out-String).Trim()
    foreach ($line in ($text -split "`r?`n")) {
        $trimmed = $line.Trim()
        if (-not [string]::IsNullOrWhiteSpace($trimmed)) {
            return $trimmed
        }
    }

    return ''
}

function Get-NormalizedBackupPath {
    param([string]$PathValue, [string]$SourceRoot)

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return Join-Path $SourceRoot 'DeploymentBundle\TheCertMasterCorporateDB.bak'
    }

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return $PathValue
    }

    return Join-Path $SourceRoot $PathValue
}

function Get-BackupFileList {
    param([string]$BackupPath)

    $escapedPath = $BackupPath.Replace("'", "''")
    $query = "RESTORE FILELISTONLY FROM DISK = N'$escapedPath';"
    $sqlcmd = Get-SqlCmdPath
    $rows = & $sqlcmd -S $SqlInstance -d master -E -b -W -s '|' -h -1 -Q $query

    $items = @()
    foreach ($row in $rows) {
        $text = $row.ToString().Trim()
        if ([string]::IsNullOrWhiteSpace($text) -or $text -like 'Changed database context*') {
            continue
        }

        $parts = $text -split '\|'
        if ($parts.Count -lt 3) {
            continue
        }

        $items += [pscustomobject]@{
            LogicalName = $parts[0].Trim()
            PhysicalName = $parts[1].Trim()
            Type = $parts[2].Trim()
        }
    }

    return $items
}

function Restore-DatabaseFromBackup {
    param([string]$BackupPath)

    if (-not (Test-Path -LiteralPath $BackupPath)) {
        throw "Database backup file was not found: $BackupPath"
    }

    $dataPath = Invoke-SqlQueryScalar -Query "SET NOCOUNT ON; SELECT CONVERT(nvarchar(4000), SERVERPROPERTY('InstanceDefaultDataPath'));" -Database 'master'
    $logPath = Invoke-SqlQueryScalar -Query "SET NOCOUNT ON; SELECT CONVERT(nvarchar(4000), SERVERPROPERTY('InstanceDefaultLogPath'));" -Database 'master'

    if ([string]::IsNullOrWhiteSpace($dataPath) -or [string]::IsNullOrWhiteSpace($logPath)) {
        throw 'Could not resolve SQL Server default data/log paths.'
    }

    $fileList = Get-BackupFileList -BackupPath $BackupPath
    $dataFile = $fileList | Where-Object { $_.Type -eq 'D' } | Select-Object -First 1
    $logFile = $fileList | Where-Object { $_.Type -eq 'L' } | Select-Object -First 1

    if ($null -eq $dataFile -or $null -eq $logFile) {
        throw "Backup file '$BackupPath' does not contain both data and log records."
    }

    $dataExtension = [System.IO.Path]::GetExtension($dataFile.PhysicalName)
    $logExtension = [System.IO.Path]::GetExtension($logFile.PhysicalName)
    if ([string]::IsNullOrWhiteSpace($dataExtension)) { $dataExtension = '.mdf' }
    if ([string]::IsNullOrWhiteSpace($logExtension)) { $logExtension = '.ldf' }

    $targetDataFile = Join-Path $dataPath ($DatabaseName + $dataExtension)
    $targetLogFile = Join-Path $logPath ($DatabaseName + '_log' + $logExtension)

    $escapedBackupPath = $BackupPath.Replace("'", "''")
    $escapedDataLogical = $dataFile.LogicalName.Replace("'", "''")
    $escapedLogLogical = $logFile.LogicalName.Replace("'", "''")
    $escapedDataFile = $targetDataFile.Replace("'", "''")
    $escapedLogFile = $targetLogFile.Replace("'", "''")

    $query = @"
IF DB_ID(N'$DatabaseName') IS NOT NULL
BEGIN
    ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
END;
RESTORE DATABASE [$DatabaseName]
FROM DISK = N'$escapedBackupPath'
WITH REPLACE,
     RECOVERY,
     MOVE N'$escapedDataLogical' TO N'$escapedDataFile',
     MOVE N'$escapedLogLogical' TO N'$escapedLogFile';
ALTER DATABASE [$DatabaseName] SET MULTI_USER;
"@

    Invoke-SqlQuery -Query $query -Database 'master' | Out-Host
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

function Ensure-AppPoolExists {
    Import-Module WebAdministration

    if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) {
        New-WebAppPool -Name $AppPoolName | Out-Null
    }

    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedRuntimeVersion -Value ""
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedPipelineMode -Value "Integrated"
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.identityType -Value "ApplicationPoolIdentity"
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name autoStart -Value $true
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

function Test-QuizDataExists {
    $query = "SET NOCOUNT ON; SELECT COUNT(1) AS QuizCount FROM [dbo].[Quizzes];"
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

function Wait-ForAdminExists {
    param(
        [string]$Email,
        [int]$TimeoutSeconds = 60
    )

    if ([string]::IsNullOrWhiteSpace($Email)) {
        return $false
    }

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        if (Test-BootstrapAdminExists -Email $Email) {
            return $true
        }

        Start-Sleep -Seconds 2
    }
    while ((Get-Date) -lt $deadline)

    return $false
}

function Resolve-Inputs {
    $script:BootstrapAdminPasswordWasGenerated = $false

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

    $script:DatabaseBackupPath = Get-NormalizedBackupPath -PathValue $DatabaseBackupPath -SourceRoot (Resolve-SourceRoot)

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
        $script:BootstrapAdminPassword = New-RandomBootstrapAdminPassword
        $script:BootstrapAdminPasswordWasGenerated = $true
    }

    if (Test-IsDefaultBootstrapPassword -Password $BootstrapAdminPassword) {
        throw "BootstrapAdminPassword cannot be the packaged default password. Choose a new password before installing."
    }
}

Resolve-Inputs

$validatedAdminEmail = $BootstrapAdminEmail
$validatedAdminPassword = $BootstrapAdminPassword

Write-Step 'Resolving source and tool locations'
$sourceRoot = Resolve-SourceRoot
$dotnetExe = Ensure-DotNetInstalled
Initialize-DotNetEnvironment -DotNetExe $dotnetExe
$dotnetEfExe = Ensure-DotNetEfInstalled
$publishScript = Join-Path $PSScriptRoot 'Publish-IISPackage.ps1'
$deployScript = Join-Path $PSScriptRoot 'Deploy-IISProduction.ps1'
$smokeTestScript = Join-Path $PSScriptRoot 'post-deploy-smoke-test.ps1'

if ($RestoreSeedDatabase) {
    Write-Step "Restoring seeded $DatabaseName content from the repository backup"
    Restore-DatabaseFromBackup -BackupPath $DatabaseBackupPath
}

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

Write-Step 'Ensuring IIS app pool identity exists before SQL access is granted'
Ensure-AppPoolExists

Write-Step 'Granting SQL access to the IIS app pool'
Ensure-AppPoolSqlAccess

if ($RestoreSeedDatabase -and -not (Test-QuizDataExists)) {
    throw "No quizzes were found in database '$DatabaseName' after the repository restore and migration steps."
}
elseif (-not $RestoreSeedDatabase -and -not (Test-QuizDataExists)) {
    Write-Warning "No quizzes were found after migration-only install. This is allowed when RestoreSeedDatabase is disabled."
}

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
    -ActiveDirectoryEnabled:$ActiveDirectoryEnabled `
    -ActiveDirectoryDomain $ActiveDirectoryDomain `
    -ActiveDirectoryContainer $ActiveDirectoryContainer `
    -ActiveDirectoryNetBiosDomain $ActiveDirectoryNetBiosDomain `
    -ActiveDirectoryUserPrincipalSuffix $ActiveDirectoryUserPrincipalSuffix `
    -ActiveDirectoryRequireMappedRole:$ActiveDirectoryRequireMappedRole `
    -ActiveDirectoryDefaultRole $ActiveDirectoryDefaultRole `
    -ActiveDirectoryAdminGroups $ActiveDirectoryAdminGroups `
    -ActiveDirectoryUserGroups $ActiveDirectoryUserGroups `
    -CertificateThumbprint $CertificateThumbprint

if ($LASTEXITCODE -ne 0) {
    throw 'Deploy script failed.'
}

Write-Step 'Reapplying SQL access after IIS deployment'
Ensure-AppPoolSqlAccess

Write-Step 'Verifying bootstrap admin exists in the deployed database'
if (-not (Wait-ForAdminExists -Email $BootstrapAdminEmail -TimeoutSeconds 60)) {
    throw "Bootstrap admin '$BootstrapAdminEmail' was not found in database '$DatabaseName' after deployment."
}

if ($EnableSmokeTest) {
    Write-Step 'Running post-deploy smoke tests'
    & $smokeTestScript -BaseUrl $PublicBaseUrl -AdminEmail $validatedAdminEmail -AdminPassword $validatedAdminPassword -SkipQuizCatalogCheck:$(-not $RestoreSeedDatabase)
}

Write-Step 'Installation summary'
Write-Host "Source root: $sourceRoot" -ForegroundColor Green
Write-Host "Deployment root: $DeploymentRoot" -ForegroundColor Green
Write-Host "SQL connection: $ConnectionString" -ForegroundColor Green
Write-Host "Site path: $SitePath" -ForegroundColor Green
Write-Host "Public base URL: $PublicBaseUrl" -ForegroundColor Green
Write-Host "Validated admin: $validatedAdminEmail" -ForegroundColor Green
Write-Host "Bootstrap admin password: $validatedAdminPassword" -ForegroundColor Yellow
if ($BootstrapAdminPasswordWasGenerated) {
    Write-Host "Bootstrap admin password was auto-generated for temporary installation/configuration use. Change the admin@quizapi.local password again after setup is complete." -ForegroundColor Yellow
}
Write-Host "HTTPS redirection enabled: $EnableHttpsRedirection" -ForegroundColor Green
Write-Host "Seed database restore enabled: $RestoreSeedDatabase" -ForegroundColor Green
Write-Host "Seed database backup path: $DatabaseBackupPath" -ForegroundColor Green

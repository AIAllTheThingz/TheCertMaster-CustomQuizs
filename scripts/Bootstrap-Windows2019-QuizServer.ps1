#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$DownloadsRoot = "C:\Deploy\downloads",
    [string]$WorkspaceRoot = "C:\src",
    [string]$LogRoot = "C:\Deploy\logs",
    [string]$RepoZipUrl = "https://github.com/AIAllTheThingz/TheCertMaster-CustomQuizs/archive/refs/heads/main.zip",
    [string]$RepoFolderName = "TheCertMaster-CustomQuizs",
    [string]$Configuration = "Release",
    [string]$SiteName = "QuizAPI",
    [string]$SitePath = "C:\sites\QuizAPI\current",
    [string]$HostName = "",
    [ValidateSet("http", "https")]
    [string]$Protocol = "http",
    [int]$Port = 80,
    [string]$AppPoolName = "QuizAppPool",
    [string]$ConnectionString = "",
    [string]$SqlServerInstance = ".\SQLEXPRESS",
    [string]$DatabaseName = "QuizDB",
    [string]$DatabaseBackupPath = "",
    [ValidateSet("", "overwrite-if-exists", "only-if-empty")]
    [string]$DatabaseRestoreMode = "",
    [string]$JwtIssuer = "QuizAPI",
    [string]$JwtAudience = "QuizAPIUsers",
    [string]$JwtKey,
    [int]$JwtAccessTokenMinutes = 60,
    [switch]$SaveGeneratedJwtKey,
    [string]$GeneratedJwtKeyPath = "",
    [string]$CertificateThumbprint = "",
    [switch]$UseRootHttpsBinding,
    [string]$DotNetChannel = "9.0",
    [string]$SqlExpressDownloadUrl = "https://go.microsoft.com/fwlink/?linkid=866658",
    [string]$SqlInstanceName = "SQLEXPRESS",
    [switch]$SkipIisInstall,
    [switch]$SkipSqlExpressInstall,
    [switch]$SkipHostingBundleInstall,
    [switch]$SkipDotNetSdkInstall,
    [switch]$SkipSourceDownload,
    [switch]$SkipTests,
    [switch]$SkipDatabaseRestore,
    [switch]$SkipDatabaseUpdate,
    [switch]$SkipDeploy,
    [string]$ReportPath = "",
    [switch]$PromptForMissingValues,
    [switch]$SmokeTestAccountCheck,
    [string]$SmokeTestAdminEmail = "admin@quizapi.local"
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Ensure-RunningAsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "This script must be run from an elevated PowerShell session."
    }
}

function Ensure-Directory {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Test-DirectoryWritable {
    param([string]$Path)

    Ensure-Directory -Path $Path
    $probePath = Join-Path $Path (".write-test-" + [Guid]::NewGuid().ToString("N") + ".tmp")

    try {
        Set-Content -Path $probePath -Value "ok" -Encoding ASCII -Force
        Remove-Item -LiteralPath $probePath -Force -ErrorAction SilentlyContinue
        return $true
    }
    catch {
        return $false
    }
}

function Get-Timestamp {
    return Get-Date -Format "yyyyMMdd_HHmmss"
}

function New-RandomJwtKey {
    $bytes = New-Object byte[] 64
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    return [Convert]::ToBase64String($bytes)
}

function Save-GeneratedJwtKeyFile {
    param(
        [string]$Path,
        [string]$KeyValue
    )

    $directory = Split-Path -Path $Path -Parent
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        Ensure-Directory -Path $directory
    }

    Set-Content -Path $Path -Value $KeyValue -Encoding ASCII -Force

    & icacls $Path /inheritance:r | Out-Null
    & icacls $Path /grant:r "Administrators:F" "SYSTEM:F" | Out-Null
}

function Read-HostWithDefault {
    param(
        [string]$Prompt,
        [string]$DefaultValue = ""
    )

    $fullPrompt = if ([string]::IsNullOrWhiteSpace($DefaultValue)) {
        $Prompt
    }
    else {
        "$Prompt [$DefaultValue]"
    }

    $value = Read-Host -Prompt $fullPrompt
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $DefaultValue
    }

    return $value.Trim()
}

function Build-ConnectionString {
    param(
        [string]$ServerInstance,
        [string]$Database
    )

    return "Server=$ServerInstance;Database=$Database;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=True;"
}

function Prepare-EnvironmentPaths {
    param(
        [string]$DownloadsRootPath,
        [string]$WorkspaceRootPath,
        [string]$LogRootPath,
        [string]$SiteRootPath,
        [string]$RepoName,
        [string]$JwtRecoveryPath,
        [string]$BackupFilePath
    )

    Write-Step "Preparing environment directories"

    $pathsToPrepare = @(
        (Split-Path -Path $DownloadsRootPath -Parent),
        $DownloadsRootPath,
        $WorkspaceRootPath,
        (Join-Path $WorkspaceRootPath $RepoName),
        (Split-Path -Path $LogRootPath -Parent),
        $LogRootPath,
        (Split-Path -Path $SiteRootPath -Parent),
        $SiteRootPath
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

    if (-not [string]::IsNullOrWhiteSpace($JwtRecoveryPath)) {
        $jwtRecoveryDirectory = Split-Path -Path $JwtRecoveryPath -Parent
        if (-not [string]::IsNullOrWhiteSpace($jwtRecoveryDirectory)) {
            $pathsToPrepare += $jwtRecoveryDirectory
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($BackupFilePath)) {
        $backupDirectory = Split-Path -Path $BackupFilePath -Parent
        if (-not [string]::IsNullOrWhiteSpace($backupDirectory)) {
            $pathsToPrepare += $backupDirectory
        }
    }

    $pathsToPrepare = $pathsToPrepare | Select-Object -Unique

    foreach ($path in $pathsToPrepare) {
        Ensure-Directory -Path $path
        if (-not (Test-DirectoryWritable -Path $path)) {
            throw "Directory is not writable: $path"
        }
    }
}

function Download-File {
    param(
        [string]$Url,
        [string]$DestinationPath
    )

    Write-Host "Downloading $Url" -ForegroundColor DarkGray
    Invoke-WebRequest -Uri $Url -OutFile $DestinationPath -UseBasicParsing
}

function Invoke-ProcessChecked {
    param(
        [string]$FilePath,
        [string[]]$ArgumentList,
        [string]$StepName
    )

    Write-Host "$FilePath $($ArgumentList -join ' ')" -ForegroundColor DarkGray
    $process = Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -Wait -PassThru -NoNewWindow
    if ($process.ExitCode -ne 0) {
        throw "$StepName failed with exit code $($process.ExitCode)."
    }
}

function Update-ProcessPathFromMachine {
    $machinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $pathParts = @()

    if (-not [string]::IsNullOrWhiteSpace($machinePath)) {
        $pathParts += $machinePath
    }

    if (-not [string]::IsNullOrWhiteSpace($userPath)) {
        $pathParts += $userPath
    }

    if ($pathParts.Count -gt 0) {
        $env:Path = ($pathParts -join ";")
    }
}

function Get-DotNetCommandPath {
    $command = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($null -ne $command -and -not [string]::IsNullOrWhiteSpace($command.Source)) {
        return $command.Source
    }

    $commonPaths = @(
        (Join-Path ${env:ProgramFiles} "dotnet\dotnet.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "dotnet\dotnet.exe")
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($candidate in $commonPaths) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw "Could not locate dotnet.exe. Confirm the .NET SDK is installed."
}

function Get-SqlCmdPath {
    $command = Get-Command sqlcmd.exe -ErrorAction SilentlyContinue
    if ($null -ne $command -and -not [string]::IsNullOrWhiteSpace($command.Source)) {
        return $command.Source
    }

    $candidates = @(
        "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\sqlcmd.exe",
        "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\sqlcmd.exe",
        "C:\Program Files\Microsoft SQL Server\150\Tools\Binn\sqlcmd.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw "Could not locate sqlcmd.exe. Install SQL Server command-line tools or SSMS tooling."
}

function Get-InstalledProductDisplayNames {
    $paths = @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*"
    )

    return Get-ItemProperty -Path $paths -ErrorAction SilentlyContinue |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_.DisplayName) } |
        Select-Object -ExpandProperty DisplayName
}

function Get-DotNetReleaseMetadata {
    param([string]$Channel)

    $indexUrl = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json"
    $index = Invoke-RestMethod -Uri $indexUrl
    $channelEntry = $index.'releases-index' | Where-Object { $_.'channel-version' -eq $Channel } | Select-Object -First 1

    if ($null -eq $channelEntry) {
        throw "Could not find .NET release metadata for channel '$Channel'."
    }

    return Invoke-RestMethod -Uri $channelEntry.'releases.json'
}

function Get-DotNetInstallerAsset {
    param(
        [object]$ReleaseMetadata,
        [ValidateSet("sdk", "hosting")]
        [string]$AssetType
    )

    $latestRelease = $ReleaseMetadata.releases | Select-Object -First 1

    switch ($AssetType) {
        "sdk" {
            $sdkFileSets = @()

            if ($null -ne $latestRelease.sdk -and $null -ne $latestRelease.sdk.files) {
                $sdkFileSets += ,$latestRelease.sdk.files
            }

            if ($null -ne $latestRelease.sdks) {
                foreach ($sdkEntry in @($latestRelease.sdks)) {
                    if ($null -ne $sdkEntry -and $null -ne $sdkEntry.files) {
                        $sdkFileSets += ,$sdkEntry.files
                    }
                }
            }

            foreach ($fileSet in $sdkFileSets) {
                $asset = @($fileSet) |
                    Where-Object {
                        $_.rid -eq "win-x64" -and
                        $_.url -like "*.exe" -and
                        ($_.name -like "dotnet-sdk-*-win-x64.exe" -or $_.name -like "dotnet-sdk-win-x64.exe")
                    } |
                    Select-Object -First 1

                if ($null -ne $asset) {
                    break
                }
            }
        }
        "hosting" {
            $asset = @($latestRelease.'aspnetcore-runtime'.files) |
                Where-Object {
                    $_.name -eq "dotnet-hosting-win.exe" -or
                    $_.url -like "*dotnet-hosting*-win.exe"
                } |
                Select-Object -First 1
        }
    }

    if ($null -eq $asset) {
        throw "Could not find .NET $AssetType installer asset."
    }

    return $asset
}

function Test-DotNetSdkInstalled {
    param([string]$Channel)

    try {
        $dotnetPath = Get-DotNetCommandPath
        $sdks = & $dotnetPath --list-sdks 2>$null
        if ($LASTEXITCODE -ne 0) {
            return $false
        }
    }
    catch {
        return $false
    }

    return $sdks | Where-Object { $_ -match "^$([regex]::Escape($Channel))\." } | Select-Object -First 1
}

function Test-HostingBundleInstalled {
    param([string]$Channel)

    $productNames = Get-InstalledProductDisplayNames
    return $productNames | Where-Object { $_ -like "Microsoft ASP.NET Core * Hosting Bundle*" -and $_ -like "*$Channel*" } | Select-Object -First 1
}

function Test-SqlExpressInstalled {
    param([string]$InstanceName)

    return Get-Service -Name "MSSQL`$$InstanceName" -ErrorAction SilentlyContinue
}

function Install-IisRoleServices {
    Write-Step "Installing IIS and required Windows features"

    $featureNames = @(
        "Web-Server",
        "Web-Default-Doc",
        "Web-Static-Content",
        "Web-Http-Errors",
        "Web-Http-Logging",
        "Web-Stat-Compression",
        "Web-Filtering",
        "Web-Http-Redirect",
        "Web-Health",
        "Web-Performance",
        "Web-Security",
        "Web-Windows-Auth",
        "Web-App-Dev",
        "Web-Net-Ext45",
        "Web-Asp-Net45",
        "Web-Mgmt-Tools",
        "Web-Mgmt-Console"
    )

    $result = Install-WindowsFeature -Name $featureNames -IncludeManagementTools
    if (-not $result.Success) {
        throw "Install-WindowsFeature reported failure while installing IIS features."
    }
}

function Install-DotNetSdk {
    param(
        [object]$ReleaseMetadata,
        [string]$DestinationRoot
    )

    if (Test-DotNetSdkInstalled -Channel $DotNetChannel) {
        Write-Step ".NET SDK $DotNetChannel is already installed"
        return
    }

    $asset = Get-DotNetInstallerAsset -ReleaseMetadata $ReleaseMetadata -AssetType "sdk"
    $installerPath = Join-Path $DestinationRoot ([IO.Path]::GetFileName($asset.url))

    Write-Step "Installing .NET SDK for channel $DotNetChannel"
    Download-File -Url $asset.url -DestinationPath $installerPath
    Invoke-ProcessChecked -FilePath $installerPath -ArgumentList @("/install", "/quiet", "/norestart") -StepName ".NET SDK installation"
    Update-ProcessPathFromMachine
}

function Install-HostingBundle {
    param(
        [object]$ReleaseMetadata,
        [string]$DestinationRoot
    )

    if (Test-HostingBundleInstalled -Channel $DotNetChannel) {
        Write-Step "ASP.NET Core Hosting Bundle $DotNetChannel is already installed"
        return
    }

    $asset = Get-DotNetInstallerAsset -ReleaseMetadata $ReleaseMetadata -AssetType "hosting"
    $installerPath = Join-Path $DestinationRoot ([IO.Path]::GetFileName($asset.url))

    Write-Step "Installing ASP.NET Core Hosting Bundle for channel $DotNetChannel"
    Download-File -Url $asset.url -DestinationPath $installerPath
    Invoke-ProcessChecked -FilePath $installerPath -ArgumentList @("/install", "/quiet", "/norestart", "OPT_NO_X86=1") -StepName "ASP.NET Core Hosting Bundle installation"
}

function Install-SqlExpress {
    param(
        [string]$DestinationRoot,
        [string]$InstanceName,
        [string]$DownloadUrl
    )

    if (Test-SqlExpressInstalled -InstanceName $InstanceName) {
        Write-Step "SQL Server Express instance '$InstanceName' is already installed"
        return
    }

    $installerPath = Join-Path $DestinationRoot "SQLEXPR_x64_ENU.exe"
    Write-Step "Installing SQL Server Express 2019 instance '$InstanceName'"
    Download-File -Url $DownloadUrl -DestinationPath $installerPath

    $arguments = @(
        "/Q",
        "/ACTION=Install",
        "/FEATURES=SQLEngine",
        "/INSTANCENAME=$InstanceName",
        "/SQLSVCSTARTUPTYPE=Automatic",
        "/SQLSVCACCOUNT=""NT AUTHORITY\NETWORK SERVICE""",
        "/SQLSYSADMINACCOUNTS=""BUILTIN\Administrators""",
        "/TCPENABLED=1",
        "/NPENABLED=0",
        "/IACCEPTSQLSERVERLICENSETERMS"
    )

    Invoke-ProcessChecked -FilePath $installerPath -ArgumentList $arguments -StepName "SQL Server Express installation"
}

function Invoke-SqlCmdQuery {
    param(
        [string]$ServerInstance,
        [string]$Database = "master",
        [string]$Query
    )

    $sqlcmdPath = Get-SqlCmdPath
    $arguments = @("-S", $ServerInstance, "-d", $Database, "-E", "-b", "-W", "-Q", $Query)
    return & $sqlcmdPath @arguments
}

function Invoke-SqlCmdScalarLines {
    param(
        [string]$ServerInstance,
        [string]$Database = "master",
        [string]$Query
    )

    $sqlcmdPath = Get-SqlCmdPath
    $arguments = @("-S", $ServerInstance, "-d", $Database, "-E", "-b", "-W", "-h", "-1", "-Q", $Query)
    $raw = & $sqlcmdPath @arguments
    return @($raw | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim() })
}

function Test-DatabaseExists {
    param(
        [string]$ServerInstance,
        [string]$Database
    )

    $rows = Invoke-SqlCmdScalarLines -ServerInstance $ServerInstance -Database "master" -Query "SET NOCOUNT ON; SELECT CASE WHEN DB_ID(N'$Database') IS NULL THEN '0' ELSE '1' END;"
    return ($rows | Select-Object -First 1) -eq "1"
}

function Test-DatabaseHasContent {
    param(
        [string]$ServerInstance,
        [string]$Database
    )

    if (-not (Test-DatabaseExists -ServerInstance $ServerInstance -Database $Database)) {
        return $false
    }

    $query = @"
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NULL OR OBJECT_ID(N'dbo.Quizzes', N'U') IS NULL OR OBJECT_ID(N'dbo.Questions', N'U') IS NULL
BEGIN
    SELECT '0';
END
ELSE
BEGIN
    SELECT CASE
        WHEN EXISTS (SELECT 1 FROM dbo.AspNetUsers)
          OR EXISTS (SELECT 1 FROM dbo.Quizzes)
          OR EXISTS (SELECT 1 FROM dbo.Questions)
        THEN '1'
        ELSE '0'
    END;
END
"@

    $rows = Invoke-SqlCmdScalarLines -ServerInstance $ServerInstance -Database $Database -Query $query
    return ($rows | Select-Object -First 1) -eq "1"
}

function Get-BackupLogicalFiles {
    param(
        [string]$ServerInstance,
        [string]$BackupPath
    )

    $escapedBackupPath = $BackupPath.Replace("'", "''")
    $rows = Invoke-SqlCmdScalarLines -ServerInstance $ServerInstance -Database "master" -Query "RESTORE FILELISTONLY FROM DISK = N'$escapedBackupPath';"

    $dataLogicalName = $null
    $logLogicalName = $null

    foreach ($row in $rows) {
        $parts = $row -split '\s{2,}'
        if ($parts.Count -lt 3) {
            continue
        }

        $logicalName = $parts[0].Trim()
        $type = $parts[2].Trim()

        if ($type -eq "D" -and [string]::IsNullOrWhiteSpace($dataLogicalName)) {
            $dataLogicalName = $logicalName
        }
        elseif ($type -eq "L" -and [string]::IsNullOrWhiteSpace($logLogicalName)) {
            $logLogicalName = $logicalName
        }
    }

    if ([string]::IsNullOrWhiteSpace($dataLogicalName) -or [string]::IsNullOrWhiteSpace($logLogicalName)) {
        throw "Could not determine logical file names from backup: $BackupPath"
    }

    return @{
        Data = $dataLogicalName
        Log = $logLogicalName
    }
}

function Get-SqlDefaultPaths {
    param([string]$ServerInstance)

    $query = "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000)); SELECT CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(4000));"
    $rows = Invoke-SqlCmdScalarLines -ServerInstance $ServerInstance -Database "master" -Query $query

    if ($rows.Count -lt 2) {
        throw "Could not determine SQL Server default data/log paths."
    }

    return @{
        Data = $rows[0]
        Log = $rows[1]
    }
}

function Restore-DatabaseFromBackup {
    param(
        [string]$ServerInstance,
        [string]$Database,
        [string]$BackupPath,
        [string]$RestoreMode
    )

    if (-not (Test-Path -LiteralPath $BackupPath)) {
        throw "Database backup path not found: $BackupPath"
    }

    $dbExists = Test-DatabaseExists -ServerInstance $ServerInstance -Database $Database
    $dbHasContent = Test-DatabaseHasContent -ServerInstance $ServerInstance -Database $Database

    if ($RestoreMode -eq "only-if-empty" -and $dbExists -and $dbHasContent) {
        Write-Step "Skipping database restore because '$Database' already contains data"
        return
    }

    Write-Step "Restoring database '$Database' from backup"
    $paths = Get-SqlDefaultPaths -ServerInstance $ServerInstance
    Ensure-Directory -Path $paths.Data
    Ensure-Directory -Path $paths.Log

    $logicalFiles = Get-BackupLogicalFiles -ServerInstance $ServerInstance -BackupPath $BackupPath
    $mdfPath = Join-Path $paths.Data "$Database.mdf"
    $ldfPath = Join-Path $paths.Log "${Database}_log.ldf"
    $escapedBackupPath = $BackupPath.Replace("'", "''")
    $escapedMdfPath = $mdfPath.Replace("'", "''")
    $escapedLdfPath = $ldfPath.Replace("'", "''")

    $restoreQuery = @"
IF DB_ID(N'$Database') IS NOT NULL
BEGIN
    ALTER DATABASE [$Database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
END;
RESTORE DATABASE [$Database]
FROM DISK = N'$escapedBackupPath'
WITH REPLACE, RECOVERY,
MOVE N'$($logicalFiles.Data)' TO N'$escapedMdfPath',
MOVE N'$($logicalFiles.Log)' TO N'$escapedLdfPath';
ALTER DATABASE [$Database] SET MULTI_USER;
"@

    Invoke-SqlCmdQuery -ServerInstance $ServerInstance -Database "master" -Query $restoreQuery | Out-Host
}

function Grant-AppPoolDatabaseAccess {
    param(
        [string]$ServerInstance,
        [string]$Database,
        [string]$AppPool
    )

    Write-Step "Granting SQL access to IIS app pool identity"
    $principal = "IIS APPPOOL\$AppPool"
    $query = @"
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$principal')
BEGIN
    CREATE LOGIN [$principal] FROM WINDOWS;
END;
USE [$Database];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$principal')
BEGIN
    CREATE USER [$principal] FOR LOGIN [$principal];
END;
ALTER ROLE [db_owner] ADD MEMBER [$principal];
"@

    Invoke-SqlCmdQuery -ServerInstance $ServerInstance -Database "master" -Query $query | Out-Host
}

function Test-SmokeTestAccount {
    param(
        [string]$ServerInstance,
        [string]$Database,
        [string]$Email
    )

    Write-Step "Checking smoke test account presence"
    $rows = Invoke-SqlCmdScalarLines -ServerInstance $ServerInstance -Database $Database -Query "SET NOCOUNT ON; SELECT Email FROM AspNetUsers WHERE Email = N'$Email';"
    if (($rows | Select-Object -First 1) -ne $Email) {
        throw "Smoke test account not found in database '$Database': $Email"
    }
}

function Resolve-DeploymentInputs {
    if ($PromptForMissingValues -or [string]::IsNullOrWhiteSpace($SqlServerInstance)) {
        $script:SqlServerInstance = Read-HostWithDefault -Prompt "SQL Server instance" -DefaultValue $(if ([string]::IsNullOrWhiteSpace($script:SqlServerInstance)) { ".\SQLEXPRESS" } else { $script:SqlServerInstance })
    }

    if ($PromptForMissingValues -or [string]::IsNullOrWhiteSpace($DatabaseName)) {
        $script:DatabaseName = Read-HostWithDefault -Prompt "Database name" -DefaultValue $(if ([string]::IsNullOrWhiteSpace($script:DatabaseName)) { "QuizDB" } else { $script:DatabaseName })
    }

    if ($PromptForMissingValues) {
        $script:HostName = Read-HostWithDefault -Prompt "Site FQDN or host name (optional for HTTP)" -DefaultValue $script:HostName
    }

    if ($PromptForMissingValues -or [string]::IsNullOrWhiteSpace($DatabaseRestoreMode)) {
        $script:DatabaseRestoreMode = Read-HostWithDefault -Prompt "Database restore mode (overwrite-if-exists or only-if-empty)" -DefaultValue $(if ([string]::IsNullOrWhiteSpace($script:DatabaseRestoreMode)) { "overwrite-if-exists" } else { $script:DatabaseRestoreMode })
    }

    if (-not $SkipDatabaseRestore -and ($PromptForMissingValues -or [string]::IsNullOrWhiteSpace($DatabaseBackupPath))) {
        $script:DatabaseBackupPath = Read-HostWithDefault -Prompt "Database backup path (.bak)" -DefaultValue $script:DatabaseBackupPath
    }

    if ([string]::IsNullOrWhiteSpace($DatabaseRestoreMode)) {
        $script:DatabaseRestoreMode = "overwrite-if-exists"
    }

    if ($DatabaseRestoreMode -notin @("overwrite-if-exists", "only-if-empty")) {
        throw "DatabaseRestoreMode must be 'overwrite-if-exists' or 'only-if-empty'."
    }

    if (-not $SkipDatabaseRestore -and [string]::IsNullOrWhiteSpace($DatabaseBackupPath)) {
        throw "DatabaseBackupPath is required unless -SkipDatabaseRestore is used."
    }

    if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
        $script:ConnectionString = Build-ConnectionString -ServerInstance $SqlServerInstance -Database $DatabaseName
    }
}

function Expand-RepoZip {
    param(
        [string]$ZipUrl,
        [string]$DestinationRoot,
        [string]$FolderName,
        [string]$DownloadRoot
    )

    $zipPath = Join-Path $DownloadRoot "$FolderName-main.zip"
    $extractRoot = Join-Path $DestinationRoot "$FolderName-main"
    $targetRoot = Join-Path $DestinationRoot $FolderName

    if (Test-Path -LiteralPath $extractRoot) {
        Remove-Item -LiteralPath $extractRoot -Recurse -Force
    }

    if (Test-Path -LiteralPath $targetRoot) {
        Remove-Item -LiteralPath $targetRoot -Recurse -Force
    }

    Write-Step "Downloading repository source"
    Download-File -Url $ZipUrl -DestinationPath $zipPath

    Write-Step "Extracting repository source"
    Expand-Archive -Path $zipPath -DestinationPath $DestinationRoot -Force
    Move-Item -Path $extractRoot -Destination $targetRoot -Force

    return $targetRoot
}

function Ensure-DotNetEfInstalled {
    Update-ProcessPathFromMachine
    $dotnetPath = Get-DotNetCommandPath
    $toolsDir = Join-Path $env:USERPROFILE ".dotnet\tools"
    $dotnetEfPath = Join-Path $toolsDir "dotnet-ef.exe"

    if (-not (Test-Path -LiteralPath $dotnetEfPath)) {
        Write-Step "Installing dotnet-ef"
        Invoke-ProcessChecked -FilePath $dotnetPath -ArgumentList @("tool", "install", "--global", "dotnet-ef", "--version", "9.*") -StepName "dotnet-ef installation"
    }

    if ($env:PATH -notlike "*$toolsDir*") {
        $env:PATH = "$toolsDir;$env:PATH"
    }

    return $dotnetEfPath
}

function Invoke-DatabaseUpdate {
    param(
        [string]$RepoRoot,
        [string]$Connection
    )

    $dotnetEfPath = Ensure-DotNetEfInstalled
    $projectPath = Join-Path $RepoRoot "QuizAPI.csproj"

    Write-Step "Applying EF Core migrations to the target database"
    $env:ConnectionStrings__DefaultConnection = $Connection
    try {
        Invoke-ProcessChecked -FilePath $dotnetEfPath -ArgumentList @(
            "database",
            "update",
            "--project", $projectPath,
            "--startup-project", $projectPath
        ) -StepName "EF Core database update"
    }
    finally {
        Remove-Item Env:\ConnectionStrings__DefaultConnection -ErrorAction SilentlyContinue
    }
}

function Invoke-PublishPackage {
    param(
        [string]$RepoRoot,
        [string]$BuildConfiguration,
        [switch]$OmitTests
    )

    $publishScript = Join-Path $RepoRoot "scripts\Publish-IISPackage.ps1"
    Write-Step "Building and packaging the application"
    $publishParams = @{
        Configuration = $BuildConfiguration
    }

    if ($OmitTests) {
        $publishParams.SkipTests = $true
    }

    & $publishScript @publishParams | Out-Host

    $latestZip = Get-ChildItem -Path (Join-Path $RepoRoot "DeploymentBundle") -Filter "QuizAPI_IIS_Production_*.zip" |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if ($null -eq $latestZip) {
        throw "Publish step completed but no deployment bundle was found."
    }

    return $latestZip.FullName
}

function Get-ScriptParameterNames {
    param([string]$ScriptPath)

    $command = Get-Command -Name $ScriptPath -CommandType ExternalScript -ErrorAction Stop
    return @($command.Parameters.Keys)
}

function Invoke-DeployScriptSafely {
    param(
        [string]$ScriptPath,
        [hashtable]$Parameters
    )

    try {
        & $ScriptPath @Parameters
        return
    }
    catch [System.Management.Automation.ParameterBindingException] {
        $message = $_.Exception.Message
        $optionalKeys = @("Protocol", "Port", "CertificateThumbprint", "UseRootHttpsBinding")
        $removedAny = $false

        foreach ($key in $optionalKeys) {
            if ($Parameters.ContainsKey($key) -and $message -like "*parameter name '$key'*") {
                $Parameters.Remove($key)
                $removedAny = $true
            }
        }

        if (-not $removedAny) {
            throw
        }

        Write-Warning "Deploy script rejected one or more optional parameters. Retrying with backward-compatible parameter set."
        & $ScriptPath @Parameters
    }
}

function New-PostInstallReport {
    param(
        [string]$DestinationPath,
        [string]$RepoRoot,
        [string]$DeploymentZip,
        [string]$TranscriptPath,
        [string]$ApplicationUrl,
        [bool]$JwtKeyGenerated,
        [string]$SavedJwtKeyPath
    )

    $sqlService = Get-Service -Name "MSSQL`$$SqlInstanceName" -ErrorAction SilentlyContinue
    $sdkList = @()
    $runtimeList = @()

    try {
        $dotnetPath = Get-DotNetCommandPath
        $sdkList = & $dotnetPath --list-sdks 2>$null
        $runtimeList = & $dotnetPath --list-runtimes 2>$null
    }
    catch {
    }

    $siteSummary = $null
    if (Get-Module -ListAvailable -Name WebAdministration) {
        try {
            Import-Module WebAdministration -ErrorAction Stop
            if (Test-Path "IIS:\Sites\$SiteName") {
                $siteItem = Get-Item "IIS:\Sites\$SiteName"
                $bindings = Get-WebBinding -Name $SiteName | ForEach-Object {
                    "- {0}" -f $_.bindingInformation
                }

                $siteSummary = @{
                    Name = $SiteName
                    PhysicalPath = $siteItem.physicalPath
                    ApplicationPool = $siteItem.applicationPool
                    Bindings = $bindings
                }
            }
        }
        catch {
        }
    }

    $reportLines = @(
        "# TheCertMaster Bootstrap Report",
        "",
        "Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')",
        "Machine: $env:COMPUTERNAME",
        "User: $env:USERDOMAIN\$env:USERNAME",
        "",
        "## Result",
        "",
        "- Protocol: $Protocol",
        "- Port: $Port",
        "- HostName: $(if ([string]::IsNullOrWhiteSpace($HostName)) { 'localhost' } else { $HostName })",
        "- Application URL: $ApplicationUrl",
        "- Site Name: $SiteName",
        "- Site Path: $SitePath",
        "- App Pool: $AppPoolName",
        "- Repo Root: $RepoRoot",
        "- Deployment Zip: $DeploymentZip",
        "- Transcript Log: $TranscriptPath",
        "- SQL Instance: $SqlInstanceName",
        "- SQL Server Instance: $SqlServerInstance",
        "- Database Name: $DatabaseName",
        "- Database Backup Path: $(if ([string]::IsNullOrWhiteSpace($DatabaseBackupPath)) { 'not supplied' } else { $DatabaseBackupPath })",
        "- Database Restore Mode: $DatabaseRestoreMode",
        "- Connection String: $ConnectionString",
        "- JWT Key Generated By Bootstrap: $JwtKeyGenerated",
        "- JWT Recovery File: $(if ([string]::IsNullOrWhiteSpace($SavedJwtKeyPath)) { 'not written' } else { $SavedJwtKeyPath })",
        "",
        "## Installed Components",
        "",
        "- IIS Install Attempted: $(-not $SkipIisInstall)",
        "- SQL Express Install Attempted: $(-not $SkipSqlExpressInstall)",
        "- Database Restore Attempted: $(-not $SkipDatabaseRestore)",
        "- .NET SDK Install Attempted: $(-not $SkipDotNetSdkInstall)",
        "- Hosting Bundle Install Attempted: $(-not $SkipHostingBundleInstall)",
        "- Database Update Attempted: $(-not $SkipDatabaseUpdate)",
        "- Deploy Attempted: $(-not $SkipDeploy)",
        "- Smoke Test Account Check: $SmokeTestAccountCheck",
        "",
        "## Prepared Paths",
        "",
        "- Downloads Root: $DownloadsRoot",
        "- Workspace Root: $WorkspaceRoot",
        "- Log Root: $LogRoot",
        "- Site Root: $SitePath",
        "",
        "## SQL Service",
        "",
        "- Service Found: $([bool]$sqlService)",
        "- Service Status: $(if ($sqlService) { $sqlService.Status } else { 'NotFound' })",
        "",
        "## dotnet SDKs",
        ""
    )

    if ($sdkList.Count -gt 0) {
        $reportLines += $sdkList | ForEach-Object { "- $_" }
    }
    else {
        $reportLines += "- none detected"
    }

    $reportLines += @(
        "",
        "## dotnet Runtimes",
        ""
    )

    if ($runtimeList.Count -gt 0) {
        $reportLines += $runtimeList | ForEach-Object { "- $_" }
    }
    else {
        $reportLines += "- none detected"
    }

    if ($null -ne $siteSummary) {
        $reportLines += @(
            "",
            "## IIS Site",
            "",
            "- Name: $($siteSummary.Name)",
            "- Physical Path: $($siteSummary.PhysicalPath)",
            "- Application Pool: $($siteSummary.ApplicationPool)",
            "- Bindings:"
        )
        if ($siteSummary.Bindings.Count -gt 0) {
            $reportLines += $siteSummary.Bindings
        }
        else {
            $reportLines += "- none found"
        }
    }

    Set-Content -Path $DestinationPath -Value $reportLines -Encoding UTF8
}

$transcriptStarted = $false
$deploymentZip = ""
$repoRoot = Join-Path $WorkspaceRoot $RepoFolderName
$timestamp = Get-Timestamp
$jwtKeyGenerated = $false
$savedJwtKeyPath = ""

Resolve-DeploymentInputs

Ensure-Directory -Path $DownloadsRoot
Ensure-Directory -Path $WorkspaceRoot
Ensure-Directory -Path $LogRoot

$transcriptPath = Join-Path $LogRoot "bootstrap-$timestamp.log"
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $LogRoot "bootstrap-report-$timestamp.md"
}

if ($SaveGeneratedJwtKey -and [string]::IsNullOrWhiteSpace($GeneratedJwtKeyPath)) {
    $GeneratedJwtKeyPath = Join-Path $LogRoot "secrets\jwtkey-$timestamp.txt"
}

Prepare-EnvironmentPaths `
    -DownloadsRootPath $DownloadsRoot `
    -WorkspaceRootPath $WorkspaceRoot `
    -LogRootPath $LogRoot `
    -SiteRootPath $SitePath `
    -RepoName $RepoFolderName `
    -JwtRecoveryPath $GeneratedJwtKeyPath `
    -BackupFilePath $DatabaseBackupPath

try {
    Start-Transcript -Path $transcriptPath -Force | Out-Null
    $transcriptStarted = $true

    Ensure-RunningAsAdministrator

    if ([string]::IsNullOrWhiteSpace($JwtKey)) {
        Write-Step "Generating a strong JWT signing key"
        $JwtKey = New-RandomJwtKey
        $jwtKeyGenerated = $true
    }

    if ($JwtKey.Length -lt 32) {
        throw "JwtKey must be at least 32 characters long."
    }

    if ($jwtKeyGenerated -and $SaveGeneratedJwtKey) {
        if ([string]::IsNullOrWhiteSpace($GeneratedJwtKeyPath)) {
            $GeneratedJwtKeyPath = Join-Path $LogRoot "secrets\jwtkey-$timestamp.txt"
        }

        Write-Step "Saving generated JWT key recovery copy"
        Save-GeneratedJwtKeyFile -Path $GeneratedJwtKeyPath -KeyValue $JwtKey
        $savedJwtKeyPath = $GeneratedJwtKeyPath
    }

    if ($Protocol -eq "https" -and [string]::IsNullOrWhiteSpace($HostName)) {
        throw "HostName is required when using HTTPS."
    }

    if ($Port -lt 1 -or $Port -gt 65535) {
        throw "Port must be between 1 and 65535."
    }

    $releaseMetadata = Get-DotNetReleaseMetadata -Channel $DotNetChannel

    if (-not $SkipIisInstall) {
        Install-IisRoleServices
    }

    if (-not $SkipDotNetSdkInstall) {
        Install-DotNetSdk -ReleaseMetadata $releaseMetadata -DestinationRoot $DownloadsRoot
    }

    if (-not $SkipHostingBundleInstall) {
        Install-HostingBundle -ReleaseMetadata $releaseMetadata -DestinationRoot $DownloadsRoot
    }

    if (-not $SkipSqlExpressInstall) {
        Install-SqlExpress -DestinationRoot $DownloadsRoot -InstanceName $SqlInstanceName -DownloadUrl $SqlExpressDownloadUrl
    }

    if (-not $SkipDatabaseRestore) {
        Restore-DatabaseFromBackup -ServerInstance $SqlServerInstance -Database $DatabaseName -BackupPath $DatabaseBackupPath -RestoreMode $DatabaseRestoreMode
    }

    if (-not $SkipSourceDownload) {
        $repoRoot = Expand-RepoZip -ZipUrl $RepoZipUrl -DestinationRoot $WorkspaceRoot -FolderName $RepoFolderName -DownloadRoot $DownloadsRoot
    }
    elseif (-not (Test-Path -LiteralPath $repoRoot)) {
        throw "SkipSourceDownload was used, but the repo folder does not exist at $repoRoot."
    }

    if (-not $SkipDatabaseUpdate) {
        Invoke-DatabaseUpdate -RepoRoot $repoRoot -Connection $ConnectionString
    }

    Grant-AppPoolDatabaseAccess -ServerInstance $SqlServerInstance -Database $DatabaseName -AppPool $AppPoolName

    $deploymentZip = Invoke-PublishPackage -RepoRoot $repoRoot -BuildConfiguration $Configuration -OmitTests:$SkipTests

if (-not $SkipDeploy) {
    $deployScript = Join-Path $scriptRoot "Deploy-IISProduction.ps1"
    $deployScriptParameters = Get-ScriptParameterNames -ScriptPath $deployScript
    $deployParams = @{
        ZipPath = $deploymentZip
        SiteName = $SiteName
        SitePath = $SitePath
        HostName = $HostName
        AppPoolName = $AppPoolName
        ConnectionString = $ConnectionString
        JwtIssuer = $JwtIssuer
        JwtAudience = $JwtAudience
        JwtKey = $JwtKey
        JwtAccessTokenMinutes = $JwtAccessTokenMinutes
    }

    if ($deployScriptParameters -contains "Protocol") {
        $deployParams.Protocol = $Protocol
    }

    if ($deployScriptParameters -contains "Port") {
        $deployParams.Port = $Port
    }

    if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        if ($deployScriptParameters -contains "CertificateThumbprint") {
            $deployParams.CertificateThumbprint = $CertificateThumbprint
        }
    }

    if ($UseRootHttpsBinding) {
        if ($deployScriptParameters -contains "UseRootHttpsBinding") {
            $deployParams.UseRootHttpsBinding = $true
        }
    }

    Write-Step "Deploying the site to IIS"
    Invoke-DeployScriptSafely -ScriptPath $deployScript -Parameters $deployParams
}

    $finalUrl = "${Protocol}://"
    if (-not [string]::IsNullOrWhiteSpace($HostName)) {
        $finalUrl += $HostName
    }
    else {
        $finalUrl += "localhost"
    }

    if (($Protocol -eq "http" -and $Port -ne 80) -or ($Protocol -eq "https" -and $Port -ne 443)) {
        $finalUrl += ":$Port"
    }

    $finalUrl += "/"

    New-PostInstallReport -DestinationPath $ReportPath -RepoRoot $repoRoot -DeploymentZip $deploymentZip -TranscriptPath $transcriptPath -ApplicationUrl $finalUrl -JwtKeyGenerated:$jwtKeyGenerated -SavedJwtKeyPath $savedJwtKeyPath

    if ($SmokeTestAccountCheck) {
        Test-SmokeTestAccount -ServerInstance $SqlServerInstance -Database $DatabaseName -Email $SmokeTestAdminEmail
    }

    Write-Step "Bootstrap complete"
    Write-Host "Repository root: $repoRoot"
    Write-Host "Deployment package: $deploymentZip"
    Write-Host "Transcript log: $transcriptPath"
    Write-Host "Post-install report: $ReportPath"
    if (-not $SkipDeploy) {
        Write-Host "Application URL: $finalUrl"
    }
}
finally {
    if ($transcriptStarted) {
        Stop-Transcript | Out-Null
    }
}

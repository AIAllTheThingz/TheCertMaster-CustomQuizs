[CmdletBinding()]
param(
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"

function Coalesce {
    param(
        $Primary,
        $Fallback = ""
    )

    if ($null -ne $Primary -and $Primary -ne "") {
        return $Primary
    }

    return $Fallback
}

function Get-HostingBundleInfo {
    $paths = @(
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Updates\.NET",
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    )

    $results = New-Object System.Collections.Generic.List[object]

    foreach ($path in $paths) {
        if (-not (Test-Path $path)) { continue }

        Get-ChildItem -Path $path -ErrorAction SilentlyContinue | ForEach-Object {
            try {
                $item = Get-ItemProperty -Path $_.PSPath -ErrorAction Stop
                $name = [string](Coalesce $item.DisplayName (Coalesce $item.PackageName ""))
                if ($name -match "Hosting Bundle|ASP\.NET Core Runtime|Windows Server Hosting") {
                    $results.Add([pscustomobject]@{
                        Name = $name
                        Version = [string](Coalesce $item.DisplayVersion (Coalesce $item.Version ""))
                        Publisher = [string](Coalesce $item.Publisher "")
                        RegistryPath = $_.PSPath
                    })
                }
            } catch {
            }
        }
    }

    $results | Sort-Object Name, Version -Unique
}

function Get-DotNetCommandOutput {
    param([string[]]$Arguments)

    try {
        $output = & dotnet @Arguments 2>$null
        return @($output)
    } catch {
        return @()
    }
}

function Get-AspNetCoreModuleInfo {
    $dllPath = Join-Path $env:ProgramFiles "IIS\Asp.Net Core Module\V2\aspnetcorev2.dll"
    if (-not (Test-Path $dllPath)) {
        return $null
    }

    $file = Get-Item $dllPath
    [pscustomobject]@{
        Path = $dllPath
        Version = $file.VersionInfo.FileVersion
        ProductVersion = $file.VersionInfo.ProductVersion
        LastWriteTime = $file.LastWriteTime
    }
}

function Format-CertificateHash {
    param($Value)

    if ($null -eq $Value) {
        return ""
    }

    if ($Value -is [byte[]]) {
        return [System.BitConverter]::ToString($Value).Replace("-", "")
    }

    return [string]$Value
}

Import-Module WebAdministration

$appPools = Get-ChildItem IIS:\AppPools | ForEach-Object {
    [pscustomobject]@{
        Name = $_.Name
        ManagedRuntimeVersion = $_.managedRuntimeVersion
        ManagedPipelineMode = $_.managedPipelineMode
        IdentityType = $_.processModel.identityType
        AutoStart = $_.autoStart
        State = $_.state
        StartMode = $_.startMode
        QueueLength = $_.queueLength
        Enable32BitAppOnWin64 = $_.enable32BitAppOnWin64
    }
}

$sites = Get-Website | ForEach-Object {
    $site = $_
    $siteBindings = @($site.Bindings.Collection | ForEach-Object {
        [pscustomobject]@{
            Protocol = $_.protocol
            BindingInformation = $_.bindingInformation
            HostHeader = ($_.bindingInformation -split ":")[-1]
            CertificateHash = Format-CertificateHash $_.certificateHash
            CertificateStoreName = [string](Coalesce $_.certificateStoreName "")
            SslFlags = [string](Coalesce $_.sslFlags "")
        }
    })

    [pscustomobject]@{
        Name = $site.Name
        Id = $site.Id
        State = $site.State
        PhysicalPath = $site.physicalPath
        ApplicationPool = $site.applicationPool
        ServerAutoStart = $site.serverAutoStart
        Bindings = $siteBindings
    }
}

$applications = Get-WebApplication | ForEach-Object {
    $parentPath = [string](Coalesce $_.PSParentPath "")
    $siteName = ""
    if (-not [string]::IsNullOrWhiteSpace($parentPath)) {
        $segments = $parentPath.Split("\")
        if ($segments.Length -gt 0) {
            $siteName = $segments[-1]
        }
    }

    [pscustomobject]@{
        Site = $siteName
        Path = $_.Path
        PhysicalPath = $_.PhysicalPath
        ApplicationPool = $_.ApplicationPool
    }
}

$globalModules = Get-WebGlobalModule | ForEach-Object {
    [pscustomobject]@{
        Name = $_.Name
        Image = $_.Image
    }
}

$windowsFeatures = @(
    "Web-Server",
    "Web-Asp-Net45",
    "Web-Mgmt-Tools",
    "Web-Scripting-Tools",
    "Web-WebServer",
    "Web-Http-Redirect",
    "Web-Static-Content",
    "Web-Default-Doc",
    "Web-Http-Errors"
) | ForEach-Object {
    try {
        $feature = Get-WindowsFeature -Name $_ -ErrorAction Stop
        [pscustomobject]@{
            Name = $feature.Name
            DisplayName = $feature.DisplayName
            Installed = $feature.Installed
        }
    } catch {
        [pscustomobject]@{
            Name = $_
            DisplayName = $_
            Installed = $false
        }
    }
}

$inventory = [pscustomobject]@{
    ComputerName = $env:COMPUTERNAME
    GeneratedUtc = (Get-Date).ToUniversalTime().ToString("O")
    OS = [pscustomobject]@{
        Caption = (Get-CimInstance Win32_OperatingSystem).Caption
        Version = (Get-CimInstance Win32_OperatingSystem).Version
    }
    IIS = [pscustomobject]@{
        Version = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\InetStp").VersionString
        AppPools = $appPools
        Sites = $sites
        Applications = $applications
        GlobalModules = $globalModules
        WindowsFeatures = $windowsFeatures
    }
    DotNet = [pscustomobject]@{
        SdkList = (Get-DotNetCommandOutput -Arguments @("--list-sdks"))
        RuntimeList = (Get-DotNetCommandOutput -Arguments @("--list-runtimes"))
        HostingBundles = Get-HostingBundleInfo
        AspNetCoreModuleV2 = Get-AspNetCoreModuleInfo
    }
}

$json = $inventory | ConvertTo-Json -Depth 8

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $json
} else {
    $directory = Split-Path -Path $OutputPath -Parent
    if ($directory) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    Set-Content -Path $OutputPath -Value $json -Encoding UTF8
    Write-Host "Wrote IIS inventory to $OutputPath" -ForegroundColor Green
}

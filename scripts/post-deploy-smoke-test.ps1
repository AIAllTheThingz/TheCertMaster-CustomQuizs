#requires -Version 5.1

[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost',
    [string]$AdminEmail = 'admin@quizapi.local',
    [string]$AdminPassword = 'Admin@123'
)

$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-ApiRequest {
    param(
        [string]$Uri,
        [string]$Method = 'Get',
        [string]$ContentType = '',
        [string]$Body = ''
    )

    try {
        if ([string]::IsNullOrWhiteSpace($ContentType)) {
            return Invoke-RestMethod -Uri $Uri -Method $Method
        }

        return Invoke-RestMethod -Uri $Uri -Method $Method -ContentType $ContentType -Body $Body
    }
    catch {
        $message = $_.Exception.Message
        throw "Request failed for $Method $Uri. $message"
    }
}

function Wait-ForHealthEndpoint {
    param(
        [string]$Uri,
        [int]$TimeoutSeconds = 60
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        try {
            return Invoke-RestMethod -Uri $Uri -Method Get
        }
        catch {
            Start-Sleep -Seconds 2
        }
    }
    while ((Get-Date) -lt $deadline)

    throw "Health endpoint did not become available within $TimeoutSeconds seconds: $Uri"
}

function Write-StartupDiagnostics {
    param([string]$BaseUrlToUse)

    Write-Host ''
    Write-Host 'Startup diagnostics:' -ForegroundColor Yellow

    $siteRoot = 'C:\sites\QuizAPI\current'
    $logsPath = Join-Path $siteRoot 'logs'
    $webConfigPath = Join-Path $siteRoot 'web.config'

    if (Test-Path -LiteralPath $webConfigPath) {
        Write-Host "web.config: $webConfigPath" -ForegroundColor Yellow
    }

    if (Test-Path -LiteralPath $logsPath) {
        $latestLog = Get-ChildItem -Path $logsPath -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1

        if ($latestLog) {
            Write-Host "Latest app log: $($latestLog.FullName)" -ForegroundColor Yellow
            Get-Content -Path $latestLog.FullName -Tail 40 | Out-Host
        }
        else {
            Write-Host "No app logs found in $logsPath" -ForegroundColor Yellow
        }
    }
    else {
        Write-Host "Logs folder not found: $logsPath" -ForegroundColor Yellow
    }

    try {
        $recentEvents = Get-WinEvent -LogName Application -MaxEvents 20 -ErrorAction Stop |
            Where-Object {
                $_.ProviderName -like '*IIS AspNetCore Module*' -or
                $_.ProviderName -like '*ASP.NET Core*' -or
                $_.Message -like '*QuizAPI*'
            } |
            Select-Object -First 5

        if ($recentEvents) {
            Write-Host 'Recent IIS/Application events:' -ForegroundColor Yellow
            foreach ($event in $recentEvents) {
                Write-Host ('[' + $event.TimeCreated + '] ' + $event.ProviderName) -ForegroundColor Yellow
                Write-Host $event.Message
                Write-Host ''
            }
        }
    }
    catch {
        Write-Host 'Could not read Windows event logs in this session.' -ForegroundColor Yellow
    }

    throw "Health endpoint did not become available within 60 seconds: $BaseUrlToUse/health"
}

$normalizedBaseUrl = $BaseUrl.TrimEnd('/')

Write-Step 'Checking health endpoint'
$healthUri = $normalizedBaseUrl + '/health'
try {
    $health = Wait-ForHealthEndpoint -Uri $healthUri
}
catch {
    Write-StartupDiagnostics -BaseUrlToUse $normalizedBaseUrl
}
Write-Host "Health response: $health" -ForegroundColor Green

Write-Step 'Checking admin login'
$loginBody = @{
    email = $AdminEmail
    password = $AdminPassword
} | ConvertTo-Json

$loginResponse = Invoke-ApiRequest -Uri ($normalizedBaseUrl + '/api/auth/login') -Method Post -ContentType 'application/json' -Body $loginBody
if ([string]::IsNullOrWhiteSpace($loginResponse.token)) {
    throw 'Admin login did not return a JWT token.'
}

Write-Host 'Smoke test passed.' -ForegroundColor Green

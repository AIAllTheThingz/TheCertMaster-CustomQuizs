#requires -Version 5.1

[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://WIN2K22IIS01',
    [string]$AdminEmail = '',
    [string]$AdminPassword = '',
    [switch]$SkipQuizCatalogCheck,
    [switch]$RunBrowserSmokeTest,
    [string]$NodePath = 'node',
    [string]$BrowserPath = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($AdminEmail)) {
    throw 'AdminEmail is required. Pass the bootstrap admin email you configured for this deployment.'
}

if ([string]::IsNullOrWhiteSpace($AdminPassword)) {
    throw 'AdminPassword is required. Pass the non-default bootstrap admin password configured for this deployment.'
}

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
        [string]$Body = '',
        [hashtable]$Headers = $null
    )

    try {
        if ([string]::IsNullOrWhiteSpace($ContentType)) {
            if ($null -eq $Headers) {
                return Invoke-RestMethod -Uri $Uri -Method $Method
            }

            return Invoke-RestMethod -Uri $Uri -Method $Method -Headers $Headers
        }

        if ($null -eq $Headers) {
            return Invoke-RestMethod -Uri $Uri -Method $Method -ContentType $ContentType -Body $Body
        }

        return Invoke-RestMethod -Uri $Uri -Method $Method -ContentType $ContentType -Body $Body -Headers $Headers
    }
    catch {
        $message = $_.Exception.Message
        throw "Request failed for $Method $Uri. $message"
    }
}

function Wait-ForHealthEndpoint {
    param(
        [string]$Uri,
        [hashtable]$Headers = $null,
        [int]$TimeoutSeconds = 60
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        try {
            if ($null -eq $Headers) {
                return Invoke-RestMethod -Uri $Uri -Method Get
            }

            return Invoke-RestMethod -Uri $Uri -Method Get -Headers $Headers
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
$baseUri = [Uri]$normalizedBaseUrl
$health = $null

try {
    $health = Wait-ForHealthEndpoint -Uri $healthUri
}
catch {
    if ($baseUri.Host -notin @('localhost', '127.0.0.1', '::1')) {
        Write-Host "Primary health check failed for $healthUri. Retrying via loopback with Host header '$($baseUri.Host)'." -ForegroundColor Yellow
        $loopbackBaseUrl = '{0}://127.0.0.1{1}' -f $baseUri.Scheme, $(if ($baseUri.IsDefaultPort) { '' } else { ':' + $baseUri.Port })
        $loopbackHealthUri = $loopbackBaseUrl.TrimEnd('/') + '/health'

        try {
            $health = Wait-ForHealthEndpoint -Uri $loopbackHealthUri -Headers @{ Host = $baseUri.Host } -TimeoutSeconds 30
            $normalizedBaseUrl = $loopbackBaseUrl
            Write-Host "Loopback health check succeeded using Host header '$($baseUri.Host)'." -ForegroundColor Green
        }
        catch {
            Write-StartupDiagnostics -BaseUrlToUse $normalizedBaseUrl
        }
    }
    else {
        Write-StartupDiagnostics -BaseUrlToUse $normalizedBaseUrl
    }
}
Write-Host "Health response: $health" -ForegroundColor Green

Write-Step 'Checking admin login'
$loginBody = @{
    email = $AdminEmail
    password = $AdminPassword
} | ConvertTo-Json

$requestHeaders = $null
if ($baseUri.Host -notin @('localhost', '127.0.0.1', '::1') -and $normalizedBaseUrl -like 'http://127.0.0.1*') {
    $requestHeaders = @{ Host = $baseUri.Host }
}

$loginResponse = Invoke-ApiRequest -Uri ($normalizedBaseUrl + '/api/auth/login') -Method Post -ContentType 'application/json' -Body $loginBody -Headers $requestHeaders
if ([string]::IsNullOrWhiteSpace($loginResponse.token)) {
    throw 'Admin login did not return a JWT token.'
}

if ($SkipQuizCatalogCheck) {
    Write-Step 'Checking quiz catalog'
    Write-Host 'Quiz catalog check skipped for migration-only install.' -ForegroundColor Yellow
}
else {
    Write-Step 'Checking quiz catalog'
    $quizHeaders = @{ Authorization = 'Bearer ' + $loginResponse.token }
    if ($null -ne $requestHeaders -and $requestHeaders.ContainsKey('Host')) {
        $quizHeaders['Host'] = $requestHeaders['Host']
    }
    $quizResponse = Invoke-ApiRequest -Uri ($normalizedBaseUrl + '/api/quiz') -Method Get -Headers $quizHeaders
    $quizCount = @($quizResponse).Count
    if ($quizCount -lt 1) {
        throw 'Quiz catalog check failed. No quizzes were returned after deployment.'
    }

    Write-Host "Quiz count: $quizCount" -ForegroundColor Green
}

Write-Host 'Smoke test passed.' -ForegroundColor Green

if ($RunBrowserSmokeTest) {
    Write-Step 'Running browser UI smoke test'

    $browserSmokeScript = Join-Path $PSScriptRoot 'browser-smoke-test.mjs'
    if (!(Test-Path -LiteralPath $browserSmokeScript)) {
        throw "Browser smoke test script not found: $browserSmokeScript"
    }

    $nodeArgs = @(
        $browserSmokeScript,
        "--base-url=$BaseUrl",
        "--admin-email=$AdminEmail",
        "--admin-password=$AdminPassword"
    )

    if (![string]::IsNullOrWhiteSpace($BrowserPath)) {
        $nodeArgs += "--browser-path=$BrowserPath"
    }

    & $NodePath @nodeArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Browser UI smoke test failed with exit code $LASTEXITCODE."
    }
}

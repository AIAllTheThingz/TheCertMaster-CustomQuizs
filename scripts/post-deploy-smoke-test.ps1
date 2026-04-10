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

$normalizedBaseUrl = $BaseUrl.TrimEnd('/')

Write-Step 'Checking health endpoint'
$health = Wait-ForHealthEndpoint -Uri ($normalizedBaseUrl + '/health')
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

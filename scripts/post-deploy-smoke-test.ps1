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

$normalizedBaseUrl = $BaseUrl.TrimEnd('/')

Write-Step 'Checking health endpoint'
$health = Invoke-RestMethod -Uri ($normalizedBaseUrl + '/health') -Method Get
Write-Host "Health response: $health" -ForegroundColor Green

Write-Step 'Checking admin login'
$loginBody = @{
    email = $AdminEmail
    password = $AdminPassword
} | ConvertTo-Json

$loginResponse = Invoke-RestMethod -Uri ($normalizedBaseUrl + '/api/auth/login') -Method Post -ContentType 'application/json' -Body $loginBody
if ([string]::IsNullOrWhiteSpace($loginResponse.token)) {
    throw 'Admin login did not return a JWT token.'
}

Write-Host 'Smoke test passed.' -ForegroundColor Green

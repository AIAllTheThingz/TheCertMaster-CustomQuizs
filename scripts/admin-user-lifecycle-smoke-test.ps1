#requires -Version 5.1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$BaseUrl = 'http://WIN2K22IIS01',
    [Parameter(Mandatory = $true)]
    [string]$AdminEmail,
    [Parameter(Mandatory = $true)]
    [string]$AdminPassword,
    [string]$DisposableEmail = '',
    [string]$DisposablePassword = '',
    [string]$ResetPassword = '',
    [switch]$RunSmtpTest,
    [string]$SmtpTestTo = '',
    [switch]$RunActiveDirectoryTest,
    [switch]$SkipCleanup
)

$ErrorActionPreference = 'Stop'

function New-TestPassword {
    $suffix = [Guid]::NewGuid().ToString('N').Substring(0, 12)
    return "Tcm!$suffix" + '9aA'
}

function Write-Step {
    param([string]$Message)
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-JsonRequest {
    param(
        [string]$Uri,
        [string]$Method = 'Get',
        [object]$Body = $null,
        [hashtable]$Headers = $null
    )

    $params = @{
        Uri = $Uri
        Method = $Method
    }

    if ($null -ne $Headers) {
        $params.Headers = $Headers
    }

    if ($null -ne $Body) {
        $params.ContentType = 'application/json'
        $params.Body = ($Body | ConvertTo-Json -Depth 8)
    }

    try {
        return Invoke-RestMethod @params
    }
    catch {
        $message = $_.Exception.Message
        $response = $_.Exception.Response
        if ($null -ne $response) {
            try {
                $reader = New-Object System.IO.StreamReader($response.GetResponseStream())
                $details = $reader.ReadToEnd()
                if (![string]::IsNullOrWhiteSpace($details)) {
                    $message = "$message $details"
                }
            }
            catch {
                # Keep the original exception message.
            }
        }

        throw "Request failed for $Method $Uri. $message"
    }
}

function Get-Users {
    param(
        [string]$ApiBase,
        [hashtable]$Headers
    )

    return @(Invoke-JsonRequest -Uri "$ApiBase/api/users" -Headers $Headers)
}

$apiBase = $BaseUrl.TrimEnd('/')
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss')
if ([string]::IsNullOrWhiteSpace($DisposableEmail)) {
    $DisposableEmail = "codex-disposable-$stamp@quizapi.local"
}
if ([string]::IsNullOrWhiteSpace($DisposablePassword)) {
    $DisposablePassword = New-TestPassword
}
if ([string]::IsNullOrWhiteSpace($ResetPassword)) {
    $ResetPassword = New-TestPassword
}

Write-Step 'Logging in as admin'
$loginResponse = Invoke-JsonRequest -Uri "$apiBase/api/auth/login" -Method Post -Body @{
    email = $AdminEmail
    password = $AdminPassword
}

if ([string]::IsNullOrWhiteSpace($loginResponse.token)) {
    throw 'Admin login did not return a JWT token.'
}

$headers = @{ Authorization = 'Bearer ' + $loginResponse.token }
Write-Host "Authenticated against $apiBase." -ForegroundColor Green

Write-Step 'Reading current admin-side settings'
$smtp = Invoke-JsonRequest -Uri "$apiBase/api/admin/smtp" -Headers $headers
$ad = Invoke-JsonRequest -Uri "$apiBase/api/admin/active-directory" -Headers $headers
Write-Host ("SMTP configured: host={0}, from={1}, notification={2}, passwordSet={3}" -f $smtp.host, $smtp.fromEmail, $smtp.notificationEmail, $smtp.passwordSet) -ForegroundColor Green
Write-Host ("Active Directory configured: enabled={0}, host={1}, port={2}, sslPort={3}" -f $ad.enabled, $ad.host, $ad.port, $ad.sslPort) -ForegroundColor Green

Write-Step "Creating disposable user $DisposableEmail"
$existing = Get-Users -ApiBase $apiBase -Headers $headers | Where-Object { $_.email -eq $DisposableEmail }
if ($existing) {
    throw "Disposable user already exists: $DisposableEmail. Choose another -DisposableEmail or clean it up first."
}

if ($PSCmdlet.ShouldProcess($DisposableEmail, 'create disposable user with role User')) {
    Invoke-JsonRequest -Uri "$apiBase/api/users" -Method Post -Headers $headers -Body @{
        email = $DisposableEmail
        firstName = 'Codex'
        lastName = 'Disposable'
        password = $DisposablePassword
        role = 'User'
    } | Out-Host
}

Write-Step 'Changing disposable user role to Admin'
if ($PSCmdlet.ShouldProcess($DisposableEmail, 'change role to Admin')) {
    Invoke-JsonRequest -Uri "$apiBase/api/users/$([uri]::EscapeDataString($DisposableEmail))/role" -Method Put -Headers $headers -Body @{
        role = 'Admin'
    } | Out-Host
}

Write-Step 'Changing disposable user role back to User'
if ($PSCmdlet.ShouldProcess($DisposableEmail, 'change role back to User')) {
    Invoke-JsonRequest -Uri "$apiBase/api/users/$([uri]::EscapeDataString($DisposableEmail))/role" -Method Put -Headers $headers -Body @{
        role = 'User'
    } | Out-Host
}

Write-Step 'Resetting disposable user password'
if ($PSCmdlet.ShouldProcess($DisposableEmail, 'reset password')) {
    Invoke-JsonRequest -Uri "$apiBase/api/users/$([uri]::EscapeDataString($DisposableEmail))/reset-password" -Method Post -Headers $headers -Body @{
        newPassword = $ResetPassword
    } | Out-Host
}

Write-Step 'Verifying disposable user can sign in after reset'
if ($PSCmdlet.ShouldProcess($DisposableEmail, 'verify reset password by signing in')) {
    $disposableLogin = Invoke-JsonRequest -Uri "$apiBase/api/auth/login" -Method Post -Body @{
        email = $DisposableEmail
        password = $ResetPassword
    }
    if ([string]::IsNullOrWhiteSpace($disposableLogin.token)) {
        throw 'Disposable user login did not return a JWT token after reset.'
    }
    Write-Host 'Disposable user login succeeded after reset.' -ForegroundColor Green
}

if ($RunSmtpTest) {
    if ([string]::IsNullOrWhiteSpace($SmtpTestTo)) {
        $SmtpTestTo = $smtp.notificationEmail
    }
    if ([string]::IsNullOrWhiteSpace($SmtpTestTo)) {
        throw 'RunSmtpTest was requested, but no SmtpTestTo value was provided and SMTP notificationEmail is blank.'
    }

    Write-Step "Sending SMTP test email to $SmtpTestTo"
    if ($PSCmdlet.ShouldProcess($SmtpTestTo, 'send SMTP test email')) {
        Invoke-JsonRequest -Uri "$apiBase/api/admin/smtp/test" -Method Post -Headers $headers -Body @{
            toEmail = $SmtpTestTo
        } | Out-Host
    }
}
else {
    Write-Host 'SMTP test skipped. Pass -RunSmtpTest and -SmtpTestTo to send a test email.' -ForegroundColor Yellow
}

if ($RunActiveDirectoryTest) {
    Write-Step 'Testing Active Directory connectivity'
    if ($PSCmdlet.ShouldProcess($ad.host, 'test LDAP/LDAPS connectivity')) {
        Invoke-JsonRequest -Uri "$apiBase/api/admin/active-directory/test" -Method Post -Headers $headers | ConvertTo-Json -Depth 8 | Out-Host
    }
}
else {
    Write-Host 'Active Directory test skipped. Pass -RunActiveDirectoryTest to test configured LDAP endpoints.' -ForegroundColor Yellow
}

if ($SkipCleanup) {
    Write-Host "Cleanup skipped. Disposable user remains: $DisposableEmail" -ForegroundColor Yellow
}
else {
    Write-Step "Deleting disposable user $DisposableEmail"
    if ($PSCmdlet.ShouldProcess($DisposableEmail, 'delete disposable user')) {
        Invoke-JsonRequest -Uri "$apiBase/api/users/$([uri]::EscapeDataString($DisposableEmail))" -Method Delete -Headers $headers | Out-Host
    }

    $remaining = Get-Users -ApiBase $apiBase -Headers $headers | Where-Object { $_.email -eq $DisposableEmail }
    if ($remaining) {
        throw "Cleanup failed. Disposable user still exists: $DisposableEmail"
    }

    Write-Host 'Cleanup verified: disposable user no longer exists.' -ForegroundColor Green
}

Write-Host 'Admin user lifecycle smoke test completed.' -ForegroundColor Green

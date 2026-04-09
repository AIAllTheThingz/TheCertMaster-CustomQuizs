param(
    [string]$BaseUrl = "https://localhost:5001",
    [string]$AdminEmail = "",
    [string]$AdminPassword = "",
    [string]$AdminToken = "",
    [string]$PackagePath = "",
    [switch]$SkipPackageBuild
)

$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    return Split-Path -Parent $PSScriptRoot
}

function New-SamplePackage {
    param(
        [string]$SourceDir,
        [string]$ZipPath
    )

    if (Test-Path $ZipPath) {
        Remove-Item $ZipPath -Force
    }

    Compress-Archive -Path (Join-Path $SourceDir '*') -DestinationPath $ZipPath -CompressionLevel Optimal
}

function Get-AuthHeaders {
    param([string]$Token)

    return @{
        Authorization = "Bearer $Token"
    }
}

function Get-AdminToken {
    param(
        [string]$ApiBaseUrl,
        [string]$Email,
        [string]$Password
    )

    if ([string]::IsNullOrWhiteSpace($Email) -or [string]::IsNullOrWhiteSpace($Password)) {
        throw "Provide -AdminToken or both -AdminEmail and -AdminPassword."
    }

    $loginBody = @{
        Email = $Email
        Password = $Password
    } | ConvertTo-Json

    $response = Invoke-RestMethod -Method Post -Uri "$ApiBaseUrl/api/auth/login" -ContentType "application/json" -Body $loginBody
    if ([string]::IsNullOrWhiteSpace($response.token)) {
        throw "Login succeeded but no JWT token was returned."
    }

    return $response.token
}

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

$repoRoot = Get-RepoRoot
$sampleDir = Join-Path $repoRoot 'Samples\ImportPackage'
$resolvedPackagePath = if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    Join-Path $sampleDir 'sample-import-package.zip'
} else {
    $PackagePath
}

if (-not $SkipPackageBuild) {
    New-SamplePackage -SourceDir $sampleDir -ZipPath $resolvedPackagePath
}

if (-not (Test-Path $resolvedPackagePath)) {
    throw "Package not found: $resolvedPackagePath"
}

$token = if ([string]::IsNullOrWhiteSpace($AdminToken)) {
    Get-AdminToken -ApiBaseUrl $BaseUrl.TrimEnd('/') -Email $AdminEmail -Password $AdminPassword
} else {
    $AdminToken
}

$headers = Get-AuthHeaders -Token $token
$apiBase = $BaseUrl.TrimEnd('/')

Write-Host "Uploading sample package from $resolvedPackagePath"
$uploadResponse = Invoke-RestMethod -Method Post -Uri "$apiBase/api/import/upload-package" -Headers $headers -Form @{
    File = Get-Item $resolvedPackagePath
}

Assert-True -Condition (-not [string]::IsNullOrWhiteSpace($uploadResponse.CsvFileName)) -Message "Upload did not return CsvFileName."
Assert-True -Condition ($uploadResponse.ImagesSaved -ge 1) -Message "Upload package should contain at least one image."

Write-Host "Processing imported CSV $($uploadResponse.CsvFileName)"
$processResponse = Invoke-RestMethod -Method Post -Uri "$apiBase/api/import/process/$($uploadResponse.CsvFileName)" -Headers $headers
Write-Host $processResponse

Write-Host "Fetching quiz list for category Safety"
$quizList = Invoke-RestMethod -Method Get -Uri "$apiBase/api/quiz?category=Safety"
$sampleQuiz = $quizList | Where-Object { $_.Title -eq 'Sample Safety Quiz' } | Select-Object -First 1
Assert-True -Condition ($null -ne $sampleQuiz) -Message "Imported quiz 'Sample Safety Quiz' was not found."

Write-Host "Fetching randomized quiz payload"
$quizPayload = Invoke-RestMethod -Method Get -Uri "$apiBase/api/quiz/$($sampleQuiz.Id)/random"
Assert-True -Condition ($quizPayload.Questions.Count -ge 2) -Message "Expected at least two questions in randomized payload."

$imageQuestion = $quizPayload.Questions | Where-Object { $_.Images.Count -gt 0 } | Select-Object -First 1
Assert-True -Condition ($null -ne $imageQuestion) -Message "No question returned any images."

$image = $imageQuestion.Images | Select-Object -First 1
Assert-True -Condition (-not [string]::IsNullOrWhiteSpace($image.Url)) -Message "Imported image URL was empty."
Assert-True -Condition ($image.Url -match '^/uploads/images/.+/forklift-safety') -Message "Imported image URL did not point to the extracted package image."

Write-Host "Sample package import verified successfully."
Write-Host "QuizId: $($sampleQuiz.Id)"
Write-Host "ImageUrl: $($image.Url)"

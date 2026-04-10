[CmdletBinding()]
param(
    [string]$ServerInstance = ".\SQLEXPRESS",
    [string]$Database = "TheCertMasterCorporateDB"
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-SqlScalarLines {
    param([string]$Query)

    $raw = sqlcmd -S $ServerInstance -d $Database -Q $Query -W -h -1 2>$null
    return @($raw | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim() })
}

Write-Step "Reading migrations from source"
$migrationFiles = Get-ChildItem -Path (Join-Path $PSScriptRoot "..\Migrations") -Filter "*.cs" -File |
    Where-Object { $_.Name -notlike "*.Designer.cs" -and $_.Name -ne "QuizDbContextModelSnapshot.cs" } |
    Sort-Object Name

$expectedMigrationIds = @($migrationFiles | ForEach-Object { [System.IO.Path]::GetFileNameWithoutExtension($_.Name) })

Write-Step "Reading migrations from database"
$databaseMigrationIds = Invoke-SqlScalarLines -Query "SET NOCOUNT ON; SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;"

Write-Step "Checking required tables"
$requiredTables = @(
    "AspNetUsers",
    "Quizzes",
    "Questions",
    "Answers",
    "Images",
    "QuizAttempts",
    "PreEmploymentSubmissions",
    "QuizProgressEntries"
)

$existingTables = Invoke-SqlScalarLines -Query "SET NOCOUNT ON; SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME;"
$missingTables = @($requiredTables | Where-Object { $_ -notin $existingTables })

$missingMigrations = @($expectedMigrationIds | Where-Object { $_ -notin $databaseMigrationIds })
$extraMigrations = @($databaseMigrationIds | Where-Object { $_ -notin $expectedMigrationIds })

$result = [pscustomobject]@{
    ServerInstance = $ServerInstance
    Database = $Database
    ExpectedMigrationCount = $expectedMigrationIds.Count
    DatabaseMigrationCount = $databaseMigrationIds.Count
    ExpectedLatestMigration = if ($expectedMigrationIds.Count -gt 0) { $expectedMigrationIds[-1] } else { "" }
    DatabaseLatestMigration = if ($databaseMigrationIds.Count -gt 0) { $databaseMigrationIds[-1] } else { "" }
    MissingMigrations = $missingMigrations
    ExtraMigrations = $extraMigrations
    MissingTables = $missingTables
    Status = if ($missingMigrations.Count -eq 0 -and $extraMigrations.Count -eq 0 -and $missingTables.Count -eq 0) { "PASS" } else { "FAIL" }
}

Write-Step "Schema audit result"
$result | ConvertTo-Json -Depth 5

if ($result.Status -ne "PASS") {
    throw "Database schema audit failed. See output above."
}

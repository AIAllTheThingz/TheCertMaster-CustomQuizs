# Database And Schema Resilience

This document exists to reduce the chance of another environment drift issue when moving the application between machines, databases, or IIS deployments.

## Current Migration Chain

Expected source migration order:

1. `20251107025231_AddIdentity`
2. `20251230160000_AddQuizCategory`
3. `20260318201123_AddQuizAttempts`
4. `20260318205407_AddUserProfileNames`
5. `20260318214725_AddQuizArchivingAndPreEmploymentSubmissions`
6. `20260318224156_AddQuizProgressAndAssessmentMetadata`

## Required Operational Tables

The current application expects these tables to exist:

- `AspNetUsers`
- `Quizzes`
- `Questions`
- `Answers`
- `Images`
- `QuizAttempts`
- `PreEmploymentSubmissions`
- `QuizProgresses`

## Readiness Checks

The app now exposes:

- `/health` for general liveness
- `/health/ready` for database-backed readiness

Use `/health/ready` after deployment to confirm the application can reach the configured SQL database.

## Schema Audit Script

Run this before a deployment or after restoring a database to a new environment:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Test-DatabaseSchema.ps1 -ServerInstance .\SQLEXPRESS -Database DevQuizDB
```

What it checks:

- migration IDs in source vs `__EFMigrationsHistory`
- missing required tables
- unexpected migration drift between code and database

If the script fails, do not continue with deployment until the mismatch is understood.

## Deployment Guidance

- Do not enable automatic migrations casually on moved or restored databases.
- Validate the database with the schema audit script first.
- Keep database backups before any migration repair work.
- Preserve `App_Data` and `wwwroot/uploads` separately from database migration work.

## Recommended Release Pattern

1. Restore or provision the target database.
2. Run `Test-DatabaseSchema.ps1`.
3. Publish the app package.
4. Deploy to IIS.
5. Validate `/health`.
6. Validate `/health/ready`.
7. Perform the admin and user smoke checks.

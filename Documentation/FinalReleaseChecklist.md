# Final Release Checklist

Date:

- 2026-04-09

Application:

- TheCertMaster-CustomQuizs

## Build And Test

- [x] `dotnet build QuizAPI.sln -c Release`
- [x] `dotnet test QuizAPI.sln -c Release --no-build`
- [x] IIS deployment package creation succeeds via `scripts/Publish-IISPackage.ps1`

## Hosting And Runtime

- [x] Root site routing is in place for `/`, `/register.html`, `/user.html`, `/manage.html`, `/quiz.html`, `/preemployment.html`
- [x] `web.config` exists for IIS hosting
- [x] Runtime folders are created automatically at startup for `App_Data`, `wwwroot/uploads`, and `logs`
- [x] `/health` endpoint exists
- [x] `/health/ready` endpoint exists for database-backed readiness

## Security And Auth

- [x] JWT startup validation requires issuer, audience, key, and connection string
- [x] JWT key minimum length enforced
- [x] Login and registration rate limits are separated
- [x] Loopback and support-heavy local workflows have a higher auth limit
- [x] Admin login verified with `admin@quizapi.local`

## User Flows

- [x] Registration works
- [x] User login works
- [x] Profile load works
- [x] Quiz generation works
- [x] Quiz submission works
- [x] Quiz history updates after submission
- [x] Quiz progress save and restore works
- [x] Pre-employment quiz generation works
- [x] Pre-employment submission works

## Admin Flows

- [x] Management page loads
- [x] Quiz list loads for admin
- [x] Categories load
- [x] Import files load
- [x] Import history load
- [x] Import history CSV export works
- [x] User activity CSV export works
- [x] SMTP settings load
- [x] Pre-employment config load
- [x] Pre-employment submissions load
- [x] Usage report loads
- [x] Question Editor data loads
- [x] Quiz Creator source quizzes and paging load

## Database And Schema

- [x] Migration chain in source is documented
- [x] Schema audit script exists: `scripts/Test-DatabaseSchema.ps1`
- [x] `DevQuizDB` passes the schema audit
- [x] Latest migration in code matches latest migration in database

## Source Control Hygiene

- [x] Runtime data under `App_Data` is no longer tracked
- [x] Uploaded assets under `wwwroot/uploads` are no longer tracked
- [x] Sample package zip is no longer tracked
- [x] `.gitignore` updated for runtime and deployment artifacts

## Deployment Artifacts And Docs

- [x] Public README includes installation and deployment guidance
- [x] IIS runbook exists
- [x] Admin restoration checklist exists
- [x] Browser/admin release checklist exists
- [x] Database resilience documentation exists

## Manual Production Checks Still Recommended At Deployment Time

- [ ] Validate production TLS certificate binding
- [ ] Validate real SMTP send-test from the target environment
- [ ] Validate production database backup/restore path
- [ ] Validate real IIS app pool file permissions on target server
- [ ] Validate uploaded image rendering after deployment
- [ ] Validate admin login from the real production hostname

# Production Settings Reference

This document explains each setting in [production-settings.template.psd1](F:\repos\TheCrtMasterCorporate\scripts\production-settings.template.psd1), what it controls, and whether it is typically required or optional.

## How to use the template

1. Copy or edit [production-settings.template.psd1](F:\repos\TheCrtMasterCorporate\scripts\production-settings.template.psd1).
2. Fill in the values that apply to your target server.
3. Run:

```powershell
powershell.exe -ExecutionPolicy Bypass -File C:\repo\TheCertMaster-CustomQuizs\scripts\install-production-application.ps1 -SettingsFile C:\repo\TheCertMaster-CustomQuizs\scripts\production-settings.template.psd1
```

## IIS and deployment settings

`DeploymentRoot`
- Purpose: Working folder used by the install scripts for tools, temporary files, and artifacts.
- Typical value: `C:\Deployment`
- Required: Optional
- Notes: The default is fine for most installs.

`SiteName`
- Purpose: IIS site name.
- Typical value: `QuizAPI`
- Required: Optional
- Notes: Change only if your IIS naming standard requires it.

`AppPoolName`
- Purpose: IIS application pool name used by the site.
- Typical value: `QuizAppPool`
- Required: Optional
- Notes: Change only if you want a different IIS naming convention.

`SitePath`
- Purpose: Physical deployment path for the published site.
- Typical value: `C:\sites\QuizAPI\current`
- Required: Optional
- Notes: The installer creates the directory if it does not exist.

`Protocol`
- Purpose: Chooses whether the IIS site is bound for HTTP or HTTPS.
- Allowed values: `http`, `https`
- Required: Yes
- Notes: Use `http` for first-time internal/server validation. Use `https` when you have a real hostname and certificate.

`HttpPort`
- Purpose: HTTP binding port.
- Typical value: `80`
- Required: Optional
- Notes: Used when `Protocol = 'http'`.

`HttpsPort`
- Purpose: HTTPS binding port.
- Typical value: `443`
- Required: Optional
- Notes: Used when `Protocol = 'https'`.

`HostName`
- Purpose: IIS host header / DNS name / FQDN used for the site binding.
- Typical value: `quiz.company.com`
- Required: Optional for HTTP, typically required for HTTPS
- Notes: Leave blank for basic HTTP rollout if you plan to use `localhost` or the server IP.

`PublicBaseUrl`
- Purpose: URL used by the post-deploy smoke test.
- Typical value: `http://localhost`, `http://server01`, or `https://quiz.company.com`
- Required: Yes
- Notes: This should match how the installer should verify the deployed app.

`EnableHttpsRedirection`
- Purpose: Controls ASP.NET Core HTTPS redirect behavior.
- Required: Optional
- Notes: Set `false` for HTTP installs. Set `true` only when the final deployment really serves HTTPS.

## JWT / authentication settings

`JwtKey`
- Purpose: Signing key for JWT access tokens.
- Required: Optional
- Notes: Leave blank to auto-generate a strong key during install. Provide your own if you want a stable key across re-installs or farm nodes.

`JwtIssuer`
- Purpose: JWT issuer value.
- Typical value: `QuizAPI`
- Required: Optional
- Notes: Usually safe to leave at the default unless your environment requires a different issuer.

`JwtAudience`
- Purpose: JWT audience value.
- Typical value: `QuizAPIUsers`
- Required: Optional
- Notes: Usually safe to leave at the default unless your environment requires a different audience.

`JwtAccessTokenMinutes`
- Purpose: Access-token lifetime in minutes.
- Typical value: `60`
- Required: Optional
- Notes: Increase or decrease only if your sign-in policy requires it.

## SQL / database settings

`SqlInstance`
- Purpose: SQL Server instance used by the application.
- Typical value: `.\SQLEXPRESS`
- Required: Yes unless `ConnectionString` is explicitly set
- Notes: The installer uses this for restore and SQL permission steps.

`DatabaseName`
- Purpose: Target application database name.
- Typical value: `TheCertMasterCorporateDB`
- Required: Yes unless `ConnectionString` is explicitly set to a different database
- Notes: This is the restored and migrated application database.

`RestoreSeedDatabase`
- Purpose: Controls whether the installer restores the repository seed database before applying migrations.
- Typical value: `$true`
- Required: Optional
- Notes: Leave this enabled for standard packaged installs so quizzes and baseline content are present after deployment. If you disable it, the install becomes migration-only and the smoke test skips the quiz-count assertion.

`DatabaseBackupPath`
- Purpose: Path to the bundled seed backup used during install.
- Typical value: `DeploymentBundle\TheCertMasterCorporateDB.bak`
- Required: Required when `RestoreSeedDatabase = $true`
- Notes: This is normally a relative path inside the repo/package.

`ConnectionString`
- Purpose: Full SQL connection string override.
- Required: Optional
- Notes: Leave blank to let the installer build one from `SqlInstance` and `DatabaseName`. Set this only if you need a custom SQL configuration.

## Bootstrap admin settings

`BootstrapAdminEmail`
- Purpose: Fallback local administrator account email.
- Required: Optional but strongly recommended
- Notes: The packaged seed database already includes `admin@quizapi.local`, and the standard packaged install template keeps that value prefilled.

`BootstrapAdminPassword`
- Purpose: Fallback local administrator account password.
- Required: Yes for packaged installs
- Notes: Leave this blank to let the installer generate a strong temporary install/configuration password and print it in the install summary. You can also set it explicitly. The installer rejects the packaged default password, and the application rotates the packaged seeded admin account to the configured bootstrap password whenever that seeded admin is out of sync with it.

`BootstrapAdminFirstName`
- Purpose: Display/profile first name for the fallback admin.
- Required: Optional
- Notes: Cosmetic only.

`BootstrapAdminLastName`
- Purpose: Display/profile last name for the fallback admin.
- Required: Optional
- Notes: Cosmetic only.

### Seeded admin behavior

When `RestoreSeedDatabase = $true`, the packaged database already contains a local administrator account:

- Email: `admin@quizapi.local`

Leave `BootstrapAdminPassword` blank to let the installer generate a strong temporary install/configuration password, or set it explicitly if your rollout requires that. During startup, the app rotates the packaged seeded admin account to the configured bootstrap password whenever the seeded admin is out of sync with it.

After installation and configuration are complete, change the `admin@quizapi.local` password again to a final long-term credential.

If you change `BootstrapAdminEmail` to something else, the installer will try to let the application create that account during startup. If that requested account does not appear after deployment, the install fails instead of silently falling back to the seeded admin.

## LDAP / Active Directory settings

`ActiveDirectoryEnabled`
- Purpose: Enables LDAP / Active Directory login support.
- Required: Optional
- Notes: Leave `false` if you are not using directory-backed login yet.

`ActiveDirectoryDomain`
- Purpose: Directory domain value used by the app’s AD integration.
- Required: Optional
- Notes: Leave blank unless you are actively configuring AD login.

`ActiveDirectoryContainer`
- Purpose: LDAP container / search base.
- Required: Optional
- Notes: Leave blank unless your directory query flow requires it.

`ActiveDirectoryNetBiosDomain`
- Purpose: NetBIOS domain prefix for certain username formats.
- Required: Optional
- Notes: Leave blank unless your environment needs it.

`ActiveDirectoryUserPrincipalSuffix`
- Purpose: UPN suffix used for directory sign-in formatting.
- Required: Optional
- Notes: Leave blank unless you know you need it.

`ActiveDirectoryRequireMappedRole`
- Purpose: Requires a directory user to match one of the configured AD role groups before login is accepted.
- Required: Optional
- Notes: Leave `false` during early rollout unless strict role mapping is required.

`ActiveDirectoryDefaultRole`
- Purpose: App role assigned when a mapped role is not otherwise found.
- Typical value: `User`
- Required: Optional
- Notes: Used only when LDAP/AD integration is enabled.

`ActiveDirectoryAdminGroups`
- Purpose: Directory group names that should map to the local `Admin` role.
- Required: Optional
- Notes: Usually left empty until LDAP is actively being configured and tested.

`ActiveDirectoryUserGroups`
- Purpose: Directory group names that should map to the local `User` role.
- Required: Optional
- Notes: Usually left empty until LDAP is actively being configured and tested.

## Validation settings

`EnableSmokeTest`
- Purpose: Runs the post-deploy smoke test after installation.
- Typical value: `$true`
- Required: Optional
- Notes: Recommended for normal installs so the script verifies health, login, and quiz availability before finishing.

## Runtime hardening settings

Anonymous pre-employment quiz generation and submission are rate-limited by remote IP. The production defaults live in `appsettings.Production.json` under `RateLimiting:PreEmployment`:

- `GeneratePermitLimit`: generation requests allowed per window.
- `SubmitPermitLimit`: submission requests allowed per window.
- `LoopbackPermitLimit`: higher local-server allowance for smoke tests and local validation.
- `WindowMinutes`: fixed-window length in minutes.

The pre-employment setup screen also supports an optional access code / link token. If it is set, candidates must enter that code or open a link such as `/preemployment.html?code=your-code` before generation/submission succeeds. Anonymous config responses only reveal whether a code is required; they do not return the configured code. Candidate submissions and completion emails also have short duplicate throttles to reduce accidental double-submits and email bursts.

Quiz package uploads are also bounded before extraction. ZIP uploads are limited to 50 MB compressed, 250 files, 150 MB total uncompressed content, 200 images, 25 MB CSV, and 10 MB per image. Supported extracted image types are PNG, JPG/JPEG, GIF, and WebP; SVG is rejected.

## Recommended minimum values for a standard HTTP rollout

For a simple internal/server validation install, the most important values are:

```powershell
@{
    Protocol = 'http'
    PublicBaseUrl = 'http://localhost'
    SqlInstance = '.\SQLEXPRESS'
    DatabaseName = 'TheCertMasterCorporateDB'
    RestoreSeedDatabase = $true
    DatabaseBackupPath = 'DeploymentBundle\TheCertMasterCorporateDB.bak'
    BootstrapAdminEmail = 'admin@quizapi.local'
    BootstrapAdminPassword = ''
    EnableSmokeTest = $true
}
```

Everything else can usually stay at the default until you need host-header bindings, HTTPS, or LDAP.

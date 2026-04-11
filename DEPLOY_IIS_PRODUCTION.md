# IIS Production Deployment

This runbook documents the current supported deployment model for TheCertMaster CustomQuizs.

## Supported Install Flow

The supported production-style install path is the packaged source bundle extracted to `C:\repo`, followed by the PowerShell 5.1 deployment scripts in `scripts\`.

Use these scripts:

- [scripts/ensure-server-prerequisites.ps1](F:\repos\TheCrtMasterCorporate\scripts\ensure-server-prerequisites.ps1)
- [scripts/install-production-application.ps1](F:\repos\TheCrtMasterCorporate\scripts\install-production-application.ps1)
- [scripts/post-deploy-smoke-test.ps1](F:\repos\TheCrtMasterCorporate\scripts\post-deploy-smoke-test.ps1)
- [scripts/production-settings.template.psd1](F:\repos\TheCrtMasterCorporate\scripts\production-settings.template.psd1)

## Target Platform

- Windows Server 2019 or Windows Server 2022
- IIS installed
- SQL Server Express 2019 or newer
- Windows PowerShell 5.1

## Packaged Install Workflow

1. Upload and extract the release bundle to `C:\repo`
2. Edit `C:\repo\TheCertMaster-CustomQuizs\scripts\production-settings.template.psd1`
3. Open an elevated Windows PowerShell session
4. Run the prerequisites script
5. Run the install script

Commands:

```powershell
powershell.exe -ExecutionPolicy Bypass -File C:\repo\TheCertMaster-CustomQuizs\scripts\ensure-server-prerequisites.ps1
powershell.exe -ExecutionPolicy Bypass -File C:\repo\TheCertMaster-CustomQuizs\scripts\install-production-application.ps1 -SettingsFile C:\repo\TheCertMaster-CustomQuizs\scripts\production-settings.template.psd1
```

Optional manual smoke test:

```powershell
powershell.exe -ExecutionPolicy Bypass -File C:\repo\TheCertMaster-CustomQuizs\scripts\post-deploy-smoke-test.ps1 -BaseUrl http://localhost -AdminEmail admin@quizapi.local -AdminPassword Admin@123
```

## What the Installer Does

The install flow:

- validates required server prerequisites
- installs missing .NET 9 SDK and Hosting Bundle components
- restores the packaged seed database backup
- applies EF Core migrations
- ensures SQL access for the IIS app pool
- builds and publishes the application
- deploys the app into IIS
- configures app settings through IIS environment variables
- runs smoke tests when enabled

## Important Settings

Before install, review:

- `PublicBaseUrl`
- `HostName`
- `Protocol`
- `SqlInstance`
- `DatabaseName`
- `BootstrapAdminEmail`
- `BootstrapAdminPassword`

Full setting guidance is documented in:

- [Documentation/ProductionSettingsReference.md](F:\repos\TheCrtMasterCorporate\Documentation\ProductionSettingsReference.md)

## Seeded Admin Account

The packaged seeded database already contains:

- Email: `admin@quizapi.local`
- Password: `Admin@123`

Important:

- this default password should be changed immediately after the first successful login
- if you keep the standard packaged install values, the installer will validate against this seeded admin account
- if you choose a different bootstrap admin email, the installer will try to let the application create it during startup

## IIS Notes

- the app is designed to run from the root of an IIS site
- the deployment script now handles common fresh-server HTTP binding conflicts, including the default IIS site on port `80`
- runtime folders such as `App_Data`, `wwwroot/uploads`, and `logs` are created automatically

## Post-Install Validation

At minimum, confirm:

- `/health` returns `Healthy`
- admin login works
- quizzes are present
- images render
- management pages load correctly

## Troubleshooting

If the app does not come up:

1. Check `C:\sites\QuizAPI\current\logs`
2. Check the Windows Application log
3. Check IIS AspNetCore Module events
4. Run the smoke test script again manually
5. Verify the SQL instance and database name in `production-settings.template.psd1`

If login works but the wrong admin account is expected:

- remember that the packaged seeded database defaults to `admin@quizapi.local`
- if you typed a different bootstrap email, the app may still validate against the seeded admin unless your custom bootstrap account was successfully created during startup

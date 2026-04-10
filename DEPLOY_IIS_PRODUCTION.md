# IIS Production Deployment

This runbook is tailored to this app and your target environment.

Automation scripts:

- [scripts/Publish-IISPackage.ps1](scripts/Publish-IISPackage.ps1)
- [scripts/Deploy-IISProduction.ps1](scripts/Deploy-IISProduction.ps1)
- [scripts/Get-IISServerInventory.ps1](scripts/Get-IISServerInventory.ps1)

## Target Environment

- Server FQDN: `oumwqapptst02.oumed.net`
- OS: Windows Server 2019
- IIS: installed
- .NET Hosting: .NET 9 Hosting Bundle installed
- SQL Server: SQL Express 2019
- App Pool Name: `QuizAppPool`
- App Pool CLR: `No Managed Code`
- App Pool Pipeline: `Integrated`
- App Pool Identity: `ApplicationPoolIdentity`
- IIS Site Physical Path: `C:\sites\QuizAPI\current`
- HTTPS Binding: port `443`

## Deployment Model

Deploy this app as its own dedicated IIS site at root.

Why:

- the frontend pages use root-relative paths such as `/api/...` and `/styles/...`
- hosting under a child path like `/quizapi` breaks those URLs unless the app is rewritten for a path base
- a dedicated IIS site keeps the current app behavior working as designed

Final expected app URL:

- `https://oumwqapptst02.oumed.net/`

## Deployment Package

Build the deployment zip from the repo first:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Publish-IISPackage.ps1
```

This creates a timestamped publish folder and deployment zip under:

- `.\publish\{timestamp}`
- `.\DeploymentBundle\QuizAPI_IIS_Production_{timestamp}.zip`

## Pre-Deployment Checklist

Before deploying, confirm:

1. The old IIS application deployment under `Default Web Site/quizapi` has been removed.
2. `QuizDB` has already been restored from development and is current.
3. `IIS APPPOOL\QuizAppPool` has access to `QuizDB`.
4. The SSL certificate for `oumwqapptst02.oumed.net` is installed in `Cert:\LocalMachine\My`.
5. Port `443` is available for the dedicated IIS site binding.

## Step 1: Copy the Zip to the Server

Example destination:

```powershell
C:\Deploy\DeploymentBundle\QuizAPI_IIS_Production_YYYYMMDD_HHMMSS.zip
```

## Step 2: Create the Site Folder and Extract

```powershell
New-Item -ItemType Directory -Path C:\sites\QuizAPI\current -Force
Expand-Archive -Path C:\Deploy\DeploymentBundle\QuizAPI_IIS_Production_YYYYMMDD_HHMMSS.zip -DestinationPath C:\sites\QuizAPI\current -Force
```

## Step 3: Create Runtime Folders

```powershell
New-Item -ItemType Directory -Path C:\sites\QuizAPI\current\App_Data -Force
New-Item -ItemType Directory -Path C:\sites\QuizAPI\current\App_Data\keys -Force
New-Item -ItemType Directory -Path C:\sites\QuizAPI\current\App_Data\uploads -Force
New-Item -ItemType Directory -Path C:\sites\QuizAPI\current\wwwroot\uploads -Force
New-Item -ItemType Directory -Path C:\sites\QuizAPI\current\logs -Force
```

## Step 4: Create the Dedicated IIS Site

```powershell
Import-Module WebAdministration
New-Website -Name "QuizAPI" -PhysicalPath "C:\sites\QuizAPI\current" -ApplicationPool "QuizAppPool" -Port 443 -Protocol https -HostHeader "oumwqapptst02.oumed.net"
```

If the site already exists and only needs updating:

```powershell
Import-Module WebAdministration
Set-ItemProperty "IIS:\Sites\QuizAPI" -Name physicalPath -Value "C:\sites\QuizAPI\current"
Set-ItemProperty "IIS:\Sites\QuizAPI" -Name applicationPool -Value "QuizAppPool"
```

## Step 5: Bind the SSL Certificate

List available machine certificates:

```powershell
Get-ChildItem Cert:\LocalMachine\My | Select Subject, Thumbprint
```

Create the site-specific host-header binding:

```powershell
New-Item "IIS:\SslBindings\0.0.0.0!443!oumwqapptst02.oumed.net" -Thumbprint "YOUR_CERT_THUMBPRINT" -SSLFlags 1
```

If your environment uses a shared `*:443` binding without SNI, replace it only intentionally.

## Step 6: Set IIS Environment Variables

These values should be stored in IIS configuration, not in appsettings files.

```powershell
Add-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' -location "QuizAPI" -filter "system.webServer/aspNetCore/environmentVariables" -name "." -value @{name='ASPNETCORE_ENVIRONMENT';value='Production'}
Add-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' -location "QuizAPI" -filter "system.webServer/aspNetCore/environmentVariables" -name "." -value @{name='ConnectionStrings__DefaultConnection';value='Server=.\SQLEXPRESS;Database=QuizDB;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=True;'}
Add-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' -location "QuizAPI" -filter "system.webServer/aspNetCore/environmentVariables" -name "." -value @{name='Jwt__Issuer';value='QuizAPI'}
Add-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' -location "QuizAPI" -filter "system.webServer/aspNetCore/environmentVariables" -name "." -value @{name='Jwt__Audience';value='QuizAPIUsers'}
Add-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' -location "QuizAPI" -filter "system.webServer/aspNetCore/environmentVariables" -name "." -value @{name='Jwt__Key';value='PUT_LONG_RANDOM_SECRET_HERE'}
Add-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' -location "QuizAPI" -filter "system.webServer/aspNetCore/environmentVariables" -name "." -value @{name='Cors__AllowedOrigins__0';value='https://oumwqapptst02.oumed.net'}
Add-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' -location "QuizAPI" -filter "system.webServer/aspNetCore/environmentVariables" -name "." -value @{name='Jwt__AccessTokenMinutes';value='60'}
```

If the environment variables already exist, update them instead of blindly adding duplicates.

## Step 7: Generate the Production JWT Key

Run this on the server to generate a strong secret:

```powershell
[Convert]::ToBase64String((1..64 | ForEach-Object { Get-Random -Maximum 256 }))
```

Use the output as the value for:

```text
Jwt__Key
```

## Step 8: Set File Permissions

Grant the IIS app pool identity the correct access:

```powershell
icacls C:\sites\QuizAPI\current /grant "IIS AppPool\QuizAppPool:(RX)" /t
icacls C:\sites\QuizAPI\current\App_Data /grant "IIS AppPool\QuizAppPool:(M)" /t
icacls C:\sites\QuizAPI\current\wwwroot\uploads /grant "IIS AppPool\QuizAppPool:(M)" /t
icacls C:\sites\QuizAPI\current\logs /grant "IIS AppPool\QuizAppPool:(M)" /t
```

This is required because the app writes to:

- `App_Data\keys`
- `App_Data\uploads`
- `App_Data\import_history.jsonl`
- `App_Data\preemployment-config.json`
- `App_Data\smtp-settings.json`
- `wwwroot\uploads`

## Step 9: Database

Your production `QuizDB` has already been restored from development.

Because published output does not include the project file required for `dotnet ef`, the deployment script does not attempt to run migrations from the published folder anymore.

If future schema changes require migrations, run them from the source project before or after deployment using the real target connection string.

## Step 10: Start or Recycle IIS

```powershell
Restart-WebAppPool -Name "QuizAppPool"
Start-Website -Name "QuizAPI"
```

If needed:

```powershell
iisreset
```

## Step 11: Smoke Test

Test these URLs and flows:

- `https://oumwqapptst02.oumed.net/`
- `https://oumwqapptst02.oumed.net/upload.html`
- admin login
- quiz import
- SMTP save/test
- profile login/history
- pre-employment quiz submission
- uploaded images

## Troubleshooting

### App Fails to Start

Check:

1. .NET 9 Hosting Bundle is installed.
2. `web.config` exists in `C:\sites\QuizAPI\current`.
3. IIS site `QuizAPI` points to `C:\sites\QuizAPI\current`.
4. `QuizAppPool` is assigned to the site.
5. IIS environment variables are present at `QuizAPI`.
6. SQL connection string is valid.
7. file permissions are correct.

### Enable Stdout Logging Temporarily

In `web.config`, change:

```xml
stdoutLogEnabled="false"
```

to:

```xml
stdoutLogEnabled="true"
```

Then recycle the app pool, reproduce the issue, inspect:

```text
C:\sites\QuizAPI\current\logs
```

After troubleshooting, turn stdout logging back off.

## Official References

- IIS hosting overview: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-9.0
- Advanced IIS hosting: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/advanced?view=aspnetcore-9.0
- ASP.NET Core IIS `web.config`: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/web-config?view=aspnetcore-9.0
- Configuration providers and precedence: https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration-providers

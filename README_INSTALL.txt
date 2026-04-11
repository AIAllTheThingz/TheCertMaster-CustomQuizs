TheCertMaster CustomQuizs - Server Install Quick Start

Package contents
- Source code for the application
- Deployment scripts under scripts\
- Seed database backup under DeploymentBundle\

Expected extract location
- Extract this package to: C:\repo
- After extraction, the scripts should be here:
  C:\repo\TheCertMaster-CustomQuizs\scripts

Required install commands
1. Open Windows PowerShell as Administrator
2. Run:

   powershell.exe -ExecutionPolicy Bypass -File C:\repo\TheCertMaster-CustomQuizs\scripts\ensure-server-prerequisites.ps1

3. Then run:

   powershell.exe -ExecutionPolicy Bypass -File C:\repo\TheCertMaster-CustomQuizs\scripts\install-production-application.ps1 -SettingsFile C:\repo\TheCertMaster-CustomQuizs\scripts\production-settings.template.psd1

Optional post-install smoke test

   powershell.exe -ExecutionPolicy Bypass -File C:\repo\TheCertMaster-CustomQuizs\scripts\post-deploy-smoke-test.ps1 -BaseUrl http://localhost -AdminEmail admin@quizapi.local -AdminPassword your-password-here

Notes
- Run the scripts from an elevated PowerShell session.
- Update production-settings.template.psd1 before install if the hostname, database, or deployment values need to change.
- The install script performs the deploy and smoke test when enabled in settings.
- Full setting-by-setting guidance is in:
  Documentation\ProductionSettingsReference.md

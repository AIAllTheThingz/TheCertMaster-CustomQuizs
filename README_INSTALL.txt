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
2. Edit:

   C:\repo\TheCertMaster-CustomQuizs\scripts\production-settings.template.psd1

   Keep the default bootstrap admin email:
   - BootstrapAdminEmail = admin@quizapi.local
   - BootstrapAdminPassword = leave blank to auto-generate a random temporary password

   The packaged database already contains the `admin@quizapi.local` account, and startup
   will rotate that seeded admin to the configured bootstrap password. If the password is
   left blank, the installer generates one and prints it in the install summary.

3. Run:

   powershell.exe -ExecutionPolicy Bypass -File C:\repo\TheCertMaster-CustomQuizs\scripts\ensure-server-prerequisites.ps1

4. Then run:

   powershell.exe -ExecutionPolicy Bypass -File C:\repo\TheCertMaster-CustomQuizs\scripts\install-production-application.ps1 -SettingsFile C:\repo\TheCertMaster-CustomQuizs\scripts\production-settings.template.psd1

Optional post-install smoke test

   powershell.exe -ExecutionPolicy Bypass -File C:\repo\TheCertMaster-CustomQuizs\scripts\post-deploy-smoke-test.ps1 -BaseUrl http://localhost -AdminEmail admin@quizapi.local -AdminPassword use-the-generated-install-password

Notes
- Run the scripts from an elevated PowerShell session.
- Update production-settings.template.psd1 before install if the hostname, database, or deployment values need to change.
- The packaged seeded admin account email is `admin@quizapi.local`.
- Leave `BootstrapAdminPassword` blank to auto-generate a strong temporary install/configuration password, or set one explicitly if your rollout requires it.
- Change the `admin@quizapi.local` password again after setup and configuration are complete.
- The install script performs the deploy and smoke test when enabled in settings.
- Full setting-by-setting guidance is in:
  Documentation\ProductionSettingsReference.md

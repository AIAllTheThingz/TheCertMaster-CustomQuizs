@{
    # Root working folder used by the install scripts for tools, temp files, and artifacts.
    DeploymentRoot = 'C:\Deployment'

    # IIS site and app pool settings.
    SiteName = 'QuizAPI'
    AppPoolName = 'QuizAppPool'
    SitePath = 'C:\sites\QuizAPI\current'
    Protocol = 'http'
    HttpPort = 80
    HttpsPort = 443

    # Set HostName to the server DNS name or FQDN when you want IIS host-header bindings.
    # Leave blank for first-time HTTP installs if you want to access the site by localhost or server IP.
    HostName = ''

    # Public URL used by the post-deploy smoke test.
    # Example: 'http://server01' or 'https://quiz.company.com'
    PublicBaseUrl = 'http://localhost'

    # Keep false for HTTP installs. Set true only when deploying behind HTTPS.
    EnableHttpsRedirection = $false

    # Leave blank to auto-generate a strong key during install.
    JwtKey = ''
    JwtIssuer = 'QuizAPI'
    JwtAudience = 'QuizAPIUsers'
    JwtAccessTokenMinutes = 60

    SqlInstance = '.\SQLEXPRESS'
    DatabaseName = 'TheCertMasterCorporateDB'
    RestoreSeedDatabase = $true
    DatabaseBackupPath = 'DeploymentBundle\TheCertMasterCorporateDB.bak'

    # Leave blank to let the installer build a local SQL Express connection string.
    ConnectionString = ''

    # The packaged seed database already contains admin@quizapi.local / Admin@123.
    # Change this password immediately after first login in any real environment.
    # Keep these values for the standard packaged install, or replace them if you want
    # the application to create/update a different fallback local admin during startup.
    BootstrapAdminEmail = 'admin@quizapi.local'
    BootstrapAdminPassword = 'Admin@123'
    BootstrapAdminFirstName = 'Server'
    BootstrapAdminLastName = 'Admin'

    ActiveDirectoryEnabled = $false
    ActiveDirectoryDomain = ''
    ActiveDirectoryContainer = ''
    ActiveDirectoryNetBiosDomain = ''
    ActiveDirectoryUserPrincipalSuffix = ''
    ActiveDirectoryRequireMappedRole = $false
    ActiveDirectoryDefaultRole = 'User'
    ActiveDirectoryAdminGroups = @()
    ActiveDirectoryUserGroups = @()

    EnableSmokeTest = $true
}

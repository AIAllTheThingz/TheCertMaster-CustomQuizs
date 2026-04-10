@{
    DeploymentRoot = 'C:\Deployment'

    SiteName = 'QuizAPI'
    AppPoolName = 'QuizAppPool'
    SitePath = 'C:\sites\QuizAPI\current'
    Protocol = 'http'
    HttpPort = 80
    HttpsPort = 443

    # Default Windows Server 2022 host baseline for the first HTTP rollout.
    HostName = 'WIN2K22IIS01'
    PublicBaseUrl = 'http://WIN2K22IIS01'
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

    BootstrapAdminEmail = 'admin@quizapi.local'
    BootstrapAdminPassword = 'Admin@123'
    BootstrapAdminFirstName = 'Server'
    BootstrapAdminLastName = 'Admin'

    EnableSmokeTest = $true
}

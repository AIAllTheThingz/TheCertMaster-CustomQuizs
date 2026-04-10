@{
    DeploymentRoot = 'C:\Deployment'

    SiteName = 'QuizAPI'
    AppPoolName = 'QuizAppPool'
    SitePath = 'C:\sites\QuizAPI\current'
    Protocol = 'http'
    HttpPort = 80
    HttpsPort = 443

    # Leave blank to use localhost for HTTP-only installs.
    HostName = ''
    PublicBaseUrl = 'http://localhost'

    # Leave blank to auto-generate a strong key during install.
    JwtKey = ''
    JwtIssuer = 'QuizAPI'
    JwtAudience = 'QuizAPIUsers'
    JwtAccessTokenMinutes = 60

    SqlInstance = '.\SQLEXPRESS'
    DatabaseName = 'QuizDB'

    # Leave blank to let the installer build a local SQL Express connection string.
    ConnectionString = ''

    BootstrapAdminEmail = 'admin@quizapi.local'
    BootstrapAdminPassword = 'Admin@123'
    BootstrapAdminFirstName = 'Server'
    BootstrapAdminLastName = 'Admin'

    EnableSmokeTest = $true
}

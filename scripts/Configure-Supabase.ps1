param([switch]$CheckOnly)

$ErrorActionPreference = 'Stop'
$projectFile = Join-Path $PSScriptRoot '..\Municipal.Web\Municipal.Web.csproj'
$projectSettings = Join-Path $PSScriptRoot 'supabase-project.json'
if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Install the .NET 9 SDK before configuring ReportSa.'
}

if (!$CheckOnly) {
    $settings = Get-Content -LiteralPath $projectSettings -Raw | ConvertFrom-Json
    if ($settings.Host -notmatch '^[a-z0-9-]+\.pooler\.supabase\.com$' -or !$settings.Username) {
        throw 'Check scripts/supabase-project.json against the Session pooler settings in your Supabase dashboard.'
    }
    Write-Host 'Connect this machine to the shared ReportSa Supabase database.'
    Write-Host 'Enter the DATABASE password from the Supabase project owner, not your admin login password.'
    $databasePassword = Read-Host 'Supabase database password' -AsSecureString
    if ($databasePassword.Length -eq 0) { throw 'A database password is required.' }
    $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $secretPayload = $null
    try {
        $builder.set_Item('Host', [string]$settings.Host)
        $builder.set_Item('Port', [int]$settings.Port)
        $builder.set_Item('Database', [string]$settings.Database)
        $builder.set_Item('Username', [string]$settings.Username)
        $builder.set_Item('Password', [System.Net.NetworkCredential]::new('', $databasePassword).Password)
        $builder.set_Item('SSL Mode', 'Require')
        $builder.set_Item('Timeout', 15)
        $secretPayload = @{
            'Database:Provider' = 'Postgres'
            'ConnectionStrings:DefaultConnection' = $builder.get_ConnectionString()
        } | ConvertTo-Json -Compress
        # Piped JSON keeps the password out of process arguments and command history.
        $secretPayload | & dotnet user-secrets set --project $projectFile
        if ($LASTEXITCODE -ne 0) { throw 'Could not save the connection in local User Secrets.' }
    }
    finally {
        $builder.Clear()
        $secretPayload = $null
        $databasePassword.Dispose()
    }
}

Write-Host 'Checking the shared database (no accounts or reports will be changed)...'
& dotnet run --project $projectFile --no-launch-profile -- --check-database
if ($LASTEXITCODE -ne 0) {
    throw 'Database verification failed. Check the database password, project availability, network access, and any ConnectionStrings__DefaultConnection environment override.'
}
Write-Host 'Ready. Restart Visual Studio or run: dotnet run --project Municipal.Web'
Write-Host 'Sign in with the existing ReportSa administrator account. Do not run --setup on a teammate machine.'

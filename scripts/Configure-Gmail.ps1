param([string]$SenderEmail, [string]$TrackUrl = 'https://localhost:7124/Requests/Track')
$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot '..\Municipal.Web'
if (-not $SenderEmail) { $SenderEmail = Read-Host 'Gmail address that will send confirmations' }
if ($SenderEmail -notmatch '^[^\s@]+@[^\s@]+\.[^\s@]+$') { throw 'Enter a valid sender email address.' }
try {
    do {
        $securePassword = Read-Host '16-character Google app password (hidden)' -AsSecureString
        $passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
        try {
            $appPassword = [regex]::Replace([Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer), '\s', '')
        } finally {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
            $securePassword.Dispose()
        }
        if ($appPassword.Length -ne 16) {
            $appPassword = $null
            Write-Warning 'Use a Google app password from https://myaccount.google.com/apppasswords, not your regular Gmail password. Try again; Ctrl+C cancels.'
        }
    } while ($null -eq $appPassword)
    $smtpClient = [System.Net.Mail.SmtpClient]::new('smtp.gmail.com', 587)
    $smtpClient.EnableSsl = $true
    $smtpClient.Timeout = 15000
    $smtpClient.Credentials = [System.Net.NetworkCredential]::new($SenderEmail, $appPassword)
    try {
        $smtpClient.Send($SenderEmail, $SenderEmail, 'Municipal Connect email setup test', 'Email notifications are ready. New report confirmations will include the reference number, assigned person and estimated completion time.')
    } finally { $smtpClient.Dispose() }
    $emailSettings = @{
        'Email:Enabled' = 'true'
        'Email:Host' = 'smtp.gmail.com'
        'Email:Port' = '587'
        'Email:Username' = $SenderEmail
        'Email:Password' = $appPassword
        'Email:FromAddress' = $SenderEmail
        'Email:FromName' = 'Municipal Connect'
        'Email:TrackUrl' = $TrackUrl
    }
    $emailSettings | ConvertTo-Json -Compress | dotnet user-secrets set --project $projectPath
    if ($LASTEXITCODE -ne 0) { throw 'Gmail settings could not be saved.' }
    Write-Host 'Test email sent and Gmail settings saved locally. Restart the website to use them.'
} finally {
    $appPassword = $null
    $emailSettings = $null
}

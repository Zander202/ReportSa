$ErrorActionPreference = 'Stop'
# Authenticate only: this diagnostic does not send an email or print credentials.
$secretPath = Join-Path $env:APPDATA 'Microsoft/UserSecrets/municipal-spm622-prototype-2026/secrets.json'
$settings = Get-Content -LiteralPath $secretPath -Raw | ConvertFrom-Json
$smtpHost = [string]$settings.'Email:Host'
$smtpPort = [int]$settings.'Email:Port'
$client = [Net.Sockets.TcpClient]::new()
$reader = $null
$writer = $null
$stream = $null
function Read-Reply {
    $lines = [Collections.Generic.List[string]]::new()
    do {
        $line = $reader.ReadLine()
        if ($null -eq $line) { throw 'The mail server closed the connection.' }
        $lines.Add($line)
    } while ($line.Length -gt 3 -and $line[3] -eq '-')
    return ($lines -join ' ')
}
function Send-Command([string]$command) {
    $writer.WriteLine($command)
    return Read-Reply
}
try {
    $connection = $client.BeginConnect($smtpHost, $smtpPort, $null, $null)
    if (-not $connection.AsyncWaitHandle.WaitOne(8000)) { throw "Cannot reach ${smtpHost}:${smtpPort} within 8 seconds." }
    $client.EndConnect($connection)
    $stream = $client.GetStream()
    $stream.ReadTimeout = 10000
    $stream.WriteTimeout = 10000
    $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::ASCII, $false, 1024, $true)
    $writer = [IO.StreamWriter]::new($stream, [Text.Encoding]::ASCII, 1024, $true)
    $writer.NewLine = "`r`n"
    $writer.AutoFlush = $true
    $greeting = Read-Reply
    if (-not $greeting.StartsWith('220')) { throw $greeting }
    $hello = Send-Command 'EHLO reportsa.local'
    if (-not $hello.StartsWith('250')) { throw $hello }
    $tlsReply = Send-Command 'STARTTLS'
    if (-not $tlsReply.StartsWith('220')) { throw $tlsReply }
    $reader.Dispose()
    $writer.Dispose()
    $stream = [Net.Security.SslStream]::new($stream, $false)
    $stream.AuthenticateAsClient($smtpHost)
    $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::ASCII, $false, 1024, $true)
    $writer = [IO.StreamWriter]::new($stream, [Text.Encoding]::ASCII, 1024, $true)
    $writer.NewLine = "`r`n"
    $writer.AutoFlush = $true
    $hello = Send-Command 'EHLO reportsa.local'
    if (-not $hello.StartsWith('250')) { throw $hello }
    $reply = Send-Command 'AUTH LOGIN'
    if (-not $reply.StartsWith('334')) { throw $reply }
    $reply = Send-Command ([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($settings.'Email:Username')))
    if (-not $reply.StartsWith('334')) { throw $reply }
    $reply = Send-Command ([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($settings.'Email:Password')))
    if (-not $reply.StartsWith('235')) { throw "SMTP authentication failed: $reply" }
    Write-Host 'PASS: connection, TLS and SMTP authentication succeeded. No email was sent.'
    $null = Send-Command 'QUIT'
} finally {
    if ($reader) { $reader.Dispose() }
    if ($writer) { $writer.Dispose() }
    if ($stream) { $stream.Dispose() }
    $client.Dispose()
    $settings = $null
}

param([string]$BaseUrl = 'https://localhost:7124', [string]$ReferenceCode = '')
$ErrorActionPreference = 'Stop'
# Uses existing local User Secrets, never logs credentials or bypasses HTTPS validation.
$secretPath = Join-Path $env:APPDATA 'Microsoft\UserSecrets\municipal-spm622-prototype-2026\secrets.json'
$secrets = if (Test-Path -LiteralPath $secretPath) { Get-Content -LiteralPath $secretPath -Raw | ConvertFrom-Json } else { $null }
$email = if ($env:Admin__Email) { $env:Admin__Email } else { $secrets.'Admin:Email' }
$password = if ($env:Admin__Password) { $env:Admin__Password } else { $secrets.'Admin:Password' }
if (!$email -or !$password) { throw 'Configure Admin:Email and Admin:Password in User Secrets or environment variables before live verification.' }
function New-Client {
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $handler.CookieContainer = [System.Net.CookieContainer]::new()
    $client = [System.Net.Http.HttpClient]::new($handler)
    $client.BaseAddress = [uri]$BaseUrl
    return $client
}
function Get-Page($client, $url) {
    $response = $client.GetAsync($url).GetAwaiter().GetResult()
    if ([int]$response.StatusCode -ne 200) { throw "GET $url returned $([int]$response.StatusCode)" }
    return $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
}
function Get-Field($html, $name) {
    $tag = [regex]::Match($html, '<input[^>]*name="' + [regex]::Escape($name) + '"[^>]*>').Value
    return [System.Net.WebUtility]::HtmlDecode([regex]::Match($tag, 'value="([^"]*)"').Groups[1].Value)
}
function Post-Form($client, $url, $values) {
    $fields = [System.Collections.Generic.Dictionary[string,string]]::new()
    foreach ($key in $values.Keys) { $fields.Add($key, [string]$values[$key]) }
    $content = [System.Net.Http.FormUrlEncodedContent]::new($fields)
    return $client.PostAsync($url, $content).GetAwaiter().GetResult()
}
function Assert-Redirect($response) {
    if ([int]$response.StatusCode -ne 302) { throw "Expected redirect, received $([int]$response.StatusCode)" }
}
$resident = New-Client
$admin = New-Client
try {
    if (!$ReferenceCode) {
        $report = Get-Page $resident '/Requests/Report'
        $submitted = Post-Form $resident '/Requests/Report' @{ __RequestVerificationToken = (Get-Field $report '__RequestVerificationToken'); Category = '0'; Location = '[FICTIONAL DEMO] Live verification address'; Description = 'Fictional request created by the local verification script.' }
        Assert-Redirect $submitted
        $confirmation = Get-Page $resident $submitted.Headers.Location.OriginalString
        $ReferenceCode = Get-Field $confirmation 'reference'
        if ($ReferenceCode -notmatch '^MUN-[0-9A-F]{24}$') { throw 'No valid reference received.' }
        Write-Output "Submission and reference confirmation verified: $ReferenceCode"
    }
    $login = Get-Page $admin '/Account/Login'
    $signedIn = Post-Form $admin '/Account/Login' @{ __RequestVerificationToken = (Get-Field $login '__RequestVerificationToken'); Email = $email; Password = $password }
    Assert-Redirect $signedIn
    $password = $null; $secrets = $null
    $list = Get-Page $admin '/Admin'
    $row = [regex]::Match($list, '(?s)<tr>[^<]*(?:(?!</tr>).)*' + [regex]::Escape($ReferenceCode) + '(?:(?!</tr>).)*</tr>').Value
    $editUrl = [regex]::Match($row, 'href="([^"]*/Admin/Edit/\d+)"').Groups[1].Value
    if (!$editUrl) { throw 'Submitted reference not found in admin list.' }
    foreach ($stage in 0..3) {
        $edit = Get-Page $admin $editUrl
        $updated = Post-Form $admin '/Admin/Update' @{ __RequestVerificationToken = (Get-Field $edit '__RequestVerificationToken'); Id = (Get-Field $edit 'Id'); RowVersion = (Get-Field $edit 'RowVersion'); Priority = '3'; Status = [string]$stage }
        Assert-Redirect $updated
        $saved = Get-Page $admin $updated.Headers.Location.OriginalString
        if ($saved -notmatch 'Request updated') { throw "Stage $stage did not save successfully." }
        if ($saved -notmatch '<option value="3" selected="selected">Urgent</option>') { throw 'Priority did not persist.' }
        if ($saved -notmatch ('<option value="' + $stage + '">[^<]*\(current\)</option>')) { throw 'Status did not persist.' }
    }
    $track = Get-Page $resident '/Requests/Track'
    $tracked = Post-Form $resident '/Requests/Track' @{ __RequestVerificationToken = (Get-Field $track '__RequestVerificationToken'); ReferenceCode = $ReferenceCode }
    $result = $tracked.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    if ([int]$tracked.StatusCode -ne 200 -or $result -notmatch 'Resolved \(current\)' -or $result -notmatch 'Status history') { throw 'Updated request was not tracked.' }
    $anonymous = $resident.GetAsync('/Admin').GetAwaiter().GetResult()
    Assert-Redirect $anonymous
    Write-Output 'LIVE PASS: administrator login, priority-only update, all forward status updates, tracking history, and anonymous protection.'
} finally { $resident.Dispose(); $admin.Dispose(); $password = $null; $secrets = $null }

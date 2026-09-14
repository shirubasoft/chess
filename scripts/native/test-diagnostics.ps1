$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'diagnostics.ps1')
$sample = @'
error: PostgreSQL server refused the connection on port 5432.
Dashboard: https://localhost:18888/login?t=dashboard-credential
{"apiKey":"generated-api-credential"}
ConnectionStrings__chess=Host=localhost;Password=database-credential
Authorization: Bearer request-credential
Endpoint: postgres://postgres:url-credential@localhost/chess
Stack: at Chess.Server.InitializeStorage.StartAsync()
'@
$clean = Protect-NativeDiagnostic $sample
foreach ($secret in @('dashboard-credential', 'generated-api-credential', 'database-credential', 'request-credential', 'url-credential')) {
    if ($clean.Contains($secret)) { throw "Diagnostic redaction retained $secret." }
}
foreach ($evidence in @('PostgreSQL server refused the connection on port 5432.', 'Chess.Server.InitializeStorage.StartAsync()')) {
    if (-not $clean.Contains($evidence)) { throw 'Diagnostic redaction removed the failure evidence.' }
}
Write-Host 'Diagnostic redaction preserves failure evidence and removes credentials.'

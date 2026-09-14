function Protect-NativeDiagnostic([string]$Text) {
    $clean = [regex]::Replace($Text, '\x1b\[[0-?]*[ -/]*[@-~]', '')
    foreach ($entry in [Environment]::GetEnvironmentVariables().GetEnumerator()) {
        if ($entry.Key -match '(?i)connectionstring|password|secret|token|api_?key' -and $entry.Value.Length -ge 8) {
            $clean = $clean.Replace([string]$entry.Value, '[redacted]')
        }
    }
    $clean = [regex]::Replace($clean, '(?i)(https?://[^\s"<>?]+)\?[^\s"<>]*', '$1?[redacted]')
    $clean = [regex]::Replace($clean, '(?i)([a-z][a-z0-9+.-]*://)[^\s/@]+:[^\s/@]+@', '$1[redacted]@')
    $clean = [regex]::Replace($clean, '(?im)((?:password|token|secret|api[-_]?key|authorization|connectionstrings?)[\w:.-]*["'']?\s*[:=]\s*).+$', '$1[redacted]')
    return $clean
}

function Write-NativeDiagnostic([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, (Protect-NativeDiagnostic $Text))
}

function Save-NativeFailureDiagnostics([string]$ArtifactPath, [string]$AppHost, [DateTimeOffset]$Since) {
    # Collect before Aspire teardown. Limit queries so diagnostics cannot hold a failed job open.
    foreach ($query in @(
        @{ Name = 'aspire-server'; Arguments = @('logs', 'server', '--tail', '200', '--format', 'Json') },
        @{ Name = 'aspire-state'; Arguments = @('describe', '--format', 'Json') }
    )) {
        try {
            $info = [Diagnostics.ProcessStartInfo]::new('aspire')
            $info.UseShellExecute = $false
            $info.RedirectStandardOutput = $true
            $info.RedirectStandardError = $true
            foreach ($argument in ($query.Arguments + @('--apphost', $AppHost, '--non-interactive', '--nologo'))) { $info.ArgumentList.Add($argument) }
            $process = [Diagnostics.Process]::Start($info)
            $output = $process.StandardOutput.ReadToEndAsync()
            $errorOutput = $process.StandardError.ReadToEndAsync()
            if (-not $process.WaitForExit(15000)) { $process.Kill($true); $process.WaitForExit() }
            $text = $output.GetAwaiter().GetResult() + $errorOutput.GetAwaiter().GetResult()
            if ($query.Name -eq 'aspire-state' -and $process.ExitCode -eq 0) {
                # Whitelist state fields; resource environments contain generated credentials.
                $state = $text | ConvertFrom-Json
                $text = $state.resources | Select-Object name, displayName, state, healthStatus, healthReports | ConvertTo-Json -Depth 10
            }
            Write-NativeDiagnostic (Join-Path $ArtifactPath "$($query.Name).log") $text
        }
        catch { Write-NativeDiagnostic (Join-Path $ArtifactPath "$($query.Name).log") "Diagnostic collection failed: $($_.Exception.Message)" }
    }
    $logDirectory = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.aspire/logs'
    if (Test-Path $logDirectory) {
        Get-ChildItem $logDirectory -Filter '*.log' | Where-Object LastWriteTimeUtc -GE $Since.UtcDateTime |
            Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 10 | ForEach-Object {
                try {
                    $text = (Get-Content $_.FullName -Tail 1500) -join "`n"
                    Write-NativeDiagnostic (Join-Path $ArtifactPath "aspire-$($_.Name)") $text
                }
                catch { Write-Warning "Could not retain Aspire log $($_.Name)." }
            }
    }
}

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'diagnostics.ps1')
. (Join-Path $PSScriptRoot 'android-artifacts.ps1')

$directory = Join-Path ([IO.Path]::GetTempPath()) "chess-android-artifacts-$([Guid]::NewGuid().ToString('N'))"
[IO.Directory]::CreateDirectory($directory) | Out-Null
try {
    $fixture = Join-Path $directory 'capture.ps1'
    $source = @'
param([string]$Mode)
$png = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=')
if ($Mode -eq 'timeout') {
    [Console]::Error.Write('capture still waiting for device')
    Start-Sleep -Seconds 30
    exit 0
}
if ($Mode -eq 'disconnected') {
    [Console]::Error.Write('adb: error: device offline')
    exit 1
}
if ($Mode -eq 'empty') { exit 0 }
if ($Mode -eq 'text') { [Console]::Write('screencap failed'); exit 0 }
if ($Mode -eq 'truncated') { $png = $png[0..($png.Length - 8)] }
if ($Mode -eq 'trailing') { $png += [byte]0 }
if ($Mode -eq 'zero-width') { $png[19] = 0 }
if ($Mode -eq 'corrupt') { $png[48] = $png[48] -bxor 1 }
if ($Mode -eq 'stderr') { [Console]::Error.Write(("diagnostic-output-`n" * 10000)) }
$output = [Console]::OpenStandardOutput()
$output.Write($png, 0, $png.Length)
$output.Flush()
if ($Mode -eq 'nonzero-png') { exit 1 }
exit 0
'@
    [IO.File]::WriteAllText($fixture, $source)
    $pwsh = (Get-Process -Id $PID).Path
    $expected = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=')
    foreach ($mode in @('valid', 'stderr')) {
        $path = Join-Path $directory "$mode.png"
        $log = Join-Path $directory "$mode.log"
        Save-NativePng $pwsh @('-NoProfile', '-File', $fixture, $mode) $path $log
        if ([Convert]::ToHexString([IO.File]::ReadAllBytes($path)) -ne [Convert]::ToHexString($expected)) {
            throw "Capture changed the PNG bytes for $mode."
        }
        $diagnostic = [IO.File]::ReadAllText($log)
        if (-not $diagnostic.Contains('Exit code: 0')) { throw 'Capture omitted the process exit code.' }
        if ($mode -eq 'stderr' -and -not $diagnostic.Contains('diagnostic-output-')) { throw 'Capture discarded stderr.' }
    }
    foreach ($failure in @(
        @{ Mode = 'disconnected'; Message = 'exit code 1'; Evidence = 'device offline' }
        @{ Mode = 'nonzero-png'; Message = 'exit code 1'; Evidence = 'Exit code: 1' }
        @{ Mode = 'empty'; Message = 'not a PNG'; Evidence = 'Output bytes: 0' }
        @{ Mode = 'text'; Message = 'not a PNG'; Evidence = 'Exit code: 0' }
        @{ Mode = 'truncated'; Message = 'truncated'; Evidence = 'Exit code: 0' }
        @{ Mode = 'trailing'; Message = 'complete image data'; Evidence = 'Exit code: 0' }
        @{ Mode = 'zero-width'; Message = 'valid image header'; Evidence = 'Exit code: 0' }
        @{ Mode = 'corrupt'; Message = 'corrupt IDAT'; Evidence = 'Exit code: 0' }
        @{ Mode = 'timeout'; Message = 'timed out'; Evidence = 'Timed out: True' }
    )) {
        $path = Join-Path $directory "$($failure.Mode).png"
        $log = Join-Path $directory "$($failure.Mode).log"
        [IO.File]::WriteAllBytes($path, $expected)
        $caught = $null
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $timeout = if ($failure.Mode -eq 'timeout') { 1500 } else { 15000 }
        try { Save-NativePng $pwsh @('-NoProfile', '-File', $fixture, $failure.Mode) $path $log $timeout }
        catch { $caught = $_ }
        if (-not $caught -or -not $caught.Exception.Message.Contains($failure.Message)) {
            throw "Capture did not report $($failure.Mode): $caught"
        }
        if (Test-Path $path) { throw "Capture retained a screenshot after $($failure.Mode)." }
        if (-not [IO.File]::ReadAllText($log).Contains($failure.Evidence)) { throw "Capture lost evidence for $($failure.Mode)." }
        if ($failure.Mode -eq 'timeout' -and $watch.Elapsed.TotalSeconds -ge 10) { throw 'Capture did not bound its child process.' }
    }
    & {
        function Invoke-NativeArtifactCommand { throw 'ADB is unavailable during cleanup.' }
        $caught = $null
        try {
            try { throw 'Original verification failure.' }
            finally {
                Save-AndroidDiagnostics emulator-test $directory -Failure
                Remove-AndroidReverse emulator-test tcp:12345 $directory 3>$null
            }
        }
        catch { $caught = $_ }
        if (-not $caught -or $caught.Exception.Message -ne 'Original verification failure.') { throw 'Cleanup masked the verification failure.' }
        if (-not [IO.File]::ReadAllText((Join-Path $directory 'android-devices.log')).Contains('ADB is unavailable')) {
            throw 'Cleanup discarded the diagnostic collection failure.'
        }
    }
    Write-Host 'Android capture preserves PNG bytes and stderr, rejects failed or incomplete output, and bounds hung processes.'
}
finally { Remove-Item $directory -Recurse -Force }

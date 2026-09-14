function Invoke-NativeArtifactCommand([string]$Command, [string[]]$Arguments, [int]$TimeoutMilliseconds = 15000) {
    $info = [Diagnostics.ProcessStartInfo]::new((Get-Command $Command -CommandType Application -ErrorAction Stop).Source)
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $startedAt = [DateTimeOffset]::UtcNow
    $output = [IO.MemoryStream]::new()
    $process = [Diagnostics.Process]::Start($info)
    try {
        # Keep PNG bytes out of PowerShell's text pipeline and drain both pipes concurrently.
        $copy = $process.StandardOutput.BaseStream.CopyToAsync($output)
        $errorOutput = $process.StandardError.ReadToEndAsync()
        $timedOut = -not $process.WaitForExit($TimeoutMilliseconds)
        if ($timedOut) { $process.Kill($true); $process.WaitForExit() }
        $copy.GetAwaiter().GetResult() | Out-Null
        return [pscustomobject]@{
            StartedAt = $startedAt
            DurationMilliseconds = ([DateTimeOffset]::UtcNow - $startedAt).TotalMilliseconds
            ExitCode = $process.ExitCode
            TimedOut = $timedOut
            Output = $output.ToArray()
            Error = $errorOutput.GetAwaiter().GetResult()
        }
    }
    finally { $process.Dispose(); $output.Dispose() }
}

function Format-NativeArtifactCommand($Result, [string[]]$Arguments, [switch]$Binary) {
    $details = @(
        "Started: $($Result.StartedAt.ToString('O'))"
        "Arguments: $($Arguments -join ' ')"
        "Exit code: $($Result.ExitCode)"
        "Timed out: $($Result.TimedOut)"
        "Duration milliseconds: $($Result.DurationMilliseconds)"
        "Output bytes: $($Result.Output.Length)"
        "Stderr: $($Result.Error)"
    )
    if (-not $Binary) { $details += [Text.Encoding]::UTF8.GetString($Result.Output) }
    return $details -join "`n"
}

function Assert-NativePng([byte[]]$Bytes) {
    # Require the PNG header, nonzero dimensions, image data and complete, intact chunks.
    if ($Bytes.Length -lt 57 -or [Convert]::ToHexString($Bytes, 0, 8) -ne '89504E470D0A1A0A') {
        throw 'Screenshot output is not a PNG image.'
    }
    $offset = 8
    $hasImageData = $false
    $crcTable = [uint32[]]::new(256)
    for ($index = 0; $index -lt 256; $index++) {
        $crc = [uint32]$index
        for ($bit = 0; $bit -lt 8; $bit++) {
            $crc = if ($crc -band 1) { ($crc -shr 1) -bxor [uint32]3988292384 } else { $crc -shr 1 }
        }
        $crcTable[$index] = $crc
    }
    while ($offset -le $Bytes.Length - 12) {
        $length = [uint32]$Bytes[$offset] * 16777216 + [uint32]$Bytes[$offset + 1] * 65536 + [uint32]$Bytes[$offset + 2] * 256 + [uint32]$Bytes[$offset + 3]
        if ($length -gt $Bytes.Length - $offset - 12) { throw 'Screenshot PNG is truncated.' }
        $type = [Text.Encoding]::ASCII.GetString($Bytes, $offset + 4, 4)
        if ($offset -eq 8 -and ($type -ne 'IHDR' -or $length -ne 13 -or
            [Convert]::ToHexString($Bytes, 16, 4) -eq '00000000' -or
            [Convert]::ToHexString($Bytes, 20, 4) -eq '00000000')) {
            throw 'Screenshot PNG has no valid image header.'
        }
        $crc = [uint32]::MaxValue
        for ($index = $offset + 4; $index -lt $offset + 8 + $length; $index++) {
            $crc = $crcTable[($crc -bxor $Bytes[$index]) -band 255] -bxor ($crc -shr 8)
        }
        $crc = $crc -bxor [uint32]::MaxValue
        if ($crc.ToString('X8') -ne [Convert]::ToHexString($Bytes, $offset + 8 + $length, 4)) {
            throw "Screenshot PNG has a corrupt $type chunk."
        }
        if ($type -eq 'IDAT' -and $length -gt 0) { $hasImageData = $true }
        if ($type -eq 'IEND') {
            if (-not $hasImageData -or $length -ne 0 -or $offset + 12 -ne $Bytes.Length -or
                [Convert]::ToHexString($Bytes, $offset + 8, 4) -ne 'AE426082') {
                throw 'Screenshot PNG has no complete image data.'
            }
            return
        }
        $offset += 12 + $length
    }
    throw 'Screenshot PNG is truncated.'
}

function Save-NativePng([string]$Command, [string[]]$Arguments, [string]$Path, [string]$DiagnosticPath, [int]$TimeoutMilliseconds = 15000) {
    Remove-Item $Path -Force -ErrorAction SilentlyContinue
    $result = Invoke-NativeArtifactCommand $Command $Arguments $TimeoutMilliseconds
    $details = Format-NativeArtifactCommand $result $Arguments -Binary
    try {
        if ($result.TimedOut) { throw 'Android screenshot capture timed out.' }
        if ($result.ExitCode -ne 0) { throw "Android screenshot capture failed with exit code $($result.ExitCode)." }
        Assert-NativePng $result.Output
        [IO.File]::WriteAllBytes($Path, $result.Output)
    }
    catch { $details += "`nFailure: $($_.Exception.Message)"; throw }
    finally {
        try { Write-NativeDiagnostic $DiagnosticPath $details }
        catch { Write-Warning "Could not retain screenshot diagnostics: $($_.Exception.Message)" }
    }
}

function Save-AndroidScreenshot([string]$Device, [string]$Path, [string]$ArtifactPath) {
    Save-NativePng adb @('-s', $Device, 'exec-out', 'screencap', '-p') $Path (Join-Path $ArtifactPath 'android-screenshot.log')
}

function Save-AndroidDiagnostics([string]$Device, [string]$ArtifactPath, [switch]$Failure) {
    $queries = @(@{ Name = 'android-verification'; Arguments = @('-s', $Device, 'logcat', '-d', '-s', 'ChessVerification:I', 'AndroidRuntime:E', '*:S') })
    if ($Failure) {
        $queries = @(
            @{ Name = 'android-devices'; Arguments = @('devices', '-l') }
            @{ Name = 'android-adb-server'; Arguments = @('server-status') }
            @{ Name = 'android-device-state'; Arguments = @('-s', $Device, 'shell', 'cat', '/proc/sys/kernel/random/boot_id', '/proc/uptime') }
            @{ Name = 'android-crash'; Arguments = @('-s', $Device, 'logcat', '-b', 'crash', '-d', '-t', '200') }
            @{ Name = 'android-system'; Arguments = @('-s', $Device, 'logcat', '-d', '-t', '500', '-s', 'adbd:I', 'SurfaceFlinger:I', 'AndroidRuntime:E', 'lmkd:I', 'ActivityManager:I', '*:S') }
        ) + $queries
    }
    foreach ($query in $queries) {
        $path = Join-Path $ArtifactPath "$($query.Name).log"
        try {
            $result = Invoke-NativeArtifactCommand adb $query.Arguments
            Write-NativeDiagnostic $path (Format-NativeArtifactCommand $result $query.Arguments)
            if ($result.TimedOut -or $result.ExitCode -ne 0) { Write-Warning "Could not collect $($query.Name); inspect $path." }
        }
        catch { Write-NativeDiagnostic $path "Diagnostic collection failed: $($_.Exception.Message)" }
    }
}

function Remove-AndroidReverse([string]$Device, [string]$Port, [string]$ArtifactPath) {
    $arguments = @('-s', $Device, 'reverse', '--remove', $Port)
    $path = Join-Path $ArtifactPath 'android-reverse-cleanup.log'
    try {
        $result = Invoke-NativeArtifactCommand adb $arguments
        Write-NativeDiagnostic $path (Format-NativeArtifactCommand $result $arguments)
        if ($result.TimedOut -or $result.ExitCode -ne 0) { Write-Warning "Could not remove Android reverse listener; inspect $path." }
    }
    catch { Write-Warning "Could not remove Android reverse listener: $($_.Exception.Message)" }
}

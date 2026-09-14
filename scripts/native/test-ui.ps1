[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Linux', 'Windows', 'Android')][string]$Platform,
    [ValidateSet('Akka', 'Orleans')][string]$Backend = 'Akka',
    [string]$Server,
    [string]$Device = 'emulator-5554',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$ArtifactDirectory = 'artifacts/native'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'diagnostics.ps1')
$runStartedAt = [DateTimeOffset]::UtcNow
Set-Location (Join-Path $PSScriptRoot '../..')
$artifactPath = [IO.Path]::GetFullPath($ArtifactDirectory)
[IO.Directory]::CreateDirectory($artifactPath) | Out-Null
$reportPath = Join-Path $artifactPath "$($Platform.ToLowerInvariant())-verification.json"
$screenshotPath = [IO.Path]::ChangeExtension($reportPath, '.png')
Remove-Item $reportPath, $screenshotPath -Force -ErrorAction SilentlyContinue
$apphost = 'Chess.AppHost/Chess.AppHost.csproj'
$ownsServer = [string]::IsNullOrWhiteSpace($Server)
$startedPostgres = $false
$nativeProcess = $null
$reversePort = $null

function Invoke-Checked([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE." }
}
function Start-Desktop([string[]]$Arguments, [string]$LogName) {
    $info = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($info)
    return @{
        Process = $process
        Output = $process.StandardOutput.ReadToEndAsync()
        Error = $process.StandardError.ReadToEndAsync()
        Log = Join-Path $artifactPath $LogName
    }
}
function Stop-Desktop($Running) {
    if (-not $Running.Process.HasExited) { $Running.Process.Kill($true) }
    $Running.Process.WaitForExit()
    [IO.File]::WriteAllText($Running.Log, $Running.Output.GetAwaiter().GetResult() + $Running.Error.GetAwaiter().GetResult())
}
function Save-LinuxScreenshot {
    # DISPLAY belongs to the xvfb-run invocation that owns this script.
    if ([string]::IsNullOrWhiteSpace($env:DISPLAY)) { throw 'Run the Linux script inside xvfb-run.' }
    $imageCommand = if (Get-Command magick -ErrorAction SilentlyContinue) { 'magick' } else { 'import' }
    $imageArguments = @('-display', $env:DISPLAY, '-window', 'root', $screenshotPath)
    if ($imageCommand -eq 'magick') { $imageArguments = @('import') + $imageArguments }
    Invoke-Checked $imageCommand $imageArguments
}

try {
    if ($ownsServer) {
        if ($Platform -eq 'Windows') {
            # This service and its disposable credentials belong to the Windows hosted runner.
            Set-Service -Name 'postgresql-x64-17' -StartupType Manual
            Start-Service 'postgresql-x64-17'
            $startedPostgres = $true
            $env:PGPASSWORD = 'root'
            Invoke-Checked (Join-Path $env:PGBIN 'createdb.exe') @('-h', '127.0.0.1', '-U', 'postgres', 'chess')
            $env:ConnectionStrings__chess = 'Host=127.0.0.1;Database=chess;Username=postgres;Password=root'
        }
        # Keep dashboard tokens and resource configuration out of logs and uploaded artifacts.
        $startup = & aspire start --apphost $apphost --isolated --format Json --non-interactive --nologo -- "--Parameters:backend=$Backend" --Chess:Ephemeral=true 2>&1
        $startupExitCode = $LASTEXITCODE
        Write-NativeDiagnostic (Join-Path $artifactPath 'aspire-startup.log') ($startup -join "`n")
        if ($startupExitCode -ne 0) { throw "Aspire could not start the native verification server (exit $startupExitCode). Inspect the Aspire diagnostic artifacts." }
    }
    Invoke-Checked aspire @('wait', 'server', '--apphost', $apphost, '--timeout', '180', '--non-interactive', '--nologo')
    $description = & aspire describe --apphost $apphost --format Json --non-interactive --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Aspire could not describe the running server.' }
    $resources = ($description -join "`n" | ConvertFrom-Json).resources
    $resource = @($resources | Where-Object displayName -EQ 'server')
    if ($resource.Count -ne 1) { throw 'Expected exactly one Aspire server resource.' }
    $httpUrls = @($resource[0].urls | Where-Object name -EQ 'http')
    if ($httpUrls.Count -ne 1) { throw 'Expected exactly one server HTTP endpoint.' }
    if ($resource[0].environment.Chess__Backend -ne $Backend) { throw "The running Aspire server uses $($resource[0].environment.Chess__Backend), not requested backend $Backend." }
    if ($ownsServer) { $Server = $httpUrls[0].url }
    if ([Uri]$Server -ne [Uri]$httpUrls[0].url) { throw "The supplied URL is not the running Aspire server HTTP endpoint." }
    $serverUri = [Uri]$Server
    if (-not $serverUri.IsAbsoluteUri -or $serverUri.Scheme -notin @('http', 'https')) { throw 'An absolute HTTP server URL is required.' }
    $env:CHESS_SERVER = $serverUri.AbsoluteUri
    $env:CHESS_TEST_SERVER = $serverUri.AbsoluteUri
    Invoke-Checked dotnet @('test', '--project', 'tests/Chess.Native.Tests/Chess.Native.Tests.csproj', '--configuration', 'Release')
    $env:CHESS_SETTINGS_PATH = Join-Path $artifactPath "$($Platform.ToLowerInvariant()).session"
    Remove-Item $env:CHESS_SETTINGS_PATH -Force -ErrorAction SilentlyContinue
    $startedAt = [DateTimeOffset]::UtcNow

    if ($Platform -eq 'Android') {
        $apk = [IO.Path]::GetFullPath('clients/Chess.Android/bin/Debug/net11.0-android37.0/android-x64/org.shirubasoft.chess-Signed.apk')
        Invoke-Checked adb @('-s', $Device, 'install', '-r', $apk)
        Invoke-Checked adb @('-s', $Device, 'shell', 'am', 'force-stop', 'org.shirubasoft.chess')
        Invoke-Checked adb @('-s', $Device, 'shell', 'run-as', 'org.shirubasoft.chess', 'rm', '-f', 'files/verification.json', 'files/verification-session')
        if ($serverUri.Host -in @('localhost', '127.0.0.1')) {
            $reversePort = "tcp:$($serverUri.Port)"
            Invoke-Checked adb @('-s', $Device, 'reverse', $reversePort, $reversePort)
        }
        Invoke-Checked adb @('-s', $Device, 'shell', 'am', 'start', '-W', '-n', 'org.shirubasoft.chess/org.shirubasoft.chess.MainActivity', '--es', 'server', $serverUri.AbsoluteUri, '--ez', 'verify_ui', 'true')
        $deadline = [DateTimeOffset]::UtcNow.AddMinutes(5)
        do {
            $json = & adb -s $Device shell run-as org.shirubasoft.chess cat files/verification.json 2>$null
            if ($LASTEXITCODE -eq 0) {
                [IO.File]::WriteAllText($reportPath, ($json -join "`n"))
                break
            }
            Start-Sleep -Milliseconds 500
        } while ([DateTimeOffset]::UtcNow -lt $deadline)
    }
    else {
        $framework = if ($Platform -eq 'Windows') { 'net11.0-windows' } else { 'net11.0' }
        $assembly = [IO.Path]::GetFullPath("clients/Chess.$Platform/bin/$Configuration/$framework/Chess.$Platform.dll")
        $nativeProcess = Start-Desktop @($assembly, '--verify-ui', $reportPath) 'desktop-verification.log'
        if (-not $nativeProcess.Process.WaitForExit(300000)) { throw 'The native UI verification timed out.' }
        if ($nativeProcess.Process.ExitCode -ne 0) { throw 'The native UI verification process failed; inspect its report and log.' }
        Stop-Desktop $nativeProcess
        $nativeProcess = $null
        if ($Platform -eq 'Linux') {
            $nativeProcess = Start-Desktop @($assembly, '--resume') 'desktop-screenshot.log'
            # The successful UI scenario has already exercised rendering. Reopen its saved final
            # position for an artifact because GTK closes the verification window on completion.
            Start-Sleep -Seconds 3
            if ($nativeProcess.Process.HasExited) { throw 'The native window closed before its screenshot.' }
            Save-LinuxScreenshot
        }
    }
    if (-not (Test-Path $reportPath)) { throw 'The native UI produced no verification report.' }
    $report = Get-Content $reportPath -Raw | ConvertFrom-Json
    if ($report.success -isnot [bool] -or -not $report.success) { throw "Native verification failed: $($report.error)" }
    if ($report.client -ne "Chess $Platform") { throw 'The report came from the wrong native client.' }
    if ([DateTimeOffset]$report.startedAt -lt $startedAt.AddSeconds(-5)) { throw 'The native verification report is stale.' }
    if ([Uri]$report.server -ne $serverUri) { throw 'The native client verified against a different server.' }
    if (@($report.checks).Count -ne 8 -or -not $report.gameId) { throw 'The native verification report is incomplete.' }
    Write-Host "$Platform native UI passed all $(@($report.checks).Count) crossplay checks with $Backend."
}
catch {
    $verificationFailure = $_
    Write-NativeDiagnostic (Join-Path $artifactPath 'verification-failure.log') ($verificationFailure | Out-String)
    try { Save-NativeFailureDiagnostics $artifactPath $apphost $runStartedAt }
    catch { Write-Warning "Could not retain all Aspire diagnostics: $($_.Exception.Message)" }
    throw $verificationFailure
}
finally {
    if ($Platform -eq 'Linux' -and -not (Test-Path $screenshotPath)) {
        try { Save-LinuxScreenshot } catch { Write-Warning 'Could not capture the Linux failure screen.' }
    }
    if ($nativeProcess) { Stop-Desktop $nativeProcess }
    if ($Platform -eq 'Android') {
        & adb -s $Device shell screencap -p /sdcard/chess-native-verification.png | Out-Null
        if ($LASTEXITCODE -eq 0) { & adb -s $Device pull /sdcard/chess-native-verification.png $screenshotPath | Out-Null }
        & adb -s $Device logcat -d -s 'ChessVerification:I' 'AndroidRuntime:E' '*:S' > (Join-Path $artifactPath 'android-verification.log')
        if ($reversePort) { & adb -s $Device reverse --remove $reversePort | Out-Null }
    }
    if ($ownsServer) {
        & aspire resource server stop --apphost $apphost --non-interactive --nologo | Out-Null
        & aspire stop --apphost $apphost --non-interactive --nologo | Out-Null
    }
    if ($startedPostgres) { Stop-Service 'postgresql-x64-17' }
}
if (-not (Test-Path $screenshotPath)) { throw 'The native UI screenshot is missing.' }

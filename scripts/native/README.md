# Native UI verification

`native-clients.yml` executes the GTK, WPF, and Android applications against both Akka and Orleans. Each job builds its native application before starting the server, starts AppHost through Aspire CLI 13.5.3, waits for server health, and reads the server endpoint from `aspire describe --format Json`. The script checks the resolved `Chess__Backend` value before running any UI scenario.

The application drives its actual native buttons and text fields, then verifies server state from an opponent using `Chess.Client`. It checks game creation and side codes, board clicks, opponent name, polling, SAN entry and history, resignation, joining by code, and knight promotion. A fresh JSON report must contain `success: true` and all five completed check groups. The workflow uploads the reports, screenshots, application logs, and Android ARM64 Release APKs. Generated side-code settings and raw Aspire resource descriptions are excluded from uploads.

## Run locally

Install the SDK selected by `global.json`, .NET 10 for AppHost, PowerShell 7, Aspire CLI 13.5.3, and the platform dependencies listed in the workflow. Build the desired client first. The scripts run from any working directory and launch compiled assemblies without rebuilding shared files while the server is running.

Linux requires GTK4, Xvfb, ImageMagick, and DejaVu fonts:

```sh
dotnet build clients/Chess.Linux/Chess.Linux.csproj -c Release
GDK_BACKEND=x11 GTK_A11Y=none xvfb-run -a -s '-screen 0 1280x1024x24 -nolisten tcp' \
  pwsh -NoProfile -File scripts/native/test-ui.ps1 -Platform Linux -Backend Orleans
```

Docker must be running for the local Linux and Android AppHost database. The Linux screenshot reopens the saved final position after the verification window closes, inside the same isolated display.

On Windows, the CI script uses the [Windows 2025 runner's PostgreSQL 17 service](https://github.com/actions/runner-images/blob/main/images/windows/Windows2025-Readme.md), creates a disposable `chess` database, and provides AppHost's `ConnectionStrings:chess` configuration. It starts and stops that runner service automatically. WPF takes its screenshot before closing the verification window.

```powershell
dotnet build clients/Chess.Windows/Chess.Windows.csproj -c Release
./scripts/native/test-ui.ps1 -Platform Windows -Backend Orleans
```

For an ordinary Windows workstation, start a configured AppHost separately and pass its HTTP URL with `-Server`; this leaves its database and AppHost lifecycle under your control.

Android needs a compatible JDK, Android SDK37.0/build-tools37.0.0, the SDK-selected Android workload, and a running x86_64 emulator. CI installs [Microsoft OpenJDK 21](https://learn.microsoft.com/en-us/dotnet/android/getting-started/installation/dependencies) and creates its own API35 emulator with [android-emulator-runner](https://github.com/ReactiveCircus/android-emulator-runner). The test runs the Debug x64 APK because extracting the app-private report uses Android's `run-as`; the ARM64 Release APK is a separate packaging check and artifact.

```sh
dotnet build clients/Chess.Android/Chess.Android.csproj -c Debug -r android-x64 \
  -p:AndroidSdkDirectory="$ANDROID_SDK_ROOT" -p:JavaSdkDirectory="$JAVA_HOME"
dotnet build clients/Chess.Android/Chess.Android.csproj -c Release -r android-arm64 \
  -p:AndroidSdkDirectory="$ANDROID_SDK_ROOT" -p:JavaSdkDirectory="$JAVA_HOME"
pwsh -NoProfile -File scripts/native/test-ui.ps1 -Platform Android -Backend Orleans -Device emulator-5554
```

The Android script installs the APK, resets only the app's verification session, reverses the allocated server port through ADB, launches the Activity, retrieves the report, and captures the device screen. It does not create or delete local AVDs.

Pass `-Server http://localhost:PORT` to reuse the current checkout's running Aspire server without stopping it afterward. The supplied URL and backend must match the running server's description. Pass `-ArtifactDirectory artifacts/native/orleans` to keep separate local runs, and `-Configuration Debug` when testing a desktop Debug build. Android always executes the Debug x64 APK.

The native scenario runs inside each application's UI thread and invokes native controls. This verifies rendered board state and client/server behavior; it does not simulate hardware touch, keyboard navigation, screen-reader interaction, or physical ARM64 devices. Stable control IDs also support separate external accessibility automation.

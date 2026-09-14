#!/usr/bin/env bash
set -euo pipefail

usage() {
    cat <<'HELP'
Usage: scripts/test-frontends.sh [Akka|Orleans] [--install-browser-deps]

Starts this checkout's Aspire AppHost in isolated mode with an ephemeral test
database, checks its selected backend, runs CLI, Web, crossplay, and native
presentation tests sequentially,
then stops the AppHost. An already running AppHost is left untouched.

Requires Linux, dotnet, Python 3, PowerShell, util-linux script, and a working
container runtime. The Aspire CLI version comes from Chess.AppHost.csproj;
a matching installed CLI is reused or installed in artifacts/tools.

Chromium is installed through Playwright. --install-browser-deps also installs
its OS dependencies (automatic when CI=true or CI=1).

Environment:
  CHESS_FRONTEND_CONFIGURATION         Build configuration (default: Release)
  CHESS_FRONTEND_INSTALL_BROWSER_DEPS  0 or 1; overrides the CI default
  CHESS_FRONTEND_ARTIFACTS             Output parent (default: artifacts/frontend-tests)
  CHESS_ASPIRE                         Explicit Aspire executable; version must match

The runner discovers and exports CHESS_TEST_SERVER, CHESS_SERVER_URL, and
CHESS_WEB_URL, and enforces CHESS_REQUIRE_CROSSPLAY=1. Reports must contain
only passing tests; skipped tests fail the run. Native presentation/session
tests do not replace Android, WPF, or GTK process/UI verification.
HELP
}

backend=Akka
backend_set=0
browser_deps="${CHESS_FRONTEND_INSTALL_BROWSER_DEPS:-}"
if [[ -z "$browser_deps" ]]; then
    case "${CI:-}" in true|1) browser_deps=1 ;; *) browser_deps=0 ;; esac
fi
for argument in "$@"; do
    case "$argument" in
        Akka|Orleans)
            if (( backend_set )); then usage >&2; exit 2; fi
            backend="$argument"
            backend_set=1
            ;;
        --install-browser-deps) browser_deps=1 ;;
        --help|-h) usage; exit 0 ;;
        *) printf 'Unknown argument: %s\n' "$argument" >&2; usage >&2; exit 2 ;;
    esac
done
if [[ "$browser_deps" != 0 && "$browser_deps" != 1 ]]; then
    printf 'CHESS_FRONTEND_INSTALL_BROWSER_DEPS must be 0 or 1.\n' >&2
    exit 2
fi

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"
apphost="$repo/Chess.AppHost/Chess.AppHost.csproj"
inspect="$repo/scripts/frontend/inspect_outputs.py"
configuration="${CHESS_FRONTEND_CONFIGURATION:-Release}"
case "$configuration" in Debug|Release) ;; *) printf 'Use Debug or Release configuration.\n' >&2; exit 2 ;; esac

for dependency in dotnet python3 pwsh; do
    if ! command -v "$dependency" >/dev/null; then
        printf 'Required command is missing: %s\n' "$dependency" >&2
        exit 1
    fi
done
if [[ "$(uname -s)" != Linux || ! -x /usr/bin/script ]]; then
    printf 'This runner requires Linux and /usr/bin/script for the CLI terminal tests.\n' >&2
    exit 1
fi

aspire_version="$(python3 "$inspect" aspire-version "$apphost")"
aspire="${CHESS_ASPIRE:-$(command -v aspire || true)}"
actual_version=""
if [[ -n "$aspire" ]]; then
    actual_version="$("$aspire" --version)"
    actual_version="${actual_version%%+*}"
fi
if [[ "$actual_version" != "$aspire_version" ]]; then
    if [[ -n "${CHESS_ASPIRE:-}" ]]; then
        printf 'CHESS_ASPIRE must be version %s, but reported %s.\n' "$aspire_version" "$actual_version" >&2
        exit 1
    fi
    aspire_directory="$repo/artifacts/tools/aspire/$aspire_version"
    aspire="$aspire_directory/aspire"
    if [[ ! -x "$aspire" ]]; then
        dotnet tool install Aspire.Cli --version "$aspire_version" --tool-path "$aspire_directory"
    fi
    actual_version="$("$aspire" --version)"
    if [[ "${actual_version%%+*}" != "$aspire_version" ]]; then
        printf 'The scoped Aspire CLI does not match the AppHost SDK pin.\n' >&2
        exit 1
    fi
fi
aspire_options=(--apphost "$apphost" --non-interactive --nologo)
"$aspire" ps --format Json --non-interactive --nologo | python3 "$inspect" assert-idle "$apphost"
python3 "$repo/scripts/frontend/test_inspect.py"

artifact_parent="${CHESS_FRONTEND_ARTIFACTS:-$repo/artifacts/frontend-tests}"
mkdir -p "$artifact_parent"
artifact_parent="$(cd "$artifact_parent" && pwd)"
run_directory="$(mktemp -d "$artifact_parent/$backend.XXXXXXXX")"
mkdir -p "$run_directory/reports" "$run_directory/screenshots" "$run_directory/packages"
printf 'Frontend artifacts: %s\n' "$run_directory"

started=0
cleanup() {
    result=$?
    trap - EXIT INT TERM
    if (( started )); then
        if (( result != 0 )); then
            "$aspire" logs server "${aspire_options[@]}" --tail 150 >"$run_directory/server.log" 2>&1 || true
            "$aspire" logs web "${aspire_options[@]}" --tail 150 >"$run_directory/web.log" 2>&1 || true
        fi
        # Let Orleans leave its cluster while PostgreSQL is still available.
        "$aspire" resource server stop "${aspire_options[@]}" || true
        if ! "$aspire" stop "${aspire_options[@]}"; then
            printf 'Aspire cleanup failed. Stop this AppHost before another run.\n' >&2
            if (( result == 0 )); then result=1; fi
        fi
    fi
    printf 'Frontend artifacts: %s\n' "$run_directory"
    exit "$result"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

projects=(Chess.Cli.Tests Chess.Web.Tests Chess.Crossplay.Tests Chess.Native.Tests)
for project in "${projects[@]}"; do
    dotnet build "tests/$project/$project.csproj" --configuration "$configuration" --nologo
done

playwright_script="$(dotnet msbuild tests/Chess.Crossplay.Tests/Chess.Crossplay.Tests.csproj \
    -property:Configuration="$configuration" -getProperty:TargetDir)/playwright.ps1"
if [[ "$browser_deps" == 1 ]]; then
    pwsh -NoProfile "$playwright_script" install --with-deps chromium
else
    pwsh -NoProfile "$playwright_script" install chromium
fi

dotnet pack clients/Chess.Cli/Chess.Cli.csproj --configuration "$configuration" --no-build \
    --output "$run_directory/packages" --nologo
package_version="$(python3 "$inspect" package-version "$run_directory/packages")"
dotnet tool install Chess.Cli --version "$package_version" --source "$run_directory/packages" \
    --tool-path "$run_directory/tool"
PATH="$run_directory/tool:$PATH" dotnet chess help >"$run_directory/cli-tool-help.txt"
if [[ "$(cat "$run_directory/cli-tool-help.txt")" != *"dotnet chess move"* ]]; then
    printf 'The installed dotnet chess tool did not produce its command help.\n' >&2
    exit 1
fi

# Arm cleanup before start so a partially started application is stopped too.
started=1
"$aspire" start "${aspire_options[@]}" --isolated -- --Parameters:backend="$backend" --Chess:Ephemeral=true
"$aspire" wait server "${aspire_options[@]}" --timeout 180
"$aspire" wait web "${aspire_options[@]}" --timeout 180
description="$("$aspire" describe "${aspire_options[@]}" --format Json)"
CHESS_TEST_SERVER="$(printf '%s' "$description" | python3 scripts/server-url.py "$backend" | python3 "$inspect" url)"
CHESS_WEB_URL="$(printf '%s' "$description" | python3 "$inspect" web-url)"
unset description
CHESS_SERVER_URL="$CHESS_TEST_SERVER"
CHESS_REQUIRE_CROSSPLAY=1
CHESS_SCREENSHOT_DIRECTORY="$run_directory/screenshots"
export CHESS_TEST_SERVER CHESS_SERVER_URL CHESS_WEB_URL CHESS_REQUIRE_CROSSPLAY CHESS_SCREENSHOT_DIRECTORY
printf 'Backend: %s\nServer: %s\nWeb: %s\n' "$backend" "$CHESS_TEST_SERVER" "$CHESS_WEB_URL"

# Separate processes must not compete for the server's random matchmaking queue.
for project in "${projects[@]}"; do
    report="$run_directory/reports/$project.trx"
    dotnet test --project "tests/$project/$project.csproj" --configuration "$configuration" --no-build \
        --results-directory "$run_directory/reports" --report-trx --report-trx-filename "$report" \
        --report-html-filename "$run_directory/reports/$project.html"
    python3 "$inspect" results "$report"
done

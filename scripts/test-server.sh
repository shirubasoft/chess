#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
backend="${1:-Akka}"
apphost=Chess.AppHost/Chess.AppHost.csproj
cleanup() {
  aspire resource server stop --apphost "$apphost" --non-interactive || true
  aspire stop --apphost "$apphost" --non-interactive
}
trap cleanup EXIT
cluster="server-test-$(python3 -c 'import uuid; print(uuid.uuid4().hex)')"
aspire start --apphost "$apphost" --isolated --non-interactive -- --Parameters:backend="$backend" --Parameters:orleans-cluster-id="$cluster"
aspire wait server --apphost "$apphost" --non-interactive
CHESS_TEST_SERVER="$(aspire describe --apphost "$apphost" --format Json --non-interactive | python3 scripts/server-url.py "$backend")"
export CHESS_TEST_SERVER
dotnet test --project tests/Chess.Server.Tests/Chess.Server.Tests.csproj --configuration Release
dotnet test --project tests/Chess.Cli.Tests/Chess.Cli.Tests.csproj --configuration Release

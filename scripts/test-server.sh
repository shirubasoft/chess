#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
backend="${1:-Postgres}"
apphost=Chess.AppHost/Chess.AppHost.csproj
cleanup() { aspire stop --apphost "$apphost" --non-interactive; }
trap cleanup EXIT
aspire start --apphost "$apphost" --isolated --non-interactive -- --Parameters:backend="$backend"
aspire wait server --apphost "$apphost" --non-interactive
CHESS_TEST_SERVER="$(aspire describe --apphost "$apphost" --format Json --non-interactive | python3 -c '
import json, sys
resources = json.load(sys.stdin)["resources"]
server = next(r for r in resources if r["displayName"] == "server")
print(next(u["url"] for u in server["urls"] if u["name"] == "http"))
')"
export CHESS_TEST_SERVER
dotnet test --project tests/Chess.Server.Tests/Chess.Server.Tests.csproj --configuration Release

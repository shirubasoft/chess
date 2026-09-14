#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
apphost=Chess.AppHost/Chess.AppHost.csproj
state="$(mktemp)"
cluster="switch-$(python3 -c 'import uuid; print(uuid.uuid4().hex)')"
cleanup() {
  rm -f "$state"
  aspire resource server stop --apphost "$apphost" --non-interactive || true
  aspire stop --apphost "$apphost" --non-interactive
}
trap cleanup EXIT
start() {
  aspire start --apphost "$apphost" --isolated --non-interactive -- --Parameters:backend="$1" --Parameters:orleans-cluster-id="$cluster"
  aspire wait server --apphost "$apphost" --non-interactive
  server="$(aspire describe --apphost "$apphost" --format Json --non-interactive | python3 scripts/server-url.py "$1")"
}
start Akka
dotnet run --project tools/Chess.Server.Validation --configuration Release -- prepare "$server" "$state"
for backend in Orleans Akka Orleans; do
  aspire resource server stop --apphost "$apphost" --non-interactive
  aspire stop --apphost "$apphost" --non-interactive
  start "$backend"
  dotnet run --project tools/Chess.Server.Validation --configuration Release --no-build -- continue "$server" "$state"
done

"""Read the public server URL only after checking Aspire's resolved backend."""
import json
import sys

server = next(r for r in json.load(sys.stdin)["resources"] if r["displayName"] == "server")
actual = server["environment"].get("Chess__Backend")
if actual != sys.argv[1]:
    raise SystemExit(f"Expected backend {sys.argv[1]}, but Aspire resolved {actual}.")
print(next(u["url"] for u in server["urls"] if u["name"] == "http"))

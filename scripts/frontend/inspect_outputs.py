"""Validate the structured outputs consumed by the frontend CI runner."""

import argparse
import json
from pathlib import Path
import sys
from urllib.parse import urlsplit
import xml.etree.ElementTree as ET
import zipfile


def http_url(value):
    if not isinstance(value, str) or any(ord(character) <= 32 or ord(character) == 127 for character in value):
        raise ValueError("Endpoint must be a URL without whitespace.")
    url = urlsplit(value)
    if (url.scheme not in ("http", "https") or not url.hostname
            or url.username is not None or url.password is not None
            or url.query or url.fragment):
        raise ValueError("Endpoint must be an HTTP(S) URL without credentials, query, or fragment.")
    _ = url.port  # Reject malformed ports before exporting the endpoint.
    return value


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("aspire-version").add_argument("project", type=Path)
    commands.add_parser("assert-idle").add_argument("apphost", type=Path)
    commands.add_parser("web-url")
    commands.add_parser("url")
    commands.add_parser("package-version").add_argument("directory", type=Path)
    commands.add_parser("results").add_argument("report", type=Path)
    args = parser.parse_args()

    if args.command == "aspire-version":
        sdk = ET.parse(args.project).getroot().get("Sdk", "")
        prefix = "Aspire.AppHost.Sdk/"
        if not sdk.startswith(prefix) or not sdk[len(prefix):]:
            raise ValueError("AppHost must pin Aspire.AppHost.Sdk to a specific version.")
        print(sdk[len(prefix):])
    elif args.command == "assert-idle":
        instances = json.load(sys.stdin)
        if not isinstance(instances, list):
            raise ValueError("Expected an array from aspire ps.")
        if any(Path(instance["appHostPath"]).resolve() == args.apphost.resolve()
               for instance in instances):
            raise ValueError("This AppHost is already running. Stop it before running frontend tests.")
    elif args.command == "web-url":
        resources = json.load(sys.stdin)["resources"]
        matches = [resource for resource in resources if resource["displayName"] == "web"]
        if len(matches) != 1:
            raise ValueError("Expected exactly one Aspire resource with displayName 'web'.")
        endpoints = [endpoint["url"] for endpoint in matches[0]["urls"] if endpoint["name"] == "http"]
        if len(endpoints) != 1:
            raise ValueError("The web resource must expose one named 'http' endpoint.")
        print(http_url(endpoints[0]))
    elif args.command == "url":
        print(http_url(sys.stdin.read().strip()))
    elif args.command == "package-version":
        packages = list(args.directory.glob("*.nupkg"))
        if len(packages) != 1:
            raise ValueError("Expected exactly one newly packed CLI package.")
        with zipfile.ZipFile(packages[0]) as package:
            manifests = [name for name in package.namelist() if name.endswith(".nuspec")]
            if len(manifests) != 1:
                raise ValueError("Expected one NuGet package manifest.")
            manifest = ET.fromstring(package.read(manifests[0]))
        metadata = manifest.find("{*}metadata")
        if metadata is None or metadata.findtext("{*}id") != "Chess.Cli":
            raise ValueError("The packed tool must be Chess.Cli.")
        version = metadata.findtext("{*}version")
        if not version:
            raise ValueError("The packed tool has no version.")
        print(version)
    elif args.command == "results":
        results = ET.parse(args.report).getroot()
        counters = results.find("{*}ResultSummary/{*}Counters")
        if counters is None:
            raise ValueError(f"{args.report} has no TRX result counters.")
        total = int(counters.get("total", "0"))
        passed = int(counters.get("passed", "0"))
        executed = int(counters.get("executed", "0"))
        if total == 0 or total != passed or total != executed:
            raise ValueError(f"{args.report.name}: required tests must all pass; "
                             f"total={total}, executed={executed}, passed={passed}.")
        print(f"{args.report.stem}: {passed} passed; no skipped tests.")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, KeyError, OSError, ET.ParseError, zipfile.BadZipFile) as error:
        raise SystemExit(str(error)) from error

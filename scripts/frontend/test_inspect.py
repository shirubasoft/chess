"""Checks for endpoint parsing and mandatory-test report enforcement."""

import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


HELPER = Path(__file__).with_name("inspect_outputs.py")


class InspectionTests(unittest.TestCase):
    def invoke(self, command, *args, input=""):
        return subprocess.run([sys.executable, str(HELPER), command, *map(str, args)],
                              input=input, text=True, capture_output=True, check=False)

    def test_web_endpoint_is_selected_by_resource_and_name(self):
        description = {"resources": [
            {"displayName": "server", "urls": [{"name": "http", "url": "http://localhost:5080"}]},
            {"displayName": "web", "urls": [
                {"name": "https", "url": "https://localhost:7443"},
                {"name": "http", "url": "http://localhost:5090"}]}]}
        result = self.invoke("web-url", input=json.dumps(description))
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout.strip(), "http://localhost:5090")

    def test_missing_or_duplicate_web_is_rejected(self):
        resource = {"displayName": "web", "urls": [{"name": "http", "url": "http://localhost:5090"}]}
        for resources in ([], [resource, resource]):
            with self.subTest(resources=resources):
                self.assertNotEqual(self.invoke("web-url", input=json.dumps({"resources": resources})).returncode, 0)

    def test_unsafe_or_malformed_urls_are_rejected(self):
        for url in ("file:///tmp/x", "http://user:password@localhost", "http://localhost:bad",
                    "http://localhost/?token=secret", "http://localhost/#x", "http://local\nhost",
                    "http://local\0host"):
            with self.subTest(url=url):
                self.assertNotEqual(self.invoke("url", input=url).returncode, 0)

    def test_existing_checkout_apphost_is_rejected(self):
        apphost = Path.cwd() / "Chess.AppHost/Chess.AppHost.csproj"
        instances = [{"appHostPath": str(apphost), "status": "running"}]
        self.assertNotEqual(self.invoke("assert-idle", apphost, input=json.dumps(instances)).returncode, 0)
        self.assertEqual(self.invoke("assert-idle", apphost, input="[]").returncode, 0)

    def test_zero_skipped_or_failed_results_are_rejected(self):
        for total, executed, passed in ((0, 0, 0), (21, 20, 20), (21, 21, 20)):
            with self.subTest(total=total, executed=executed, passed=passed):
                result = self.report(total, executed, passed)
                self.assertNotEqual(result.returncode, 0)

    def test_complete_passing_results_are_accepted(self):
        result = self.report(21, 21, 21)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("21 passed; no skipped tests", result.stdout)

    def report(self, total, executed, passed):
        with tempfile.TemporaryDirectory() as directory:
            report = Path(directory) / "crossplay.trx"
            report.write_text(f'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
                              f'<ResultSummary><Counters total="{total}" executed="{executed}" '
                              f'passed="{passed}" /></ResultSummary></TestRun>')
            return self.invoke("results", report)


if __name__ == "__main__":
    unittest.main()

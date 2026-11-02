import importlib.util
import io
import json
import os
import runpy
import tempfile
import unittest
from contextlib import redirect_stdout
from datetime import datetime, timezone
from pathlib import Path
from unittest.mock import patch
from urllib.error import HTTPError
from urllib.parse import parse_qs, urlparse

spec = importlib.util.spec_from_file_location(
    "daily_schedule", Path(__file__).parents[1] / "Deployment" / "daily_schedule.py"
)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class DailyScheduleTests(unittest.TestCase):
    def check(self, timestamp, first_hour):
        now = datetime.fromisoformat(timestamp).replace(tzinfo=timezone.utc)
        for hour in range(24):
            with self.subTest(hour=hour):
                self.assertEqual(
                    first_hour <= hour <= first_hour + 3,
                    module.should_run("schedule", f"0 {hour} * * *", now),
                )

    def test_winter_with_hours_of_delay(self):
        self.check("2026-11-01T13:42:00", 7)

    def test_summer_with_hours_of_delay(self):
        self.check("2026-07-15T16:10:00", 6)

    def test_spring_transition_day_even_before_transition(self):
        self.check("2026-03-29T00:10:00", 6)

    def test_autumn_transition_day_even_before_transition(self):
        self.check("2026-10-25T00:10:00", 7)

    def test_manual_is_eligible_outside_scheduled_hours(self):
        self.assertTrue(module.should_run("workflow_dispatch", "", datetime.now(timezone.utc)))

    def test_unknown_event_or_cron_does_not_run(self):
        now = datetime.now(timezone.utc)
        self.assertFalse(module.should_run("push", "", now))
        self.assertFalse(module.should_run("schedule", "0 11 * * *", now))

    def test_default_target_uses_skopje_and_manual_target_is_preserved(self):
        now = datetime.fromisoformat("2026-11-02T23:30:00+00:00")
        self.assertEqual("2026-11-03", module.target_date("", now))
        self.assertEqual("2026-10-31", module.target_date("2026-10-31", now))
        for invalid in ["20261102", "bad-date", "2026-02-30", "2026-1-2"]:
            with self.subTest(invalid=invalid), self.assertRaises(ValueError):
                module.target_date(invalid, now)

    def plan(self, event="schedule", schedule="0 7 * * *", requested="", artifacts=None,
             status="completed", conclusion="success", path=".github/workflows/daily-run.yml"):
        calls = []

        def request(url, token):
            calls.append(url)
            self.assertEqual("test-token", token)
            if "/actions/artifacts?" in url:
                target = requested or "2026-11-02"
                self.assertEqual([f"daily-success-{target}"],
                                 parse_qs(urlparse(url).query)["name"])
                return {"artifacts": artifacts or []}
            self.assertTrue(url.endswith("/actions/runs/123"))
            return {"status": status, "conclusion": conclusion, "path": path}

        result = module.plan_run(
            event, schedule, requested, datetime.fromisoformat("2026-11-02T07:00:00+00:00"),
            "owner/repo", "test-token", "https://api.github.com", request,
        )
        return result, calls

    def artifact(self, name="daily-success-2026-11-02", expired=False):
        return {"name": name, "expired": expired, "workflow_run": {"id": 123}}

    def test_each_of_four_attempts_runs_without_a_success_record(self):
        for hour in range(7, 11):
            with self.subTest(hour=hour):
                (run, target, _), _ = self.plan(schedule=f"0 {hour} * * *")
                self.assertTrue(run)
                self.assertEqual("2026-11-02", target)

    def test_successful_manual_or_scheduled_execution_blocks_all_later_attempts(self):
        for event, schedule in [("schedule", f"0 {hour} * * *") for hour in range(7, 11)] + [
                ("workflow_dispatch", "")]:
            with self.subTest(event=event, schedule=schedule):
                (run, _, message), _ = self.plan(
                    event=event, schedule=schedule, artifacts=[self.artifact()])
                self.assertFalse(run)
                self.assertIn("already exists", message)

    def test_failed_cancelled_or_unfinished_runs_do_not_block_retry(self):
        for status, conclusion in [("completed", "failure"), ("completed", "cancelled"),
                                   ("in_progress", None)]:
            with self.subTest(status=status, conclusion=conclusion):
                (run, _, _), _ = self.plan(
                    artifacts=[self.artifact()], status=status, conclusion=conclusion)
                self.assertTrue(run)

    def test_other_date_expired_marker_and_other_workflow_do_not_block_retry(self):
        for artifact, path in [
                (self.artifact(name="daily-success-2026-11-01"), ".github/workflows/daily-run.yml"),
                (self.artifact(expired=True), ".github/workflows/daily-run.yml"),
                (self.artifact(), ".github/workflows/other.yml")]:
            with self.subTest(artifact=artifact, path=path):
                (run, _, _), _ = self.plan(artifacts=[artifact], path=path)
                self.assertTrue(run)

    def test_manual_backfill_uses_its_own_success_marker(self):
        (run, target, _), _ = self.plan(
            event="workflow_dispatch", requested="2026-10-31",
            artifacts=[self.artifact(name="daily-success-2026-10-31")])
        self.assertFalse(run)
        self.assertEqual("2026-10-31", target)

    def test_inactive_occurrence_does_not_query_success_records(self):
        (run, _, _), calls = self.plan(schedule="0 6 * * *")
        self.assertFalse(run)
        self.assertEqual([], calls)

    def test_marker_lookup_is_paginated(self):
        calls = []

        def request(url, token):
            calls.append(url)
            if "/actions/runs/" in url:
                return {"status": "completed", "conclusion": "success",
                        "path": ".github/workflows/daily-run.yml"}
            page = parse_qs(urlparse(url).query)["page"]
            return {"artifacts": [self.artifact(expired=True)] * 100 if page == ["1"]
                    else [self.artifact()]}

        self.assertTrue(module.has_successful_run(
            "2026-11-02", "owner/repo", "test-token", "https://api.github.com", request))
        self.assertEqual(3, len(calls))

    def test_github_failure_does_not_silently_allow_another_run(self):
        def request(url, token):
            raise HTTPError(url, 403, "Forbidden", {}, None)

        with self.assertRaises(HTTPError):
            module.plan_run(
                "schedule", "0 7 * * *", "", datetime.fromisoformat("2026-11-02T07:00:00+00:00"),
                "owner/repo", "test-token", "https://api.github.com", request,
            )

    def test_entrypoint_writes_run_decision_and_frozen_target_to_github_output(self):
        for already_succeeded in [False, True]:
            with self.subTest(already_succeeded=already_succeeded), tempfile.TemporaryDirectory() as temp:
                output = Path(temp) / "github-output"
                artifacts = [self.artifact(name="daily-success-2026-10-31")] if already_succeeded else []
                responses = [{"artifacts": artifacts}]
                if already_succeeded:
                    responses.append({"status": "completed", "conclusion": "success",
                                      "path": ".github/workflows/daily-run.yml"})
                environment = {
                    "EVENT_NAME": "workflow_dispatch", "EVENT_SCHEDULE": "",
                    "TARGET_DATE": "2026-10-31", "GITHUB_REPOSITORY": "owner/repo",
                    "GITHUB_TOKEN": "test-token", "GITHUB_API_URL": "https://api.github.com",
                    "GITHUB_OUTPUT": str(output),
                }
                with patch.dict(os.environ, environment), patch(
                        "urllib.request.urlopen",
                        side_effect=[io.BytesIO(json.dumps(value).encode()) for value in responses]
                ), redirect_stdout(io.StringIO()):
                    runpy.run_path(str(Path(__file__).parents[1] / "Deployment" / "daily_schedule.py"),
                                   run_name="__main__")
                decision = "false" if already_succeeded else "true"
                self.assertEqual(f"run={decision}\ntarget_date=2026-10-31\n", output.read_text())


if __name__ == "__main__":
    unittest.main()

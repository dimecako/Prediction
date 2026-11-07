import importlib.util
import io
import json
import os
import re
import runpy
import tempfile
import unittest
from contextlib import redirect_stdout
from datetime import datetime, time, timezone
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
    def check(self, timestamp):
        now = datetime.fromisoformat(timestamp).replace(tzinfo=timezone.utc)
        for hour in range(24):
            with self.subTest(hour=hour):
                scheduled = datetime.combine(now.date(), time(7, hour), timezone.utc)
                expected = (hour in range(5, 9) and
                            7 <= scheduled.astimezone(module.LOCAL_ZONE).hour <= 9)
                self.assertEqual(
                    expected,
                    module.should_run("schedule", f"7 {hour} * * *", now),
                )

    def test_winter_with_hours_of_delay(self):
        self.check("2026-11-01T13:42:00")

    def test_summer_with_hours_of_delay(self):
        self.check("2026-07-15T16:10:00")

    def test_spring_transition_day_even_before_transition(self):
        self.check("2026-03-29T00:10:00")

    def test_autumn_transition_day_even_before_transition(self):
        self.check("2026-10-25T00:10:00")

    def test_manual_is_eligible_outside_scheduled_hours(self):
        self.assertTrue(module.should_run("workflow_dispatch", "", datetime.now(timezone.utc)))

    def test_unknown_event_or_cron_does_not_run(self):
        now = datetime.now(timezone.utc)
        self.assertFalse(module.should_run("push", "", now))
        self.assertFalse(module.should_run("schedule", "0 11 * * *", now))
        self.assertFalse(module.should_run("schedule", "0 7 * * *", now))

    def test_workflow_crons_cover_local_attempts_in_both_seasons(self):
        workflow = (Path(__file__).parents[1] / ".github" / "workflows" /
                    "daily-run.yml").read_text(encoding="utf-8")
        crons = re.findall(r'cron: "([^"]+)"', workflow)
        self.assertEqual([f"7 {hour} * * *" for hour in range(5, 9)], crons)
        winter = datetime.fromisoformat("2026-11-06T16:37:00+00:00")
        summer = datetime.fromisoformat("2026-07-15T16:37:00+00:00")
        self.assertEqual(
            [False, True, True, True],
            [module.should_run("schedule", cron, winter) for cron in crons],
        )
        self.assertEqual(
            [True, True, True, False],
            [module.should_run("schedule", cron, summer) for cron in crons],
        )

    def test_default_target_uses_skopje_and_manual_target_is_preserved(self):
        now = datetime.fromisoformat("2026-11-02T23:30:00+00:00")
        self.assertEqual("2026-11-03", module.target_date("", now))
        self.assertEqual("2026-10-31", module.target_date("2026-10-31", now))
        for invalid in ["20261102", "bad-date", "2026-02-30", "2026-1-2"]:
            with self.subTest(invalid=invalid), self.assertRaises(ValueError):
                module.target_date(invalid, now)

    def plan(self, event="schedule", schedule="7 7 * * *", requested="", artifacts=None,
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

    def test_each_of_three_attempts_is_eligible_without_a_success_record(self):
        for hour in range(6, 9):
            with self.subTest(hour=hour):
                (run, target, _), _ = self.plan(schedule=f"7 {hour} * * *")
                self.assertTrue(run)
                self.assertEqual("2026-11-02", target)

    def test_successful_manual_or_scheduled_execution_blocks_all_later_attempts(self):
        for event, schedule in [("schedule", f"7 {hour} * * *") for hour in range(6, 9)] + [
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
        (run, _, _), calls = self.plan(schedule="7 5 * * *")
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

    def test_latest_checkpoint_uses_only_completed_runs_of_daily_workflow(self):
        artifact = self.artifact(name="daily-checkpoint-2026-11-02-15")

        def request(url, token):
            if "/actions/artifacts?" in url:
                self.assertNotIn("name", parse_qs(urlparse(url).query))
                return {"artifacts": [artifact]}
            return {"status": "completed", "path": ".github/workflows/daily-run.yml"}

        self.assertEqual(("123", "daily-checkpoint-2026-11-02-15"),
                         module.latest_checkpoint(
                             "2026-11-02", 4, "owner/repo", "test-token",
                             "https://api.github.com", request))

    def test_date_only_checkpoint_is_restored_even_when_source_revision_changes(self):
        def request(url, token):
            if "/actions/artifacts?" in url:
                return {"artifacts": [self.artifact(name="daily-checkpoint-2026-11-02")]}
            return {"status": "completed", "path": ".github/workflows/daily-run.yml",
                    "head_sha": "previous-deployment"}

        self.assertEqual(("123", "daily-checkpoint-2026-11-02"), module.latest_checkpoint(
            "2026-11-02", 4, "owner/repo", "test-token", "https://api.github.com", request))

    def test_persisted_reservation_restores_budget_when_final_checkpoint_is_missing(self):
        name = "daily-attempt-2026-11-02-123-2"

        def request(url, token):
            if "/actions/artifacts?" in url:
                return {"artifacts": [self.artifact(name=name),
                                      self.artifact(name="daily-checkpoint-2026-11-02")]}
            return {"status": "completed", "path": ".github/workflows/daily-run.yml"}

        self.assertEqual(("123", name), module.latest_checkpoint(
            "2026-11-02", 15, "owner/repo", "test-token", "https://api.github.com", request))

    def test_checkpoint_lookup_is_paginated_and_ignores_other_dates(self):
        def request(url, token):
            if "/actions/runs/" in url:
                return {"status": "completed", "path": ".github/workflows/daily-run.yml"}
            page = parse_qs(urlparse(url).query)["page"]
            return {"artifacts": [self.artifact(name="daily-checkpoint-2026-11-01")] * 100
                    if page == ["1"] else [self.artifact(name="daily-checkpoint-2026-11-02")]}

        self.assertEqual(("123", "daily-checkpoint-2026-11-02"), module.latest_checkpoint(
            "2026-11-02", 15, "owner/repo", "test-token", "https://api.github.com", request))

    def test_reservation_enforces_three_attempts_across_count_revision_and_manual_retry(self):
        with tempfile.TemporaryDirectory(dir=Path(__file__).parent) as directory:
            path = Path(directory) / "nested" / "daily-checkpoints.json"
            for attempt in range(1, 4):
                self.assertTrue(module.reserve_attempt(path, "2026-11-02", attempt, f"revision-{attempt}"))
                state = json.loads(path.read_text())
                self.assertEqual(attempt, state["Attempts"])
                state["CompletedSteps"] = ["completed"]
                state["IncludeDayBefore"] = True
                path.write_text(json.dumps(state))
            self.assertFalse(module.reserve_attempt(path, "2026-11-02", 100, "new-revision"))
            state = json.loads(path.read_text())
            self.assertEqual(3, state["Attempts"])
            self.assertEqual(["completed"], state["CompletedSteps"])
            self.assertTrue(state["IncludeDayBefore"])
            self.assertTrue(module.reserve_attempt(path, "2026-11-03", 15, "new-revision"))
            self.assertEqual(1, json.loads(path.read_text())["Attempts"])
            self.assertEqual([], json.loads(path.read_text())["CompletedSteps"])

    def test_legacy_checkpoint_keeps_completed_steps_and_starts_attempt_count(self):
        with tempfile.TemporaryDirectory(dir=Path(__file__).parent) as directory:
            path = Path(directory) / "daily-checkpoints.json"
            path.write_text(json.dumps({"FormatVersion": 1, "Date": "2026-11-02", "Count": 15,
                                       "Revision": "old", "CompletedSteps": ["done"],
                                       "IncludeDayBefore": True}))
            self.assertTrue(module.reserve_attempt(path, "2026-11-02", 4, "new"))
            state = json.loads(path.read_text())
            self.assertEqual(1, state["Attempts"])
            self.assertEqual(["done"], state["CompletedSteps"])

    def test_invalid_checkpoint_does_not_reset_attempt_budget(self):
        with tempfile.TemporaryDirectory(dir=Path(__file__).parent) as directory:
            path = Path(directory) / "daily-checkpoints.json"
            for content in ["not-json", '{"FormatVersion":2}',
                           '{"FormatVersion":1,"CompletedSteps":[],"Attempts":-1}']:
                path.write_text(content)
                with self.assertRaises(ValueError):
                    module.reserve_attempt(path, "2026-11-02", 15, "new")

    def test_missing_checkpoint_cannot_reset_github_rerun_attempt_counter(self):
        with tempfile.TemporaryDirectory(dir=Path(__file__).parent) as directory:
            path = Path(directory) / "daily-checkpoints.json"
            self.assertTrue(module.reserve_attempt(path, "2026-11-02", 15, "new",
                                                   previous_run_attempts=2))
            self.assertEqual(3, json.loads(path.read_text())["Attempts"])
            path.unlink()
            self.assertFalse(module.reserve_attempt(path, "2026-11-02", 4, "newer",
                                                    previous_run_attempts=3))
            self.assertEqual(3, json.loads(path.read_text())["Attempts"])

    def test_rerun_counter_is_a_floor_not_added_to_restored_attempt_count(self):
        with tempfile.TemporaryDirectory(dir=Path(__file__).parent) as directory:
            path = Path(directory) / "daily-checkpoints.json"
            self.assertTrue(module.reserve_attempt(path, "2026-11-02", 15, "old"))
            self.assertTrue(module.reserve_attempt(path, "2026-11-02", 15, "new",
                                                   previous_run_attempts=1))
            self.assertEqual(2, json.loads(path.read_text())["Attempts"])

    def test_incomplete_or_other_workflow_checkpoint_is_not_restored(self):
        artifact = self.artifact(name="daily-checkpoint-2026-11-02-15")

        for status, path in [("in_progress", ".github/workflows/daily-run.yml"),
                             ("completed", ".github/workflows/other.yml")]:
            with self.subTest(status=status, path=path):
                def request(url, token):
                    if "/actions/artifacts?" in url:
                        return {"artifacts": [artifact]}
                    return {"status": status, "path": path}

                self.assertEqual(("", ""), module.latest_checkpoint(
                    "2026-11-02", 15, "owner/repo", "test-token",
                    "https://api.github.com", request))

    def test_workflow_rerun_restores_its_own_previous_attempt_checkpoint(self):
        def request(url, token):
            if "/actions/artifacts?" in url:
                return {"artifacts": [self.artifact(name="daily-checkpoint-2026-11-02")]}
            return {"status": "in_progress", "path": ".github/workflows/daily-run.yml"}

        self.assertEqual(("123", "daily-checkpoint-2026-11-02"), module.latest_checkpoint(
            "2026-11-02", 15, "owner/repo", "test-token", "https://api.github.com", request,
            current_run_id="123"))

    def test_github_failure_does_not_silently_allow_another_run(self):
        def request(url, token):
            raise HTTPError(url, 403, "Forbidden", {}, None)

        with self.assertRaises(HTTPError):
            module.plan_run(
                "schedule", "7 7 * * *", "", datetime.fromisoformat("2026-11-02T07:07:00+00:00"),
                "owner/repo", "test-token", "https://api.github.com", request,
            )

    def test_entrypoint_writes_run_decision_and_frozen_target_to_github_output(self):
        for already_succeeded, count in [(False, 1), (False, 15), (True, 15)]:
            with self.subTest(already_succeeded=already_succeeded, count=count), tempfile.TemporaryDirectory(
                    dir=Path(__file__).parent) as temp:
                output = Path(temp) / "github-output"
                artifacts = [self.artifact(name="daily-success-2026-10-31")] if already_succeeded else []
                responses = [{"artifacts": artifacts}]
                if already_succeeded:
                    responses.append({"status": "completed", "conclusion": "success",
                                      "path": ".github/workflows/daily-run.yml"})
                else:
                    responses.append({"artifacts": []})
                environment = {
                    "EVENT_NAME": "workflow_dispatch", "EVENT_SCHEDULE": "",
                    "TARGET_DATE": "2026-10-31", "GITHUB_REPOSITORY": "owner/repo",
                    "TICKET_COUNT": str(count),
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
                self.assertEqual(
                    f"run={decision}\ntarget_date=2026-10-31\n"
                    "checkpoint_run_id=\ncheckpoint_artifact_name=\n",
                    output.read_text())

    def test_entrypoint_rejects_count_outside_one_to_fifteen_before_checkpoint_lookup(self):
        for count in ["0", "-1", "16", "100"]:
            with self.subTest(count=count), tempfile.TemporaryDirectory(
                    dir=Path(__file__).parent) as temp:
                output = Path(temp) / "github-output"
                environment = {
                    "EVENT_NAME": "workflow_dispatch", "EVENT_SCHEDULE": "",
                    "TARGET_DATE": "2026-10-31", "GITHUB_REPOSITORY": "owner/repo",
                    "TICKET_COUNT": count,
                    "GITHUB_TOKEN": "test-token", "GITHUB_API_URL": "https://api.github.com",
                    "GITHUB_OUTPUT": str(output),
                }
                with patch.dict(os.environ, environment), patch(
                        "urllib.request.urlopen",
                        return_value=io.BytesIO(b'{"artifacts": []}')
                ) as request, redirect_stdout(io.StringIO()), self.assertRaisesRegex(
                        ValueError, "between 1 and 15"):
                    runpy.run_path(str(Path(__file__).parents[1] / "Deployment" / "daily_schedule.py"),
                                   run_name="__main__")
                self.assertEqual(1, request.call_count)
                self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()

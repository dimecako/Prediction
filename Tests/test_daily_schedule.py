import importlib.util
import unittest
from datetime import datetime, timezone
from pathlib import Path

spec = importlib.util.spec_from_file_location(
    "daily_schedule", Path(__file__).parents[1] / "Deployment" / "daily_schedule.py"
)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class DailyScheduleTests(unittest.TestCase):
    def check(self, timestamp, active):
        now = datetime.fromisoformat(timestamp).replace(tzinfo=timezone.utc)
        self.assertTrue(module.should_run("schedule", active, now))
        other = "0 7 * * *" if active == "0 6 * * *" else "0 6 * * *"
        self.assertFalse(module.should_run("schedule", other, now))

    def test_winter_with_hours_of_delay(self):
        self.check("2026-11-01T13:42:00", "0 7 * * *")

    def test_summer_with_hours_of_delay(self):
        self.check("2026-07-15T16:10:00", "0 6 * * *")

    def test_spring_transition_day_even_before_transition(self):
        self.check("2026-03-29T00:10:00", "0 6 * * *")

    def test_autumn_transition_day_even_before_transition(self):
        self.check("2026-10-25T00:10:00", "0 7 * * *")

    def test_manual_always_runs(self):
        self.assertTrue(module.should_run("workflow_dispatch", "", datetime.now(timezone.utc)))

    def test_unknown_event_or_cron_does_not_run(self):
        now = datetime.now(timezone.utc)
        self.assertFalse(module.should_run("push", "", now))
        self.assertFalse(module.should_run("schedule", "0 8 * * *", now))


if __name__ == "__main__":
    unittest.main()

"""Select the UTC cron occurrence for 08:00 Europe/Skopje, even if GitHub queues it."""
import os
from datetime import datetime, time, timezone
from zoneinfo import ZoneInfo


def should_run(event_name, schedule, now):
    if event_name == "workflow_dispatch":
        return True
    if event_name != "schedule":
        return False
    local = now.astimezone(ZoneInfo("Europe/Skopje"))
    planned = datetime.combine(local.date(), time(8), ZoneInfo("Europe/Skopje"))
    expected = f"0 {planned.astimezone(timezone.utc).hour} * * *"
    return schedule == expected


if __name__ == "__main__":
    run = should_run(
        os.environ.get("EVENT_NAME", ""),
        os.environ.get("EVENT_SCHEDULE", ""),
        datetime.now(timezone.utc),
    )
    print(f"[SCHEDULE] {'Run' if run else 'Skip inactive UTC occurrence'}")
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
        output.write(f"run={'true' if run else 'false'}\n")

"""Gate daily attempts on the Skopje schedule and a successful run for the date."""
import json
import os
import sys
from datetime import date, datetime, timezone
from pathlib import Path
from urllib.parse import urlencode
from urllib.request import Request, urlopen
from zoneinfo import ZoneInfo


LOCAL_ZONE = ZoneInfo("Europe/Skopje")
SCHEDULE_UTC_HOURS = range(5, 9)
LOCAL_ATTEMPT_HOURS = range(7, 10)


def scheduled_local_hour(schedule, now):
    if schedule not in {f"0 {hour} * * *" for hour in SCHEDULE_UTC_HOURS}:
        return None
    utc_hour = int(schedule.split()[1])
    scheduled_utc = datetime.combine(
        now.astimezone(timezone.utc).date(),
        datetime.min.time(),
        tzinfo=timezone.utc,
    ).replace(hour=utc_hour, minute=0)
    return scheduled_utc.astimezone(LOCAL_ZONE).hour


def should_run(event_name, schedule, now):
    if event_name == "workflow_dispatch":
        return True
    if event_name != "schedule":
        return False
    local_hour = scheduled_local_hour(schedule, now)
    return local_hour in LOCAL_ATTEMPT_HOURS


def target_date(value, now):
    if not value:
        return now.astimezone(LOCAL_ZONE).date().isoformat()
    parsed = date.fromisoformat(value)
    if parsed.isoformat() != value:
        raise ValueError("Target date must use yyyy-MM-dd.")
    return value


def github_json(url, token):
    request = Request(url, headers={
        "Authorization": f"Bearer {token}",
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
    })
    with urlopen(request, timeout=30) as response:
        return json.load(response)


def has_successful_run(target, repository, token, api_url, request_json=github_json):
    name = f"daily-success-{target}"
    base = f"{api_url.rstrip('/')}/repos/{repository}"
    page = 1
    while True:
        query = urlencode({"name": name, "per_page": 100, "page": page})
        artifacts = request_json(f"{base}/actions/artifacts?{query}", token)["artifacts"]
        for artifact in artifacts:
            if artifact["name"] != name or artifact["expired"]:
                continue
            run_id = artifact["workflow_run"]["id"]
            run = request_json(f"{base}/actions/runs/{run_id}", token)
            if (run["status"] == "completed" and run["conclusion"] == "success"
                    and run["path"] == ".github/workflows/daily-run.yml"):
                return True
        if len(artifacts) < 100:
            return False
        page += 1


def latest_checkpoint(target, count, repository, token, api_url, request_json=github_json,
                      current_run_id=""):
    name = f"daily-checkpoint-{target}"
    reservation_prefix = f"daily-attempt-{target}-"
    base = f"{api_url.rstrip('/')}/repos/{repository}"
    page = 1
    while True:
        # Include legacy count-suffixed checkpoints: changing count must not reset attempts.
        query = urlencode({"per_page": 100, "page": page})
        artifacts = request_json(f"{base}/actions/artifacts?{query}", token)["artifacts"]
        for artifact in artifacts:
            if (artifact["name"] != name and not artifact["name"].startswith(f"{name}-")
                    and not artifact["name"].startswith(reservation_prefix)) or artifact["expired"]:
                continue
            run_id = artifact["workflow_run"]["id"]
            run = request_json(f"{base}/actions/runs/{run_id}", token)
            # A workflow rerun retains its run ID and artifacts while becoming in_progress again.
            if ((run["status"] == "completed" or str(run_id) == current_run_id)
                    and run["path"] == ".github/workflows/daily-run.yml"):
                return str(run_id), artifact["name"]
        if len(artifacts) < 100:
            return "", ""
        page += 1


def plan_run(event_name, schedule, requested_date, now, repository, token, api_url,
             request_json=github_json):
    target = target_date(requested_date, now)
    if not should_run(event_name, schedule, now):
        return False, target, "Skip inactive UTC occurrence"
    if has_successful_run(target, repository, token, api_url, request_json):
        return False, target, f"Skip {target}: a successful daily run already exists"
    return True, target, f"Run {target}: no successful daily run recorded"


def reserve_attempt(path, target, count, revision, previous_run_attempts=0):
    path = Path(path)
    if path.exists():
        checkpoint = json.loads(path.read_text(encoding="utf-8"))
        if (checkpoint.get("FormatVersion") != 1
                or not isinstance(checkpoint.get("CompletedSteps"), list)
                or type(checkpoint.get("Attempts", 0)) is not int
                or checkpoint.get("Attempts", 0) < 0):
            raise ValueError("Unsupported or invalid daily-run checkpoint.")
        if checkpoint.get("Date") != target:
            checkpoint = None
    else:
        checkpoint = None
    if checkpoint is None:
        checkpoint = {"FormatVersion": 1, "Date": target, "Count": count,
                      "Revision": revision, "IncludeDayBefore": False, "CompletedSteps": []}
    # GitHub keeps this counter even if a rerun's checkpoint artifact was lost.
    attempts = max(checkpoint.get("Attempts", 0), previous_run_attempts)
    available = attempts < 3
    checkpoint["Attempts"] = attempts + (1 if available else 0)
    path.parent.mkdir(parents=True, exist_ok=True)
    staging = path.with_suffix(".pending")
    try:
        staging.write_text(json.dumps(checkpoint, indent=2), encoding="utf-8")
        staging.replace(path)
    finally:
        staging.unlink(missing_ok=True)
    return available


if __name__ == "__main__":
    if sys.argv[1:] == ["--reserve"]:
        if not reserve_attempt(os.environ["DAILY_CHECKPOINT_FILE"], os.environ["TARGET_DATE"],
                               int(os.environ.get("TICKET_COUNT", "15")),
                               os.environ["DAILY_CHECKPOINT_REVISION"],
                               max(0, int(os.environ.get("GITHUB_RUN_ATTEMPT", "1")) - 1)):
            print("[SCHEDULE] Maximum three attempts for this date already reserved.")
            sys.exit(1)
        print("[SCHEDULE] Reserved one of three daily attempts.")
        sys.exit(0)
    run, target, message = plan_run(
        os.environ.get("EVENT_NAME", ""),
        os.environ.get("EVENT_SCHEDULE", ""),
        os.environ.get("TARGET_DATE", ""),
        datetime.now(timezone.utc),
        os.environ["GITHUB_REPOSITORY"],
        os.environ["GITHUB_TOKEN"],
        os.environ["GITHUB_API_URL"],
    )
    print(f"[SCHEDULE] {message}")
    checkpoint_run_id = ""
    checkpoint_artifact_name = ""
    if run:
        count = int(os.environ.get("TICKET_COUNT", "15"))
        if count < 1 or count > 15:
            raise ValueError("Ticket count must be between 1 and 15.")
        checkpoint_run_id, checkpoint_artifact_name = latest_checkpoint(
            target, count, os.environ["GITHUB_REPOSITORY"], os.environ["GITHUB_TOKEN"],
            os.environ["GITHUB_API_URL"], current_run_id=os.environ.get("GITHUB_RUN_ID", ""))
        if checkpoint_run_id:
            print(f"[SCHEDULE] Restore completed steps from run {checkpoint_run_id}.")
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
        output.write(f"run={'true' if run else 'false'}\n")
        output.write(f"target_date={target}\n")
        output.write(f"checkpoint_run_id={checkpoint_run_id}\n")
        output.write(f"checkpoint_artifact_name={checkpoint_artifact_name}\n")

"""Gate daily attempts on the Skopje schedule and a successful run for the date."""
import json
import os
from datetime import date, datetime, time, timezone
from urllib.parse import urlencode
from urllib.request import Request, urlopen
from zoneinfo import ZoneInfo


LOCAL_ZONE = ZoneInfo("Europe/Skopje")


def should_run(event_name, schedule, now):
    if event_name == "workflow_dispatch":
        return True
    if event_name != "schedule":
        return False
    local = now.astimezone(LOCAL_ZONE)
    for hour in range(8, 12):
        planned = datetime.combine(local.date(), time(hour), LOCAL_ZONE)
        expected = f"0 {planned.astimezone(timezone.utc).hour} * * *"
        if schedule == expected:
            return True
    return False


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


def latest_checkpoint(target, count, repository, token, api_url, request_json=github_json):
    name = f"daily-checkpoint-{target}-{count}"
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
            if run["status"] == "completed" and run["path"] == ".github/workflows/daily-run.yml":
                return str(run_id), name
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


if __name__ == "__main__":
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
        if count < 1 or count > 100:
            raise ValueError("Ticket count must be between 1 and 100.")
        checkpoint_run_id, checkpoint_artifact_name = latest_checkpoint(
            target, count, os.environ["GITHUB_REPOSITORY"], os.environ["GITHUB_TOKEN"],
            os.environ["GITHUB_API_URL"])
        if checkpoint_run_id:
            print(f"[SCHEDULE] Restore completed steps from run {checkpoint_run_id}.")
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
        output.write(f"run={'true' if run else 'false'}\n")
        output.write(f"target_date={target}\n")
        output.write(f"checkpoint_run_id={checkpoint_run_id}\n")
        output.write(f"checkpoint_artifact_name={checkpoint_artifact_name}\n")

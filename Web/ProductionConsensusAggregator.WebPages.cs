using Prediction.Web;

public partial class Program
{
    private static string GetHomePage(Prediction.Web.UiLanguage language, string? antiforgeryToken)
    {
        string today = DatabaseReportEndpoints.Today().ToString("yyyy-MM-dd");
        string body = $$"""
            <section class="hero">
              <div class="hero-content">
                <div class="eyebrow">{{Prediction.Web.DatabaseReportPages.Text(language, "Matchday, at a glance")}}</div>
                <h2>{{Prediction.Web.DatabaseReportPages.Text(language, "Make every fixture")}} <span>{{Prediction.Web.DatabaseReportPages.Text(language, "count.")}}</span></h2>
                <p>{{Prediction.Web.DatabaseReportPages.Text(language, "Explore stored predictions, compare ticket simulations, and track results in one place.")}}</p>
                <form class="date-form" method="get">
                  <input type="hidden" name="lang" value="{{Prediction.Web.DatabaseReportPages.LanguageCode(language)}}">
                  <label for="analysisDate">{{Prediction.Web.DatabaseReportPages.Text(language, "Choose a match date")}}
                    <input id="analysisDate" name="date" type="date" value="{{today}}" required>
                  </label>
                  <button type="submit" formaction="/predictions">{{Prediction.Web.DatabaseReportPages.Text(language, "View predictions")}} <span aria-hidden="true">→</span></button>
                  <button class="button-secondary" type="submit" formaction="/analyse">{{Prediction.Web.DatabaseReportPages.Text(language, "Collect predictions")}}</button>
                </form>
              </div>
            </section>
            <section class="panel" aria-labelledby="daily-run-heading">
              <div class="eyebrow">{{Prediction.Web.DatabaseReportPages.Text(language, "Daily automation")}}</div>
              <h2 id="daily-run-heading">{{Prediction.Web.DatabaseReportPages.Text(language, "Daily run status")}}</h2>
              <p id="daily-run-status" role="status" aria-live="polite">{{Prediction.Web.DatabaseReportPages.Text(language, "Checking today's scheduled run…")}}</p>
              <p><a id="daily-run-link" hidden target="_blank" rel="noopener noreferrer">{{Prediction.Web.DatabaseReportPages.Text(language, "View GitHub run")}}</a></p>
              <form id="daily-run-form" method="post" action="/automation/daily-run" hidden>
                <input type="hidden" name="__RequestVerificationToken" value="{{DatabaseReportPages.Encode(antiforgeryToken)}}">
                <button type="submit">{{Prediction.Web.DatabaseReportPages.Text(language, "Start daily-run manually")}}</button>
              </form>
            </section>
            <section class="grid" aria-label="{{Prediction.Web.DatabaseReportPages.Text(language, "Explore analytics")}}">
              <a class="card feature" href="/tickets?lang={{Prediction.Web.DatabaseReportPages.LanguageCode(language)}}">
                <span class="icon" aria-hidden="true">▤</span>
                <h2>{{Prediction.Web.DatabaseReportPages.Text(language, "Ticket simulations")}}</h2>
                <p>{{Prediction.Web.DatabaseReportPages.Text(language, "Browse saved ticket variants, frozen picks, and settlement status.")}}</p>
              </a>
              <a class="card feature" href="/performance?lang={{Prediction.Web.DatabaseReportPages.LanguageCode(language)}}">
                <span class="icon" aria-hidden="true">↗</span>
                <h2>{{Prediction.Web.DatabaseReportPages.Text(language, "Performance & ROI")}}</h2>
                <p>{{Prediction.Web.DatabaseReportPages.Text(language, "Review simulated returns across dates and ticket strategies.")}}</p>
              </a>
              <a class="card feature" href="/leagues?lang={{Prediction.Web.DatabaseReportPages.LanguageCode(language)}}">
                <span class="icon" aria-hidden="true">◎</span>
                <h2>{{Prediction.Web.DatabaseReportPages.Text(language, "League insights")}}</h2>
                <p>{{Prediction.Web.DatabaseReportPages.Text(language, "Compare historical consensus accuracy by league and market.")}}</p>
              </a>
            </section>
            <p class="footer-note">{{Prediction.Web.DatabaseReportPages.Text(language, "Daily automation targets 08:00 Europe/Skopje; GitHub scheduling may be delayed.")}}
            {{Prediction.Web.DatabaseReportPages.Text(language, "Stored predictions and tickets are available without Excel files.")}}
            <a href="https://github.com/dimecako/Prediction/actions">{{Prediction.Web.DatabaseReportPages.Text(language, "View run history and errors →")}}</a></p>
            <script>
              (() => {
                const status = document.getElementById("daily-run-status");
                const form = document.getElementById("daily-run-form");
                const link = document.getElementById("daily-run-link");
                const mk = {{(Prediction.Web.DatabaseReportPages.LanguageCode(language) == "mk").ToString().ToLowerInvariant()}};
                const labels = mk
                  ? {checking:"Се проверува денешното стартување…",unconfigured:"Интеграцијата со GitHub не е наместена на Render.",waiting:"Дневното автоматско стартување сѐ уште се очекува.",queued:"Дневниот run е во редица.",running:"Дневниот run е во тек.",succeeded:"Дневниот run заврши успешно.",failed:"Дневниот run не успеа.",missed:"Автоматскиот run не е забележан по последниот термин.",limit:"Неуспешен run; потрошени се сите 3 обиди.",unknown:"Не може да се утврди статусот на дневниот run.",retry:"Обиди се повторно со daily-run",sent:"Барањето за рачно стартување е испратено.",error:"Не успеа рачното стартување. Провери ја GitHub Actions историјата."}
                  : {checking:"Checking today's scheduled run…",unconfigured:"GitHub integration is not configured on Render.",waiting:"Today's automatic run is still expected.",queued:"Daily run is queued.",running:"Daily run is in progress.",succeeded:"Daily run completed successfully.",failed:"Daily run failed.",missed:"No automatic run was recorded after the final schedule window.",limit:"Run failed; all 3 attempts have been used.",unknown:"The daily-run status could not be determined.",retry:"Retry daily-run",sent:"Manual run request submitted.",error:"Manual start failed. Check GitHub Actions run history."};
                const refresh = async () => {
                  try {
                    const response = await fetch("/automation/daily-run/status", {cache:"no-store"});
                    if (!response.ok) throw new Error("Status request failed");
                    const data = await response.json();
                    status.textContent = data.configured ? (labels[data.state] || labels.unknown) : labels.unconfigured;
                    form.hidden = !data.canRetry;
                    form.querySelector("button").textContent = labels.retry;
                    link.hidden = !data.runUrl;
                    if (data.runUrl) link.href = data.runUrl;
                  } catch {
                    status.textContent = labels.unknown;
                    form.hidden = true;
                  }
                };
                form.addEventListener("submit", async event => {
                  event.preventDefault();
                  const button = form.querySelector("button");
                  button.disabled = true;
                  status.textContent = labels.checking;
                  try {
                    const response = await fetch(form.action, {method:"POST",body:new FormData(form)});
                    if (!response.ok) throw new Error("Dispatch failed");
                    status.textContent = labels.sent;
                    form.hidden = true;
                    window.setTimeout(refresh, 5000);
                  } catch {
                    status.textContent = labels.error;
                    button.disabled = false;
                    await refresh();
                  }
                });
                refresh();
                window.setInterval(refresh, 60000);
              })();
            </script>
            """;

        return Prediction.Web.DatabaseReportPages.Page("Matchday dashboard", body, showNotice: false, language: language);
    }

    private static string GetResultPage(string date, string result, Prediction.Web.UiLanguage language)
    {
        string body = $$"""
            <section class="panel result-panel">
              <div class="eyebrow">{{DatabaseReportPages.Text(language, "Analysis complete")}}</div>
              <h2>{{DatabaseReportPages.Text(language, "Prediction date:")}} {{DatabaseReportPages.Encode(date)}}</h2>
              <p><a class="button button-secondary" href="/?lang={{DatabaseReportPages.LanguageCode(language)}}">{{DatabaseReportPages.Text(language, "← Back to dashboard")}}</a></p>
              <pre>{{DatabaseReportPages.Encode(result)}}</pre>
            </section>
            """;
        return DatabaseReportPages.Page("Prediction results", body, showNotice: false, language: language);
    }

    private static string GetErrorPage(string error, Prediction.Web.UiLanguage language)
    {
        string body = $$"""
            <section class="panel error-panel">
              <div class="eyebrow">{{DatabaseReportPages.Text(language, "Something went wrong")}}</div>
              <h2>{{DatabaseReportPages.Text(language, "Analysis could not be completed")}}</h2>
              <pre>{{DatabaseReportPages.Encode(error)}}</pre>
              <a class="button button-secondary" href="/?lang={{DatabaseReportPages.LanguageCode(language)}}">{{DatabaseReportPages.Text(language, "← Back to dashboard")}}</a>
            </section>
            """;
        return DatabaseReportPages.Page("Analysis error", body, showNotice: false, language: language);
    }
}

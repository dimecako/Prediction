using Prediction.Web;

public partial class Program
{
    private static string GetHomePage(Prediction.Web.UiLanguage language)
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

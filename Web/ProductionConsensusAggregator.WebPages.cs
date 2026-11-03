using Prediction.Web;

public partial class Program
{
    private static string GetHomePage()
    {
        string today = DatabaseReportEndpoints.Today().ToString("yyyy-MM-dd");
        string body = $$"""
            <section class="hero">
              <div class="hero-content">
                <div class="eyebrow">Matchday, at a glance</div>
                <h2>Make every fixture <span>count.</span></h2>
                <p>Explore stored predictions, compare ticket simulations, and track results in one place.</p>
                <form class="date-form" method="get">
                  <label for="analysisDate">Choose a match date
                    <input id="analysisDate" name="date" type="date" value="{{today}}" required>
                  </label>
                  <button type="submit" formaction="/predictions">View predictions <span aria-hidden="true">→</span></button>
                  <button class="button-secondary" type="submit" formaction="/analyse">Collect predictions</button>
                </form>
              </div>
            </section>
            <section class="grid" aria-label="Explore analytics">
              <a class="card feature" href="/tickets">
                <span class="icon" aria-hidden="true">▤</span>
                <h2>Ticket simulations</h2>
                <p>Browse saved ticket variants, frozen picks, and settlement status.</p>
              </a>
              <a class="card feature" href="/performance">
                <span class="icon" aria-hidden="true">↗</span>
                <h2>Performance &amp; ROI</h2>
                <p>Review simulated returns across dates and ticket strategies.</p>
              </a>
              <a class="card feature" href="/leagues">
                <span class="icon" aria-hidden="true">◎</span>
                <h2>League insights</h2>
                <p>Compare historical consensus accuracy by league and market.</p>
              </a>
            </section>
            <p class="footer-note">Daily automation targets 08:00 Europe/Skopje; GitHub scheduling may be delayed.
            Stored predictions and tickets are available without Excel files.
            <a href="https://github.com/dimecako/Prediction/actions">View run history and errors →</a></p>
            """;

        return DatabaseReportPages.Page("Matchday dashboard", body, showNotice: false);
    }

    private static string GetResultPage(string date, string result)
    {
        string body = $$"""
            <section class="panel result-panel">
              <div class="eyebrow">Analysis complete</div>
              <h2>Prediction date: {{DatabaseReportPages.Encode(date)}}</h2>
              <p><a class="button button-secondary" href="/">← Back to dashboard</a></p>
              <pre>{{DatabaseReportPages.Encode(result)}}</pre>
            </section>
            """;
        return DatabaseReportPages.Page("Prediction results", body, showNotice: false);
    }

    private static string GetErrorPage(string error)
    {
        string body = $$"""
            <section class="panel error-panel">
              <div class="eyebrow">Something went wrong</div>
              <h2>Analysis could not be completed</h2>
              <pre>{{DatabaseReportPages.Encode(error)}}</pre>
              <a class="button button-secondary" href="/">← Back to dashboard</a>
            </section>
            """;
        return DatabaseReportPages.Page("Analysis error", body, showNotice: false);
    }
}

using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Prediction.Services;
using Prediction.Web;

namespace Prediction.Web;

public static class DatabaseReportEndpoints
{
    public static void MapDatabaseReports(this WebApplication app)
    {
        app.MapGet("/predictions", async Task<Results<ContentHttpResult, ProblemHttpResult>> (
            HttpContext context, IDatabaseReportService reports, CancellationToken ct) =>
        {
            if (!TryDate(context, "date", Today(), out var date) || !TryPage(context, out int page))
                return InvalidQuery(context);
            var language = DatabaseReportPages.ParseLanguage(context.Request.Query["lang"]);
            return TypedResults.Content(await reports.PredictionsAsync(date, page, language, ct), "text/html; charset=utf-8");
        }).WithSummary("Read stored predictions by date");

        app.MapGet("/tickets", async Task<Results<ContentHttpResult, ProblemHttpResult>> (
            HttpContext context, IDatabaseReportService reports, CancellationToken ct) =>
        {
            if (!TryRange(context, out var from, out var to) || !TryPage(context, out int page))
                return InvalidQuery(context);
            var language = DatabaseReportPages.ParseLanguage(context.Request.Query["lang"]);
            return TypedResults.Content(await reports.TicketsAsync(from, to, context.Request.Query["mode"], page, language, ct), "text/html; charset=utf-8");
        }).WithSummary("Browse stored ticket simulations");

        app.MapGet("/tickets/{id:long}", async Task<Results<ContentHttpResult, NotFound>> (
            long id, HttpContext context, IDatabaseReportService reports, CancellationToken ct) =>
        {
            var language = DatabaseReportPages.ParseLanguage(context.Request.Query["lang"]);
            string? html = await reports.TicketAsync(id, language, ct);
            return html == null ? TypedResults.NotFound() : TypedResults.Content(html, "text/html; charset=utf-8");
        }).WithSummary("Read frozen selections, results and pending reasons for a ticket");

        app.MapGet("/performance", async Task<Results<ContentHttpResult, ProblemHttpResult>> (
            HttpContext context, IDatabaseReportService reports, CancellationToken ct) =>
        {
            if (!TryRange(context, out var from, out var to))
                return InvalidQuery(context);
            var language = DatabaseReportPages.ParseLanguage(context.Request.Query["lang"]);
            return TypedResults.Content(await reports.PerformanceAsync(from, to, language, ct), "text/html; charset=utf-8");
        }).WithSummary("Read simulated ticket profit and ROI");

        app.MapGet("/leagues", async Task<Results<ContentHttpResult, ProblemHttpResult>> (
            HttpContext context, IDatabaseReportService reports, CancellationToken ct) =>
        {
            if (!TryRange(context, out var from, out var to))
                return InvalidQuery(context);
            var language = DatabaseReportPages.ParseLanguage(context.Request.Query["lang"]);
            return TypedResults.Content(await reports.LeaguesAsync(from, to, language, ct), "text/html; charset=utf-8");
        }).WithSummary("Read strict historical consensus accuracy by league and market");
    }

    public static DateOnly Today() => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Europe/Skopje").DateTime);

    private static bool TryDate(HttpContext context, string key, DateOnly fallback, out DateOnly date)
    {
        string? value = context.Request.Query[key];
        date = fallback;
        return value == null || DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static bool TryRange(HttpContext context, out DateOnly from, out DateOnly to)
    {
        bool fromValid = TryDate(context, "from", Today().AddMonths(-1), out from);
        bool toValid = TryDate(context, "to", Today(), out to);
        return fromValid && toValid && from <= to;
    }

    private static bool TryPage(HttpContext context, out int page)
    {
        string? value = context.Request.Query["page"];
        page = 1;
        return value == null || (int.TryParse(value, out page) && page is >= 1 and <= 1000000);
    }

    private static ProblemHttpResult InvalidQuery(HttpContext context)
    {
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseReports")
            .LogWarning("Invalid report date range or page number.");
        return TypedResults.Problem(statusCode: 400, title: "Invalid report query",
            detail: "Use yyyy-MM-dd dates, from <= to, and a positive page number.");
    }
}

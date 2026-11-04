using System.Globalization;
using Microsoft.AspNetCore.Antiforgery;

namespace Prediction.Web;

public enum ResultCollectionStatus { Succeeded, Failed, Busy }

public static class ResultCollectionEndpoints
{
    public static void MapResultCollection(this WebApplication app,
        Func<DateOnly, CancellationToken, Task<ResultCollectionStatus>> collect)
    {
        app.MapGet("/results/collect", (HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!TryDate(context.Request.Query["date"], out var date))
                return InvalidDate(context);
            var language = DatabaseReportPages.ParseLanguage(context.Request.Query["lang"]);
            var tokens = antiforgery.GetAndStoreTokens(context);
            string body = $"""
                <p>{DatabaseReportPages.Text(language, "Collect verified FT/HT results only. Predictions and tickets will not be generated.")}</p>
                <form method="post" action="/results/collect">
                <input type="hidden" name="{DatabaseReportPages.Encode(tokens.FormFieldName)}" value="{DatabaseReportPages.Encode(tokens.RequestToken)}">
                <input type="hidden" name="lang" value="{DatabaseReportPages.LanguageCode(language)}">
                <label>{DatabaseReportPages.Text(language, "Date")} <input type="date" name="date" value="{date:yyyy-MM-dd}" required></label>
                <button>{DatabaseReportPages.Text(language, "Collect results only")}</button></form>
                """;
            return (IResult)TypedResults.Content(DatabaseReportPages.Page("Collect results only", body, language: language),
                "text/html; charset=utf-8");
        }).WithSummary("Confirm results-only collection for a match date");

        app.MapPost("/results/collect", async (HttpContext context, IAntiforgery antiforgery, CancellationToken ct) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                app.Logger.LogWarning("Rejected results collection request with invalid antiforgery token.");
                return (IResult)TypedResults.Problem(statusCode: 400, title: "Invalid collection request",
                    detail: "Reload the collection form and try again.");
            }
            var form = await context.Request.ReadFormAsync(ct);
            if (!TryDate(form["date"], out var date))
                return InvalidDate(context);
            var language = DatabaseReportPages.ParseLanguage(form["lang"]);
            var status = await collect(date, ct);
            if (status == ResultCollectionStatus.Busy)
                return TypedResults.Problem(statusCode: 409, title: "Collection already running",
                    detail: "Another collection is running. Wait for it to finish before trying again.");
            bool succeeded = status == ResultCollectionStatus.Succeeded;
            if (!succeeded)
                app.Logger.LogError("Results-only collection failed for {Date}; inspect provider and persistence logs.", date);
            string message = succeeded ? "Results collection completed." :
                "Results collection failed or was partial. Check server logs; confirmed results were preserved.";
            string body = $"<p>{DatabaseReportPages.Text(language, message)}</p>" +
                $"<p><a href='/predictions?date={date:yyyy-MM-dd}&amp;lang={DatabaseReportPages.LanguageCode(language)}'>{DatabaseReportPages.Text(language, "View predictions")}</a></p>";
            return TypedResults.Content(DatabaseReportPages.Page("Collect results only", body, language: language),
                "text/html; charset=utf-8", statusCode: succeeded ? 200 : 502);
        }).WithSummary("Collect and safely persist verified results only; never generate predictions or tickets");
    }

    private static bool TryDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) &&
        date <= DatabaseReportEndpoints.Today();

    private static IResult InvalidDate(HttpContext context)
    {
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ResultCollection")
            .LogWarning("Rejected invalid or future results collection date.");
        return TypedResults.Problem(statusCode: 400, title: "Invalid collection date",
            detail: "Use a yyyy-MM-dd date that is not in the future.");
    }
}

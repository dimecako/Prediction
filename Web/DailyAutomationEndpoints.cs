using Microsoft.AspNetCore.Antiforgery;
using Prediction.Services;

namespace Prediction.Web;

public static class DailyAutomationEndpoints
{
    public static void MapDailyAutomation(this WebApplication app)
    {
        app.MapGet("/automation/daily-run/status",
            async (IGitHubDailyWorkflowClient client, CancellationToken ct) =>
                TypedResults.Ok(await client.GetStatusAsync(DatabaseReportEndpoints.Today(), ct)))
            .WithSummary("Get today's scheduled daily-run status");

        app.MapPost("/automation/daily-run", async Task<IResult> (
            HttpContext context, IAntiforgery antiforgery,
            IGitHubDailyWorkflowClient client, CancellationToken ct) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                app.Logger.LogWarning("Rejected manual daily-run request with invalid antiforgery token.");
                return TypedResults.Problem(statusCode: 400, title: "Invalid request",
                    detail: "Reload the dashboard and try again.");
            }

            var date = DatabaseReportEndpoints.Today();
            var status = await client.GetStatusAsync(date, ct);
            if (!status.Configured)
                return TypedResults.Problem(statusCode: 503, title: "GitHub integration is not configured",
                    detail: "Set GitHub__Token on the Render web service.");
            if (!status.CanRetry)
                return TypedResults.Problem(statusCode: 409, title: "Daily run cannot be started",
                    detail: "A run is already active, has succeeded, is not due, or today's retry limit is exhausted.");

            try
            {
                await client.DispatchAsync(date, ct);
                return TypedResults.Accepted("/automation/daily-run/status");
            }
            catch (HttpRequestException ex)
            {
                app.Logger.LogWarning(ex,
                    "GitHub rejected the manual daily-run dispatch with HTTP {StatusCode}.",
                    ex.StatusCode);
                return TypedResults.Problem(statusCode: 502, title: "GitHub dispatch failed",
                    detail: ex.StatusCode == null
                        ? "GitHub could not be reached. Wait briefly and try again."
                        : $"GitHub rejected workflow dispatch with HTTP {(int)ex.StatusCode.Value}. " +
                          "Check the GitHub token Actions permission and GitHub Actions history.");
            }
        }).WithSummary("Manually dispatch today's daily-run workflow when it failed or was missed");
    }
}

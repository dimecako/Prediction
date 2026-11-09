using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Prediction.Entities;
using Prediction.Models;

namespace Prediction.Services;

public sealed class FlashscoreResultProvider(Action<string>? log = null) : ITargetedMatchResultProvider
{
    private static readonly Regex ScorePattern = new(
        @"^(?<home>\d+)\s*-\s*(?<away>\d+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    public string Name => "Flashscore";

    public Task<IReadOnlyList<ExternalMatchResult>> GetResultsAsync(
        DateOnly date, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Flashscore collection requires targeted pending fixtures.");

    public async Task<IReadOnlyList<ExternalMatchResult>> GetResultsAsync(
        DateOnly date, IReadOnlyCollection<Prediction.Entities.Match> matches,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (matches.Count == 0)
            return [];

        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Skopje");
        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
        int daysBack = today.DayNumber - date.DayNumber;
        if (daysBack < 1)
        {
            log?.Invoke($"[FLASHSCORE] {date:yyyy-MM-dd} is not a past date; skipped.");
            return [];
        }

        using var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        IBrowserContext? context = null;
        try
        {
            context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                Locale = "en-US",
                TimezoneId = "Europe/Skopje"
            });
            var page = await context.NewPageAsync();
            await page.GotoAsync($"https://www.flashscore.info/day-{daysBack}/",
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
            await page.Locator("button[data-testid='wcl-dayPickerButton']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30_000 });
            string? dateLabel = await page.Locator("button[data-testid='wcl-dayPickerButton']")
                .GetAttributeAsync("aria-label");
            if (!DateOnly.TryParseExact(dateLabel, "dddd, MMMM d, yyyy",
                CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out var pageDate) ||
                pageDate != date)
                throw new JsonException($"Flashscore date page mismatch: requested {date:yyyy-MM-dd}, received '{dateLabel}'.");

            var rows = await page.Locator(".event__match").EvaluateAllAsync<string[][]>(
                """
                rows => rows.map(row => [
                    row.id || "",
                    row.querySelector(".event__homeParticipant")?.innerText.trim() || "",
                    row.querySelector(".event__awayParticipant")?.innerText.trim() || "",
                    row.querySelector(".event__stage")?.innerText.trim() || "",
                    row.querySelector(".event__score--home")?.innerText.trim() || "",
                    row.querySelector(".event__score--away")?.innerText.trim() || "",
                    row.querySelector("a.eventRowLink")?.href || ""
                ])
                """);
            if (rows.Any(row => row.Length != 7 || string.IsNullOrWhiteSpace(row[0]) ||
                    string.IsNullOrWhiteSpace(row[1]) || string.IsNullOrWhiteSpace(row[2])))
                throw new JsonException("Flashscore result list contains an invalid fixture row.");

            var results = new List<ExternalMatchResult>();
            var errors = new List<string>();
            foreach (var target in matches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var matching = rows.Where(row => row.Length == 7 &&
                    TeamNameMatcher.SameFixture(target.HomeTeam, target.AwayTeam, row[1], row[2])).ToList();
                if (matching.Count == 0)
                    continue;
                if (matching.Count != 1)
                {
                    errors.Add($"{target.HomeTeam} vs {target.AwayTeam}: multiple Flashscore fixtures matched.");
                    continue;
                }

                var row = matching[0];
                if (!row[3].Equals("Finished", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!int.TryParse(row[4], NumberStyles.None, CultureInfo.InvariantCulture, out int listHome) ||
                    !int.TryParse(row[5], NumberStyles.None, CultureInfo.InvariantCulture, out int listAway))
                {
                    errors.Add($"{row[0]} {row[1]} vs {row[2]}: invalid result-list FT.");
                    continue;
                }

                try
                {
                    await page.GotoAsync(row[6],
                        new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
                    await page.Locator(".duelParticipant__score .detailScore__wrapper")
                        .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30_000 });
                    var detail = await page.Locator("body").EvaluateAsync<string[]>(
                        """
                        body => [
                            body.querySelector(".duelParticipant__home")?.innerText.trim() || "",
                            body.querySelector(".duelParticipant__away")?.innerText.trim() || "",
                            body.querySelector(".duelParticipant__startTime")?.innerText.trim() || "",
                            body.querySelector(".detailScore__statusWithPenalties")?.innerText.trim() || "",
                            body.querySelector(".detailScore__wrapper")?.innerText.trim() || "",
                            ...[...body.querySelectorAll("[class*='wcl-cell']")].map(cell => cell.innerText.trim())
                        ]
                        """);
                    if (detail.Length < 5)
                        throw new JsonException("Flashscore match detail is incomplete.");
                    var result = ParseVerifiedMatch(date, row[0], detail[0], detail[1],
                        detail[2], detail[3], detail[4], detail.Skip(5).ToArray());
                    if (result.HomeGoals != listHome || result.AwayGoals != listAway)
                        throw new JsonException("Flashscore result-list and match-detail FT scores conflict.");
                    if (!TeamNameMatcher.SameFixture(target.HomeTeam, target.AwayTeam,
                            result.HomeTeam, result.AwayTeam))
                        throw new JsonException("Flashscore match-detail teams do not match the requested fixture.");
                    results.Add(result);
                }
                catch (Exception ex) when (ex is PlaywrightException or JsonException or
                    OperationCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    errors.Add($"{row[0]} {row[1]} vs {row[2]}: {ex.Message}");
                }
            }

            if (errors.Count > 0)
                throw new PartialMatchResultException(results, errors);
            return results;
        }
        finally
        {
            try
            {
                if (context != null)
                    await context.CloseAsync();
            }
            finally
            {
                await browser.CloseAsync();
            }
        }
    }

    public static ExternalMatchResult ParseVerifiedMatch(
        DateOnly requestedDate, string externalId, string home, string away, string kickoff,
        string status, string finalScore, IReadOnlyList<string> periodCells)
    {
        if (string.IsNullOrWhiteSpace(externalId) || string.IsNullOrWhiteSpace(home) ||
            string.IsNullOrWhiteSpace(away) ||
            !DateOnly.TryParseExact(kickoff.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(),
                "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var matchDate) ||
            matchDate != requestedDate)
            throw new JsonException("Flashscore match identity or date is invalid.");

        if (!status.Equals("Finished", StringComparison.OrdinalIgnoreCase) ||
            periodCells.Any(cell => cell.Contains("extra time", StringComparison.OrdinalIgnoreCase) ||
                                    cell.Contains("penalt", StringComparison.OrdinalIgnoreCase)))
            throw new JsonException("Flashscore does not verify a regulation-time FT for this fixture.");

        var score = ParseScore(finalScore);
        int? halfHome = null, halfAway = null;
        for (int i = 0; i < periodCells.Count; i++)
        {
            if (!periodCells[i].Equals("1ST HALF", StringComparison.OrdinalIgnoreCase))
                continue;
            if (halfHome.HasValue || i + 1 >= periodCells.Count)
                throw new JsonException("Flashscore has ambiguous or incomplete first-half score data.");
            var half = ParseScore(periodCells[i + 1]);
            if (half.Home > score.Home || half.Away > score.Away)
                throw new JsonException("Flashscore first-half score exceeds the final score.");
            halfHome = half.Home;
            halfAway = half.Away;
        }

        return new ExternalMatchResult
        {
            ExternalId = $"flashscore:{externalId}",
            HomeTeam = home,
            AwayTeam = away,
            HomeGoals = score.Home,
            AwayGoals = score.Away,
            HalfTimeHomeGoals = halfHome,
            HalfTimeAwayGoals = halfAway,
            IsRegulationTimeVerified = true,
            Source = "Flashscore"
        };
    }

    private static (int Home, int Away) ParseScore(string value)
    {
        var match = ScorePattern.Match(value.Trim());
        if (!match.Success ||
            !int.TryParse(match.Groups["home"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int home) ||
            !int.TryParse(match.Groups["away"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int away))
            throw new JsonException($"Flashscore has an invalid score '{value}'.");
        return (home, away);
    }
}

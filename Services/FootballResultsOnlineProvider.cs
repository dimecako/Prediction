using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Prediction.Models;

namespace Prediction.Services;

public sealed class FootballResultsOnlineProvider : IMatchResultProvider
{
    private const string Url =
        "https://www.footballresultsonline.co.uk/Home.aspx";

    private readonly HttpClient httpClient;

    public string Name => "FootballResultsOnline";

    public FootballResultsOnlineProvider(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public async Task<IReadOnlyList<ExternalMatchResult>> GetResultsAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        using var request =
            new HttpRequestMessage(HttpMethod.Get, Url);

        request.Headers.UserAgent.ParseAdd("Mozilla/5.0");

        using var response =
            await httpClient.SendAsync(
                request,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        string html =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        string? tableHtml =
            ExtractScrollerContent(html);

        if (string.IsNullOrWhiteSpace(tableHtml))
        {
            Console.WriteLine(
                "[FRO] scrollercontent not found.");

            return Array.Empty<ExternalMatchResult>();
        }

        tableHtml = WebUtility.HtmlDecode(tableHtml);

        var document = new HtmlDocument();
        document.LoadHtml(tableHtml);

        var rows =
            document.DocumentNode.SelectNodes("//tr");

        if (rows == null)
            return Array.Empty<ExternalMatchResult>();

        var results =
            new List<ExternalMatchResult>();

        string requestedDate =
            date.ToString(
                "dd/MM/yyyy",
                CultureInfo.InvariantCulture);

        foreach (var row in rows)
        {
            var cells =
                row.SelectNodes("./td");

            if (cells == null || cells.Count != 6)
                continue;

            string dateText =
                Clean(cells[0].InnerText);

            if (!string.Equals(
                    dateText,
                    requestedDate,
                    StringComparison.Ordinal))
            {
                continue;
            }

            string homeTeam =
                Clean(cells[1].InnerText);

            string homeGoalsText =
                Clean(cells[2].InnerText);

            string separator =
                Clean(cells[3].InnerText);

            string awayGoalsText =
                Clean(cells[4].InnerText);

            string awayTeam =
                Clean(cells[5].InnerText);

            if (!string.Equals(
                    separator,
                    "v",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(homeTeam) ||
                string.IsNullOrWhiteSpace(awayTeam))
            {
                continue;
            }

            if (!int.TryParse(
                    homeGoalsText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int homeGoals))
            {
                continue;
            }

            if (!int.TryParse(
                    awayGoalsText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int awayGoals))
            {
                continue;
            }

            results.Add(
                new ExternalMatchResult
                {
                    ExternalId =
                        $"fro:{date:yyyyMMdd}:" +
                        $"{MatchIdentity.NormalizeTeam(homeTeam)}:" +
                        $"{MatchIdentity.NormalizeTeam(awayTeam)}",

                    HomeTeam = homeTeam,
                    AwayTeam = awayTeam,

                    KickoffUtc = null,

                    HomeGoals = homeGoals,
                    AwayGoals = awayGoals,

                    Source = Name
                });
        }

        return results;
    }

    private static string? ExtractScrollerContent(
        string html)
    {
        var match = Regex.Match(
            html,
            @"var\s+scrollercontent\s*=\s*'(?<content>.*?)';",
            RegexOptions.Singleline |
            RegexOptions.IgnoreCase);

        if (!match.Success)
            return null;

        return match.Groups["content"].Value;
    }

    private static string Clean(string value)
    {
        return WebUtility.HtmlDecode(value)
            .Replace('\u00A0', ' ')
            .Trim();
    }
}
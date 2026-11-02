using System.Globalization;
using System.Net;
using System.Text;
using Prediction.Entities;
using Prediction.Services;

namespace Prediction.Web;

public static class DatabaseReportPages
{
    public static string Encode(object? value) =>
        WebUtility.HtmlEncode(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");

    public static string Page(string title, string body) => $$"""
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8">
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <title>{{Encode(title)}} - Football AI</title>
        <style>
        body{font-family:Arial,sans-serif;background:#0f172a;color:#e2e8f0;margin:0}
        main{max-width:1400px;margin:auto;padding:24px}
        h1,a{color:#38bdf8}a{display:inline-block;margin:6px}
        nav,form,.notice{padding:16px;background:#1e293b;border-radius:10px;margin-bottom:20px}
        input,select,button{padding:10px;margin:4px;border-radius:6px}
        .scroll{overflow:auto}table{border-collapse:collapse;width:100%;background:#1e293b}
        th,td{text-align:left;padding:10px;border-bottom:1px solid #475569;vertical-align:top}
        th{color:#38bdf8}.Won{color:#86efac}.Lost{color:#fca5a5}.Pending{color:#fde68a}
        details{max-width:600px}small{color:#94a3b8}
        </style></head><body><main>
        <h1>{{Encode(title)}}</h1>
        <nav><a href="/">Home</a><a href="/predictions">Predictions</a>
        <a href="/tickets">Tickets</a><a href="/performance">Performance / ROI</a>
        <a href="/leagues">League statistics</a></nav>
        <p class="notice">SIMULATIONS ONLY: 1 unit for the whole ticket, not actual payments.
        Source probabilities are not calibrated ticket probabilities.
        All pages read stored database records; opening a page does not collect or generate tickets.</p>
        {{body}}
        </main></body></html>
        """;

    public static string Predictions(DateOnly date, IReadOnlyList<Match> matches, int page, int total)
    {
        var body = new StringBuilder($"""
            <form method="get"><label>Date <input type="date" name="date" value="{date:yyyy-MM-dd}" required></label>
            <button>Show stored predictions</button></form>
            <p>{total} stored fixtures for {date:yyyy-MM-dd}. Latest snapshot per source; capture times are UTC.</p>
            """);
        if (matches.Count == 0)
            body.Append("<p>No stored predictions for this date.</p>");
        else
        {
            body.Append("<div class='scroll'><table><tr><th>Fixture / league</th><th>Source</th><th>1X2</th><th>Score</th><th>BTTS</th><th>Goals</th><th>Source Prob.</th><th>Captured UTC</th><th>Final / half-time</th></tr>");
            foreach (var match in matches)
            {
                foreach (var snapshot in CalibrationAnalyzer.LatestPerSource(match).OrderBy(x => x.Source))
                {
                    var result = match.Result;
                    body.Append("<tr>");
                    Cell(body, $"{match.HomeTeam} - {match.AwayTeam} / {TicketPerformanceAnalyzer.LeagueName(match.League)}");
                    Cell(body, snapshot.Source);
                    Cell(body, snapshot.PredictedResult);
                    Cell(body, snapshot.PredictedScore);
                    Cell(body, snapshot.Btts);
                    Cell(body, snapshot.Goals);
                    Cell(body, snapshot.Confidence.HasValue ? snapshot.Confidence.Value.ToString("P1", CultureInfo.InvariantCulture) : "n/a");
                    Cell(body, snapshot.CapturedAtUtc.ToString("u"));
                    Cell(body, Score(result));
                    body.Append("</tr>");
                }
                if (match.PredictionSnapshots.Count == 0)
                {
                    body.Append("<tr>");
                    Cell(body, $"{match.HomeTeam} - {match.AwayTeam}");
                    body.Append("<td colspan='8'>No stored snapshots.</td></tr>");
                }
            }
            body.Append("</table></div>");
        }
        body.Append(Pagination($"/predictions?date={date:yyyy-MM-dd}", page, total, 100));
        return Page("Stored predictions", body.ToString());
    }

    public static string TicketTable(IEnumerable<TicketSettlement> settlements)
    {
        var body = new StringBuilder("<div class='scroll'><table><tr><th>Ticket</th><th>Date</th><th>Mode</th><th>Picks</th><th>Status</th><th>Odds</th><th>Stake</th><th>Profit</th><th>Created UTC</th></tr>");
        foreach (var row in settlements)
        {
            body.Append($"<tr><td><a href='/tickets/{row.Ticket.Id}'>#{row.Ticket.Id}</a></td>");
            Cell(body, row.Ticket.MatchDate);
            Cell(body, row.Ticket.Mode);
            Cell(body, row.Selections.Count);
            body.Append($"<td class='{row.Status}'>{row.Status}</td>");
            Cell(body, Money(row.CombinedOdds));
            Cell(body, Money(row.Ticket.Stake));
            Cell(body, Money(row.Profit));
            Cell(body, row.Ticket.CreatedAtUtc.ToString("u"));
            body.Append("</tr>");
        }
        return body.Append("</table></div>").ToString();
    }

    public static string Ticket(TicketSettlement settlement, IReadOnlyDictionary<long, MatchResult> results)
    {
        var body = new StringBuilder(TicketTable([settlement]));
        body.Append("<h2>Frozen selections and current results</h2><div class='scroll'><table><tr><th>#</th><th>Fixture / league</th><th>Market / pick</th><th>Odds</th><th>Status</th><th>FT / HT</th><th>Details</th></tr>");
        foreach (var leg in settlement.Selections.OrderBy(x => x.Selection.Position))
        {
            var selection = leg.Selection;
            results.TryGetValue(selection.MatchId, out var result);
            string status = leg.Correct == true ? "Won" : leg.Correct == false ? "Lost" : "Pending";
            body.Append("<tr>");
            Cell(body, selection.Position);
            Cell(body, $"{selection.Fixture} / {TicketPerformanceAnalyzer.LeagueName(selection.League)}");
            Cell(body, $"{selection.Market} / {selection.Pick}");
            Cell(body, Money(selection.Odds));
            body.Append($"<td class='{status}'>{status}</td>");
            Cell(body, Score(result));
            body.Append("<td><details><summary>Evidence / pending reason</summary>");
            body.Append(Encode(selection.Evidence));
            body.Append("<p>").Append(Encode(TicketPerformanceAnalyzer.PendingReason(leg, result))).Append("</p>");
            if (!TicketLedger.IsDailyMode(settlement.Ticket.Mode))
                body.Append($"<p>Historical accuracy: {Encode(selection.Accuracy.ToString("P1"))}; Wilson lower bound: {Encode(selection.Safe.ToString("P1"))}; sample: {selection.Sample}</p>");
            body.Append($"<p>Kickoff UTC: {Encode(selection.KickoffUtc.ToString("u"))}</p></details></td></tr>");
        }
        return Page($"Ticket #{settlement.Ticket.Id}", body.Append("</table></div>").ToString());
    }

    public static string RangeForm(DateOnly from, DateOnly to) => $"""
        <form method="get">
        <label>From <input type="date" name="from" value="{from:yyyy-MM-dd}" required></label>
        <label>To <input type="date" name="to" value="{to:yyyy-MM-dd}" required></label>
        <button>Show stored records</button></form>
        """;

    public static string Pagination(string url, int page, int total, int size) =>
        $"<p>Page {page} | {total} records " +
        (page > 1 ? $"<a href='{Encode(url)}&amp;page={page - 1}'>Previous</a>" : "") +
        ((long)page * size < total ? $"<a href='{Encode(url)}&amp;page={page + 1}'>Next</a>" : "") + "</p>";

    public static void Cell(StringBuilder body, object? value) =>
        body.Append("<td>").Append(Encode(value ?? "n/a")).Append("</td>");

    public static string Money(decimal? value) => value?.ToString("F2", CultureInfo.InvariantCulture) ?? "n/a";

    private static string Score(MatchResult? result) => result == null ? "Pending" :
        $"FT {result.HomeGoals}-{result.AwayGoals} / HT " +
        (result.HalfTimeHomeGoals.HasValue && result.HalfTimeAwayGoals.HasValue
            ? $"{result.HalfTimeHomeGoals}-{result.HalfTimeAwayGoals}" : "n/a");
}

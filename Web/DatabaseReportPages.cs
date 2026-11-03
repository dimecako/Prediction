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

    public static string Page(string title, string body, bool showNotice = true) => $$$"""
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8">
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <meta name="theme-color" content="#080d18">
        <title>{{{Encode(title)}}} - Football AI</title>
        <style>
        :root{color-scheme:dark;--bg:#080d18;--surface:#101827;--surface-raised:#151f30;--line:#243248;--text:#edf3fb;--muted:#93a2b8;--accent:#b9f36a;--blue:#8eb7ff;--red:#ff8f91;--amber:#f7ce76;--radius:18px}
        *{box-sizing:border-box}
        body{margin:0;min-height:100vh;background:radial-gradient(ellipse at 82% -10%,#1e3448 0,transparent 36%),radial-gradient(ellipse at 0% 40%,#142333 0,transparent 36%),var(--bg);color:var(--text);font:15px/1.6 Inter,ui-sans-serif,system-ui,-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif}
        a{color:var(--accent);text-decoration:none;transition:color .18s ease,background .18s ease,border-color .18s ease}
        a:hover{color:#d7ff9f}
        main{width:min(1440px,100%);margin:0 auto;padding:28px clamp(18px,4vw,56px) 56px}
        .topbar{display:flex;align-items:center;justify-content:space-between;gap:24px;padding:4px 0 24px;border-bottom:1px solid var(--line);margin-bottom:38px}
        .brand{display:inline-flex;align-items:center;gap:11px;color:var(--text);font-size:16px;font-weight:750;letter-spacing:-.03em}
        .brand-mark{display:grid;place-items:center;width:36px;height:36px;border-radius:12px;background:var(--accent);color:#15200a;font-size:19px}
        .brand small{display:block;color:var(--muted);font-size:10px;font-weight:650;letter-spacing:.13em;text-transform:uppercase;line-height:1.2}
        nav{display:flex;flex-wrap:wrap;justify-content:flex-end;gap:6px}
        nav a{padding:8px 12px;border:1px solid transparent;border-radius:999px;color:#b1bfd1;font-size:13px;font-weight:600}
        nav a:hover{border-color:var(--line);background:#ffffff08;color:var(--text)}
        h1,h2,h3,p{margin-top:0}
        h1{font-size:clamp(28px,4vw,42px);line-height:1.12;letter-spacing:-.045em;margin-bottom:10px}
        h2{font-size:21px;letter-spacing:-.025em}
        h3{font-size:16px;letter-spacing:-.015em}
        .page-heading{margin-bottom:24px}
        .eyebrow{color:var(--accent);font-size:11px;font-weight:750;letter-spacing:.16em;text-transform:uppercase}
        .muted,small{color:var(--muted)}
        .notice{display:flex;gap:13px;align-items:flex-start;padding:15px 18px;margin:0 0 24px;border:1px solid #304038;border-radius:14px;background:#17231b;color:#c5d0bf;font-size:13px}
        .notice strong{color:var(--accent);white-space:nowrap}
        .panel,.card{border:1px solid var(--line);border-radius:var(--radius);background:linear-gradient(145deg,#141f30f2,#0e1725f2);box-shadow:0 18px 60px #00000020}
        .result-panel{padding:clamp(20px,4vw,36px)}
        form{display:flex;flex-wrap:wrap;align-items:end;gap:12px;padding:18px;margin:0 0 22px;border:1px solid var(--line);border-radius:16px;background:#101827c9}
        label{display:grid;gap:6px;color:#adbacb;font-size:12px;font-weight:650}
        input,select,button{font:inherit}
        input,select{min-height:44px;padding:9px 12px;border:1px solid #34445b;border-radius:11px;background:#0b1220;color:var(--text);accent-color:var(--accent)}
        input:focus,select:focus{outline:2px solid #b9f36a88;outline-offset:1px;border-color:var(--accent)}
        button,.button{display:inline-flex;align-items:center;justify-content:center;gap:9px;min-height:44px;padding:10px 16px;border:1px solid var(--accent);border-radius:11px;background:var(--accent);color:#14200a;font-size:13px;font-weight:800;cursor:pointer;transition:transform .18s ease,filter .18s ease}
        button:hover,.button:hover{transform:translateY(-1px);filter:brightness(1.05);color:#14200a}
        .button-secondary{border-color:#34445b;background:#172235;color:var(--text)}
        .button-secondary:hover{background:#1d2b40;color:#fff}
        .scroll{overflow:auto;border:1px solid var(--line);border-radius:16px;background:#0e1725}
        table{width:100%;border-collapse:collapse;min-width:760px}
        th,td{text-align:left;padding:13px 15px;border-bottom:1px solid #202d40;vertical-align:top}
        th{position:sticky;top:0;background:#141f30;color:#aebdd0;font-size:11px;font-weight:750;letter-spacing:.09em;text-transform:uppercase;white-space:nowrap}
        td{color:#dce5f1;font-size:13px}
        tbody tr:last-child td{border-bottom:0}
        tbody tr:hover{background:#ffffff05}
        td a{margin:0}
        .Won,.Lost,.Pending{display:inline-flex;align-items:center;padding:3px 9px;border-radius:999px;font-size:11px;font-weight:750}
        .Won{background:#b9f36a18;color:var(--accent)}.Lost{background:#ff8f9118;color:var(--red)}.Pending{background:#f7ce7618;color:var(--amber)}
        details{max-width:600px}summary{color:var(--blue);cursor:pointer;font-weight:650}
        pre{overflow:auto;padding:22px;border:1px solid var(--line);border-radius:16px;background:#0b1220;color:#dce8f8;font:13px/1.75 ui-monospace,SFMono-Regular,Consolas,monospace;white-space:pre-wrap}
        .pagination{display:flex;align-items:center;gap:10px;margin-top:18px}
        .pagination a{padding:7px 12px;border:1px solid var(--line);border-radius:9px;background:var(--surface)}
        .hero{position:relative;overflow:hidden;padding:clamp(26px,5vw,56px);margin-bottom:25px;border:1px solid #344650;border-radius:26px;background:radial-gradient(ellipse at 95% 15%,#b9f36a24,transparent 34%),linear-gradient(120deg,#172536,#101a27 60%,#13231d)}
        .hero:after{content:"";position:absolute;right:8%;top:18%;width:190px;height:190px;border:1px solid #b9f36a25;border-radius:50%;box-shadow:0 0 0 24px #b9f36a09,0 0 0 48px #b9f36a06;pointer-events:none}
        .hero-content{position:relative;z-index:1;max-width:700px}
        .hero h1{max-width:660px;font-size:clamp(36px,6.5vw,70px)}
        .hero h1 span{color:var(--accent)}
        .hero p{max-width:560px;color:#aab8ca;font-size:16px}
        .hero .date-form{max-width:590px;margin-top:26px;padding:10px;border-color:#536048;background:#0b1220cc}
        .hero .date-form label{flex:1;min-width:180px;padding-left:8px}
        .hero .date-form button{white-space:nowrap}
        .grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:15px}
        .feature{display:flex;flex-direction:column;min-height:174px;padding:20px;color:var(--text)}
        .feature:hover{transform:translateY(-2px);border-color:#647a58;color:var(--text)}
        .feature .icon{display:grid;place-items:center;width:38px;height:38px;margin-bottom:15px;border:1px solid #3b4b3b;border-radius:12px;background:#b9f36a13;color:var(--accent);font-size:17px}
        .feature h2{margin-bottom:6px;font-size:16px}
        .feature p{margin:0;color:var(--muted);font-size:13px}
        .footer-note{margin-top:24px;color:var(--muted);font-size:12px}
        .error-panel{padding:clamp(22px,4vw,36px);border-color:#633f47;background:#21171d}
        @media(max-width:760px){main{padding-top:18px}.topbar{align-items:flex-start;flex-direction:column;margin-bottom:25px;padding-bottom:17px}nav{justify-content:flex-start;margin-left:-7px}.grid{grid-template-columns:1fr}.feature{min-height:130px}.hero:after{right:-70px;top:45%}form{align-items:stretch;flex-direction:column}form label,form input,form select,form button{width:100%}.notice{flex-direction:column;gap:5px}}
        @media(prefers-reduced-motion:reduce){*,*::before,*::after{scroll-behavior:auto!important;transition:none!important}}
        </style></head><body><main>
        <header class="topbar"><a class="brand" href="/"><span class="brand-mark">⚽</span><span>FOOTBALL AI<small>Match intelligence</small></span></a>
        <nav aria-label="Main navigation"><a href="/">Overview</a><a href="/predictions">Predictions</a>
        <a href="/tickets">Tickets</a><a href="/performance">Performance</a>
        <a href="/leagues">Leagues</a></nav></header>
        {{{(showNotice ? "<p class=\"notice\"><strong>SIMULATION ONLY</strong><span>1 unit is staked on the whole ticket; these are not actual payments. Source probabilities are not calibrated ticket probabilities. Pages display stored records only and do not collect or generate tickets.</span></p>" : "")}}}
        <section class="page-heading"><div class="eyebrow">Football analytics</div><h1>{{{Encode(title)}}}</h1></section>
        {{{body}}}
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
        $"<div class='pagination'><span class='muted'>Page {page} · {total} records</span> " +
        (page > 1 ? $"<a href='{Encode(url)}&amp;page={page - 1}'>← Previous</a>" : "") +
        ((long)page * size < total ? $"<a href='{Encode(url)}&amp;page={page + 1}'>Next →</a>" : "") + "</div>";

    public static void Cell(StringBuilder body, object? value) =>
        body.Append("<td>").Append(Encode(value ?? "n/a")).Append("</td>");

    public static string Money(decimal? value) => value?.ToString("F2", CultureInfo.InvariantCulture) ?? "n/a";

    private static string Score(MatchResult? result) => result == null ? "Pending" :
        $"FT {result.HomeGoals}-{result.AwayGoals} / HT " +
        (result.HalfTimeHomeGoals.HasValue && result.HalfTimeAwayGoals.HasValue
            ? $"{result.HalfTimeHomeGoals}-{result.HalfTimeAwayGoals}" : "n/a");
}

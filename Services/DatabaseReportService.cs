using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Prediction.Data;
using Prediction.Entities;
using Prediction.Web;

namespace Prediction.Services;

public interface IDatabaseReportService
{
    Task<string> PredictionsAsync(DateOnly date, int page, UiLanguage language, CancellationToken ct,
        string sort = "result-agreement", bool ascending = false);
    Task<string> TicketsAsync(DateOnly from, DateOnly to, string? mode, int page, UiLanguage language, CancellationToken ct);
    Task<string?> TicketAsync(long id, UiLanguage language, CancellationToken ct);
    Task<string> PerformanceAsync(DateOnly from, DateOnly to, UiLanguage language, CancellationToken ct);
    Task<string> LeaguesAsync(DateOnly from, DateOnly to, UiLanguage language, CancellationToken ct);
}

public sealed class DatabaseReportService(FootballDbContext db) : IDatabaseReportService
{
    public static IReadOnlyList<string> TicketModes { get; } =
    [
        "value", "safe", "ml", "ml-safe", "ml-league-safe", "daily", "daily-league",
        "daily-league-safe", "1-3p3+", "1-3p3+-safe",
        "fixed-1", "fixed-2", "ai-fixed-1", "ai-fixed-2"
    ];

    public async Task<string> PredictionsAsync(DateOnly date, int page, UiLanguage language, CancellationToken ct,
        string sort = "result-agreement", bool ascending = false)
    {
        var matches = await db.Matches.AsNoTracking().Where(x => x.MatchDate == date)
            .Include(x => x.PredictionSnapshots).Include(x => x.Result)
            .AsSplitQuery().ToListAsync(ct);
        var fixtures = PredictionConsensus.Order(PredictionReportFixtures.Consolidate(matches), sort, ascending);
        return DatabaseReportPages.Predictions(date, fixtures.Skip((page - 1) * 100).Take(100)
            .Select(x => x.Match).ToList(), page, fixtures.Count, language, sort, ascending);
    }

    public async Task<string> TicketsAsync(DateOnly from, DateOnly to, string? mode, int page, UiLanguage language, CancellationToken ct)
    {
        var query = db.SimulatedTickets.AsNoTracking().Where(x => x.MatchDate >= from && x.MatchDate <= to);
        if (!string.IsNullOrWhiteSpace(mode))
            query = query.Where(x => x.Mode == mode);
        int total = await query.CountAsync(ct);
        var storedModes = await db.SimulatedTickets.AsNoTracking().Select(x => x.Mode).Distinct().ToListAsync(ct);
        var modes = TicketModes.Concat(storedModes).Distinct().Order().ToArray();
        var tickets = await query.OrderByDescending(x => x.MatchDate).ThenByDescending(x => x.Id)
            .Skip((page - 1) * 50).Take(50).Include(x => x.Selections).AsSplitQuery().ToListAsync(ct);
        var results = await ResultsAsync(tickets, ct);
        string options = $"<option value=''>{DatabaseReportPages.Text(language, "All modes")}</option>" + string.Concat(modes.Select(m =>
            $"<option value='{DatabaseReportPages.Encode(m)}'{(m == mode ? " selected" : "")}>{DatabaseReportPages.Encode(m)}</option>"));
        string form = DatabaseReportPages.RangeForm(from, to, language).Replace("<button>", $"<select name='mode'>{options}</select><button>");
        string body = form + (tickets.Count == 0 ? $"<p>{DatabaseReportPages.Text(language, "No saved tickets in this range.")}</p>" :
            DatabaseReportPages.TicketTable(tickets.Select(t => TicketPerformanceAnalyzer.Settle(t, results)), language));
        body += DatabaseReportPages.Pagination($"/tickets?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&mode={Uri.EscapeDataString(mode ?? "")}", page, total, 50, language);
        return DatabaseReportPages.Page("Stored tickets", body, language: language);
    }

    public async Task<string?> TicketAsync(long id, UiLanguage language, CancellationToken ct)
    {
        var ticket = await db.SimulatedTickets.AsNoTracking().Include(x => x.Selections).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (ticket == null)
            return null;
        var results = await ResultsAsync([ticket], ct);
        return DatabaseReportPages.Ticket(TicketPerformanceAnalyzer.Settle(ticket, results), results, language);
    }

    public async Task<string> PerformanceAsync(DateOnly from, DateOnly to, UiLanguage language, CancellationToken ct)
    {
        var tickets = await db.SimulatedTickets.AsNoTracking()
            .Where(x => x.MatchDate >= from && x.MatchDate <= to).Include(x => x.Selections).AsSplitQuery().ToListAsync(ct);
        var results = await ResultsAsync(tickets, ct);
        var settlements = tickets.Select(t => TicketPerformanceAnalyzer.Settle(t, results)).ToList();
        var body = new StringBuilder(DatabaseReportPages.RangeForm(from, to, language));
        body.Append("<p>").Append(DatabaseReportPages.Text(language, "Current stored results applied to frozen selections. Pending stakes are excluded from settled ROI. Overlapping tickets are not independent evidence.")).Append("</p>");
        body.Append($"<div class='scroll'><table><tr><th>{DatabaseReportPages.Text(language, "Mode")}</th><th>{DatabaseReportPages.Text(language, "Tickets")}</th><th>{DatabaseReportPages.Text(language, "Won count")}</th><th>{DatabaseReportPages.Text(language, "Lost count")}</th><th>{DatabaseReportPages.Text(language, "Pending")}</th><th>{DatabaseReportPages.Text(language, "Settled stake")}</th><th>{DatabaseReportPages.Text(language, "Pending stake")}</th><th>{DatabaseReportPages.Text(language, "Profit")}</th><th>{DatabaseReportPages.Text(language, "ROI")}</th></tr>");
        Summary(DatabaseReportPages.Text(language, "ALL"), settlements);
        foreach (var mode in settlements.GroupBy(x => x.Ticket.Mode).OrderBy(x => x.Key))
            Summary(mode.Key, mode);
        return DatabaseReportPages.Page("Ticket performance", body.Append("</table></div>").ToString(), language: language);

        void Summary(string label, IEnumerable<TicketSettlement> rows)
        {
            var summary = TicketPerformanceAnalyzer.Summarize(rows);
            body.Append("<tr>");
            foreach (object value in new object[] { label, summary.Total, summary.Won, summary.Lost, summary.Pending,
                DatabaseReportPages.Money(summary.SettledStake, language), DatabaseReportPages.Money(summary.PendingStake, language),
                DatabaseReportPages.Money(summary.Profit, language), summary.Roi?.ToString("P1", CultureInfo.InvariantCulture) ?? DatabaseReportPages.Text(language, "n/a") })
                DatabaseReportPages.Cell(body, value);
            body.Append("</tr>");
        }
    }

    public async Task<string> LeaguesAsync(DateOnly from, DateOnly to, UiLanguage language, CancellationToken ct)
    {
        var matches = await db.Matches.AsNoTracking().Where(x => x.MatchDate >= from && x.MatchDate <= to && x.Result != null)
            .Include(x => x.Result).Include(x => x.PredictionSnapshots).AsSplitQuery().ToListAsync(ct);
        var rows = LeaguePerformanceAnalyzer.Analyze(matches);
        var body = new StringBuilder(DatabaseReportPages.RangeForm(from, to, language));
        body.Append("<p>").Append(DatabaseReportPages.Text(language, "Equal-vote consensus; only snapshots BEFORE 00:00 UTC on match date. Missing league labels remain unknown. Accuracy is not ROI. Samples under 30 are not reliable.")).Append("</p>");
        body.Append($"<div class='scroll'><table><tr><th>{DatabaseReportPages.Text(language, "League")}</th><th>{DatabaseReportPages.Text(language, "Market")}</th><th>{DatabaseReportPages.Text(language, "Correct")}</th><th>{DatabaseReportPages.Text(language, "Graded")}</th><th>{DatabaseReportPages.Text(language, "Accuracy")}</th><th>{DatabaseReportPages.Text(language, "Wilson lower bound")}</th><th>{DatabaseReportPages.Text(language, "Sample")}</th></tr>");
        foreach (var row in rows)
        {
            body.Append("<tr>");
            foreach (object value in new object[] { row.League, row.Market, row.Correct, row.Total,
                row.Accuracy.ToString("P1", CultureInfo.InvariantCulture), row.Safe.ToString("P1", CultureInfo.InvariantCulture),
                DatabaseReportPages.Text(language, row.Total < 30 ? "Small sample" : "30+ observations") })
                DatabaseReportPages.Cell(body, value);
            body.Append("</tr>");
        }
        if (rows.Count == 0)
            body.Append($"<tr><td colspan='7'>{DatabaseReportPages.Text(language, "No graded pre-date consensus picks in this range.")}</td></tr>");
        return DatabaseReportPages.Page("League statistics", body.Append("</table></div>").ToString(), language: language);
    }

    private Task<Dictionary<long, MatchResult>> ResultsAsync(IEnumerable<SimulatedTicket> tickets, CancellationToken ct)
    {
        var ids = tickets.SelectMany(t => t.Selections).Select(x => x.MatchId).Distinct().ToArray();
        return db.MatchResults.AsNoTracking().Where(x => ids.Contains(x.MatchId)).ToDictionaryAsync(x => x.MatchId, ct);
    }
}

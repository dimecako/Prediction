using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Prediction.Data;
using Prediction.Entities;
using Prediction.Web;

namespace Prediction.Services;

public interface IDatabaseReportService
{
    Task<string> PredictionsAsync(DateOnly date, int page, CancellationToken ct);
    Task<string> TicketsAsync(DateOnly from, DateOnly to, string? mode, int page, CancellationToken ct);
    Task<string?> TicketAsync(long id, CancellationToken ct);
    Task<string> PerformanceAsync(DateOnly from, DateOnly to, CancellationToken ct);
    Task<string> LeaguesAsync(DateOnly from, DateOnly to, CancellationToken ct);
}

public sealed class DatabaseReportService(FootballDbContext db) : IDatabaseReportService
{
    public async Task<string> PredictionsAsync(DateOnly date, int page, CancellationToken ct)
    {
        var query = db.Matches.AsNoTracking().Where(x => x.MatchDate == date);
        int total = await query.CountAsync(ct);
        var matches = await query.OrderBy(x => x.HomeTeam).ThenBy(x => x.Id)
            .Skip((page - 1) * 100).Take(100).Include(x => x.PredictionSnapshots).Include(x => x.Result)
            .AsSplitQuery().ToListAsync(ct);
        return DatabaseReportPages.Predictions(date, matches, page, total);
    }

    public async Task<string> TicketsAsync(DateOnly from, DateOnly to, string? mode, int page, CancellationToken ct)
    {
        var query = db.SimulatedTickets.AsNoTracking().Where(x => x.MatchDate >= from && x.MatchDate <= to);
        if (!string.IsNullOrWhiteSpace(mode))
            query = query.Where(x => x.Mode == mode);
        int total = await query.CountAsync(ct);
        var storedModes = await db.SimulatedTickets.AsNoTracking().Select(x => x.Mode).Distinct().ToListAsync(ct);
        var modes = new[] { "value", "safe", "ml", "ml-safe", "ml-league-safe", "daily", "daily-league", "1-3p3+", "1-3p3+-safe" }
            .Concat(storedModes).Distinct().Order().ToArray();
        var tickets = await query.OrderByDescending(x => x.MatchDate).ThenByDescending(x => x.Id)
            .Skip((page - 1) * 50).Take(50).Include(x => x.Selections).AsSplitQuery().ToListAsync(ct);
        var results = await ResultsAsync(tickets, ct);
        string options = "<option value=''>All modes</option>" + string.Concat(modes.Select(m =>
            $"<option value='{DatabaseReportPages.Encode(m)}'{(m == mode ? " selected" : "")}>{DatabaseReportPages.Encode(m)}</option>"));
        string form = DatabaseReportPages.RangeForm(from, to).Replace("<button>", $"<select name='mode'>{options}</select><button>");
        string body = form + (tickets.Count == 0 ? "<p>No saved tickets in this range.</p>" :
            DatabaseReportPages.TicketTable(tickets.Select(t => TicketPerformanceAnalyzer.Settle(t, results))));
        body += DatabaseReportPages.Pagination($"/tickets?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&mode={Uri.EscapeDataString(mode ?? "")}", page, total, 50);
        return DatabaseReportPages.Page("Stored tickets", body);
    }

    public async Task<string?> TicketAsync(long id, CancellationToken ct)
    {
        var ticket = await db.SimulatedTickets.AsNoTracking().Include(x => x.Selections).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (ticket == null)
            return null;
        var results = await ResultsAsync([ticket], ct);
        return DatabaseReportPages.Ticket(TicketPerformanceAnalyzer.Settle(ticket, results), results);
    }

    public async Task<string> PerformanceAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var tickets = await db.SimulatedTickets.AsNoTracking()
            .Where(x => x.MatchDate >= from && x.MatchDate <= to).Include(x => x.Selections).AsSplitQuery().ToListAsync(ct);
        var results = await ResultsAsync(tickets, ct);
        var settlements = tickets.Select(t => TicketPerformanceAnalyzer.Settle(t, results)).ToList();
        var body = new StringBuilder(DatabaseReportPages.RangeForm(from, to));
        body.Append("<p>Current stored results applied to frozen selections. Pending stakes are excluded from settled ROI. Overlapping tickets are not independent evidence.</p>");
        body.Append("<div class='scroll'><table><tr><th>Mode</th><th>Tickets</th><th>Won</th><th>Lost</th><th>Pending</th><th>Settled stake</th><th>Pending stake</th><th>Profit</th><th>ROI</th></tr>");
        Summary("ALL", settlements);
        foreach (var mode in settlements.GroupBy(x => x.Ticket.Mode).OrderBy(x => x.Key))
            Summary(mode.Key, mode);
        return DatabaseReportPages.Page("Ticket performance", body.Append("</table></div>").ToString());

        void Summary(string label, IEnumerable<TicketSettlement> rows)
        {
            var summary = TicketPerformanceAnalyzer.Summarize(rows);
            body.Append("<tr>");
            foreach (object value in new object[] { label, summary.Total, summary.Won, summary.Lost, summary.Pending,
                DatabaseReportPages.Money(summary.SettledStake), DatabaseReportPages.Money(summary.PendingStake),
                DatabaseReportPages.Money(summary.Profit), summary.Roi?.ToString("P1", CultureInfo.InvariantCulture) ?? "n/a" })
                DatabaseReportPages.Cell(body, value);
            body.Append("</tr>");
        }
    }

    public async Task<string> LeaguesAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var matches = await db.Matches.AsNoTracking().Where(x => x.MatchDate >= from && x.MatchDate <= to && x.Result != null)
            .Include(x => x.Result).Include(x => x.PredictionSnapshots).AsSplitQuery().ToListAsync(ct);
        var rows = LeaguePerformanceAnalyzer.Analyze(matches);
        var body = new StringBuilder(DatabaseReportPages.RangeForm(from, to));
        body.Append("<p>Equal-vote consensus; only snapshots BEFORE 00:00 UTC on match date. Missing league labels remain unknown. Accuracy is not ROI. Samples under 30 are not reliable.</p>");
        body.Append("<div class='scroll'><table><tr><th>League</th><th>Market</th><th>Correct</th><th>Graded</th><th>Accuracy</th><th>Wilson lower bound</th><th>Sample</th></tr>");
        foreach (var row in rows)
        {
            body.Append("<tr>");
            foreach (object value in new object[] { row.League, row.Market, row.Correct, row.Total,
                row.Accuracy.ToString("P1", CultureInfo.InvariantCulture), row.Safe.ToString("P1", CultureInfo.InvariantCulture),
                row.Total < 30 ? "Small sample" : "30+ observations" })
                DatabaseReportPages.Cell(body, value);
            body.Append("</tr>");
        }
        if (rows.Count == 0)
            body.Append("<tr><td colspan='7'>No graded pre-date consensus picks in this range.</td></tr>");
        return DatabaseReportPages.Page("League statistics", body.Append("</table></div>").ToString());
    }

    private Task<Dictionary<long, MatchResult>> ResultsAsync(IEnumerable<SimulatedTicket> tickets, CancellationToken ct)
    {
        var ids = tickets.SelectMany(t => t.Selections).Select(x => x.MatchId).Distinct().ToArray();
        return db.MatchResults.AsNoTracking().Where(x => ids.Contains(x.MatchId)).ToDictionaryAsync(x => x.MatchId, ct);
    }
}

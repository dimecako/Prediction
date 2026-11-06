using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Prediction.Data;
using Prediction.Entities;
using Prediction.Services;
using Prediction.Infrastructure;
using Prediction.Reporting;
using Prediction.ML;

public partial class Program
{
    private static async Task RunDailyTicketAsync(string[] args)
    {
        bool leagueOnly = args[0].Equals("daily-ticket-league", StringComparison.OrdinalIgnoreCase);
        string command = leagueOnly ? "daily-ticket-league" : "daily-ticket";
        bool why = args.Skip(2).Any(a => a.Equals("why", StringComparison.OrdinalIgnoreCase));
        var countArgs = args.Skip(2).Where(a => !a.Equals("why", StringComparison.OrdinalIgnoreCase)).ToList();
        int count = TicketDefaultCount;
        if (args.Length < 2 ||
            !DateOnly.TryParseExact(args[1], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date) ||
            countArgs.Count > 1 ||
            (countArgs.Count == 1 && (!int.TryParse(countArgs[0], out count) || count is < 1 or > TicketQualityPolicy.MaximumSelections)))
        {
            Console.Error.WriteLine($"[DAILY] Invalid arguments. Use: {command} yyyy-MM-dd [count 1-15] [why]");
            CommandExecution.Fail();
            return;
        }

        string? connection = Environment.GetEnvironmentVariable("ConnectionStrings__FootballDb");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("ConnectionStrings__FootballDb environment variable is not configured.");
        var options = new DbContextOptionsBuilder<FootballDbContext>().UseNpgsql(connection).Options;
        await using var db = new FootballDbContext(options);
        if (await ReuseFrozenTicketAsync(db, date, leagueOnly ? "daily-league" : "daily"))
            return;
        var matches = await db.Matches.AsNoTracking()
            .Where(m => m.MatchDate == date)
            .Include(m => m.PredictionSnapshots)
            .ToListAsync();
        if (matches.Count == 0)
        {
            Console.Error.WriteLine($"[DAILY] Nema sobrani prognozi za {date:yyyy-MM-dd}. " +
                $"Prvo pusti: dotnet run --no-build -- {date:yyyy-MM-dd}");
            CommandExecution.Fail();
            return;
        }

        IReadOnlyList<BookmakerMatch> offers;
        try
        {
            offers = await MaxbetOddsProvider.FetchAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or OperationCanceledException)
        {
            Console.Error.WriteLine($"[DAILY] Maxbet ponudata ne e dostapna: {ex.Message}. Tiket ne e generiran.");
            CommandExecution.Fail();
            return;
        }
        var now = DateTime.UtcNow;
        var history = await db.Matches.AsNoTracking()
            .Where(m => m.MatchDate < date && m.Result != null)
            .Include(m => m.Result).Include(m => m.PredictionSnapshots).ToListAsync();
        history = TicketQualityPolicy.PrepareHistory(history, date, Console.WriteLine);
        var calibration = TicketQualityPolicy.Calibrate(history);
        var mlRanker = MlMatchRanker.TrainFromHistory(history);
        if (mlRanker == null)
            Console.WriteLine("[DAILY] Nema dovolno istorija za ML; source consensus i kalibracija ostanuvaat.");
        var selection = DailyTicketSelector.Build(matches, offers, date, now, count, leagueOnly, mlRanker, calibration);
        Console.WriteLine($"[DAILY] {command} | Datum={date:yyyy-MM-dd} | Sobrani zapisi={matches.Count} | " +
            $"Sparteni razlicni mecevi={selection.Matched} | Spoeni duplikati={selection.Duplicates}");
        Console.WriteLine($"[DAILY] {TicketQualityPolicy.Version}: exact-market pre-date historical qualification. " +
            "Source agreement and raw ML are not calibrated win probabilities. No half-time markets.");
        Console.WriteLine(leagueOnly
            ? "[DAILY] Samo prioritetnite ligi; prvo eden po liga, potoa slednite po istiot redosled do baraniot broj."
            : "[DAILY] Site ligi, bez liga-filter ili ogranicuvanje po liga.");
        Console.WriteLine($"[DAILY] Kvalifikuvani={selection.Eligible} | Iskluceni ligi={selection.ExcludedLeagues} | " +
            $"Izbrani={selection.Picks.Count}/{count}");
        if (leagueOnly)
            foreach (var diagnosis in selection.LeagueDiagnoses)
                Console.WriteLine($"[LEAGUE] {diagnosis.Eligibility}: {diagnosis.League} | count={diagnosis.Count}");
        if (why)
            foreach (var rejection in selection.Rejections)
                Console.WriteLine($"[ODBIEN] {rejection}");

        foreach (var (pick, index) in selection.Picks.Select((p, i) => (p, i)))
        {
            Console.WriteLine($"[DAILY] {index + 1,2}. {pick.Fixture} | " +
                $"{TicketPerformanceAnalyzer.LeagueName(pick.League)} | {pick.Candidate.DisplayPick} | Kvota={pick.Odds:F2}");
            Console.WriteLine($"[SIGNALI] {pick.Evidence}");
        }
        if (selection.Picks.Count < count)
            Console.WriteLine("[DAILY] Nema dovolno mecevi so potvrdena kvota, dozvolen tip i nezapocnat termin" +
                (leagueOnly ? " vo izbranite ligi." : ".") + " Nema izmislenuvanje dopolnitelni izbori.");
        if (selection.Picks.Count == 0)
        {
            if (selection.NoSelection && CommandExecution.ExitCode == 0)
                Console.WriteLine("[DAILY] NO_SELECTION: no eligible candidate passed exact-market quality thresholds. No ticket saved or exported.");
            else
            {
                if (leagueOnly && selection.LeagueDiagnoses.Any(d => d.Eligibility == LeagueEligibility.Missing))
                    Console.Error.WriteLine("[DAILY] Missing league metadata; zero selections cannot be treated as NO_SELECTION.");
                Console.Error.WriteLine("[DAILY] Nema potvrdeni izbori; prazen tiket ne e izvezen.");
                CommandExecution.Fail();
            }
            return;
        }
        string path = Path.Combine(Directory.GetCurrentDirectory(),
            $"{command}-{date:yyyy-MM-dd}-{selection.Picks.Count}.xlsx");
        if (!CommandExecution.IsAutomation)
            await db.Database.MigrateAsync();
        var saved = await TicketLedger.SaveAsync(db,
            TicketLedger.CreateDaily(date, selection.Picks, now, leagueOnly));
        Console.WriteLine($"[SIMULATION] #{saved.Ticket.Id} | " +
            $"{(saved.Created ? "Saved" : "Already saved; not duplicated")} | Mode={saved.Ticket.Mode} | " +
            "1 unit for the WHOLE ticket; not an actual bet. Historical quality metrics are frozen in evidence.");
        if (!ExportSettings.ExcelEnabled)
        {
            Console.WriteLine("[DAILY] Excel export disabled; simulation remains saved in the database.");
            return;
        }

        try
        {
            ExportDailyTicket(path, command, date, saved.Ticket.CreatedAtUtc, count, selection.Picks, saved.Ticket);
            Console.WriteLine($"[DAILY] Excel snimen: {path}");
            Console.WriteLine($"[DAILY] Ocenuvanje: ticket-report {date:yyyy-MM-dd} {date:yyyy-MM-dd}");
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"[DAILY] Excel ne mozese da se snimi (dali e otvoren?): {ex.Message}");
            CommandExecution.Fail();
        }
    }

    private static void ExportDailyTicket(
        string path, string command, DateOnly date, DateTime createdAtUtc,
        int requested, IReadOnlyList<DailyTicketPick> picks, SimulatedTicket simulation)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Tiket");
        string[] headers = ["#", "Natprevar", "Liga", "Pazar", "Izbor", "Kvota",
            "Source Prob. % (1X2)", "1X2 soglasnost", "Osnovna kvota", "Signali", "Kickoff UTC", "Match ID"];
        for (int c = 0; c < headers.Length; c++)
            sheet.Cell(1, c + 1).Value = headers[c];
        sheet.Range(1, 1, 1, headers.Length).Style.Font.Bold = true;
        sheet.Range(1, 1, 1, headers.Length).Style.Fill.BackgroundColor = XLColor.LightGray;
        for (int i = 0; i < picks.Count; i++)
        {
            var p = picks[i];
            int row = i + 2;
            sheet.Cell(row, 1).Value = i + 1;
            sheet.Cell(row, 2).Value = p.Fixture;
            sheet.Cell(row, 3).Value = TicketPerformanceAnalyzer.LeagueName(p.League);
            sheet.Cell(row, 4).Value = p.Candidate.Market;
            sheet.Cell(row, 5).Value = p.Candidate.DisplayPick;
            sheet.Cell(row, 6).Value = p.Odds;
            if (p.SourceProbability is double probability)
                sheet.Cell(row, 7).Value = Math.Round(probability, 1);
            else
                sheet.Cell(row, 7).Value = "n/a";
            sheet.Cell(row, 8).Value = $"{p.MatchingVotes}/{p.TotalVotes}";
            sheet.Cell(row, 9).Value = p.BaseOdds;
            sheet.Cell(row, 10).Value = p.Evidence;
            sheet.Cell(row, 11).Value = p.KickoffUtc.ToString("O");
            sheet.Cell(row, 12).Value = p.MatchId;
        }
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();
        sheet.Column(10).Width = 90;
        sheet.Column(10).Style.Alignment.WrapText = true;
        var info = workbook.Worksheets.Add("Info");
        info.Cell(1, 1).Value = command;
        info.Cell(2, 1).Value = "Datum";
        info.Cell(2, 2).Value = date.ToString("yyyy-MM-dd");
        info.Cell(3, 1).Value = "Created UTC";
        info.Cell(3, 2).Value = createdAtUtc.ToString("O");
        info.Cell(4, 1).Value = "Izbrani / barani";
        info.Cell(4, 2).Value = $"{picks.Count}/{requested}";
        info.Cell(5, 1).Value = "Vkupna kvota";
        info.Cell(5, 2).Value = picks.Aggregate(1.0, (total, p) => total * p.Odds);
        info.Cell(6, 1).Value = $"Policy={TicketQualityPolicy.Version}; exact-market historical quality gates, confirmed odds.";
        info.Cell(7, 1).Value = "Source Prob. e prosecna prognoza za 1X2, NE verojatnost na kombinacijata.";
        info.Cell(8, 1).Value = "Rank: exact-market Wilson lower bound, historical accuracy/sample, source agreement. No guarantee.";
        info.Cell(9, 1).Value = "Alternatives need their own sample, accuracy and expected return. No half-time markets.";
        info.Columns().AdjustToContents();
        var tracking = workbook.Worksheets.Add("Simulation");
        tracking.Cell(1, 1).Value = "SIMULATION ONLY - not an actual bet";
        tracking.Cell(2, 1).Value = "Ticket ID";
        tracking.Cell(2, 2).Value = simulation.Id;
        tracking.Cell(3, 1).Value = "Created at UTC";
        tracking.Cell(3, 2).Value = simulation.CreatedAtUtc.ToString("O");
        tracking.Cell(4, 1).Value = "Mode";
        tracking.Cell(4, 2).Value = simulation.Mode;
        tracking.Cell(5, 1).Value = "Stake for whole ticket (units)";
        tracking.Cell(5, 2).Value = simulation.Stake;
        tracking.Columns().AdjustToContents();
        workbook.SaveAs(path);
    }
}

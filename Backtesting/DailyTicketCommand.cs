using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Prediction.Data;
using Prediction.Services;

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
            (countArgs.Count == 1 && (!int.TryParse(countArgs[0], out count) || count < 1)))
        {
            Console.Error.WriteLine($"[DAILY] Invalid arguments. Use: {command} yyyy-MM-dd [count] [why]");
            Environment.ExitCode = 1;
            return;
        }

        string? connection = Environment.GetEnvironmentVariable("ConnectionStrings__FootballDb");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("ConnectionStrings__FootballDb environment variable is not configured.");
        var options = new DbContextOptionsBuilder<FootballDbContext>().UseNpgsql(connection).Options;
        await using var db = new FootballDbContext(options);
        var matches = await db.Matches.AsNoTracking()
            .Where(m => m.MatchDate == date)
            .Include(m => m.PredictionSnapshots)
            .ToListAsync();
        if (matches.Count == 0)
        {
            Console.Error.WriteLine($"[DAILY] Nema sobrani prognozi za {date:yyyy-MM-dd}. " +
                $"Prvo pusti: dotnet run --no-build -- {date:yyyy-MM-dd}");
            Environment.ExitCode = 1;
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
            Environment.ExitCode = 1;
            return;
        }
        var now = DateTime.UtcNow;
        var selection = DailyTicketSelector.Build(matches, offers, date, now, count, leagueOnly);
        Console.WriteLine($"[DAILY] {command} | Datum={date:yyyy-MM-dd} | Sobrani zapisi={matches.Count} | " +
            $"Sparteni razlicni mecevi={selection.Matched} | Spoeni duplikati={selection.Duplicates}");
        Console.WriteLine("[DAILY] SAMO dnevni prognozi + tekovni kvoti. Bez istorija, kalibracija ili ML-treniranje. " +
            "Source Prob. i soglasnost NE se kalibrirana verojatnost na izbraniot tip.");
        Console.WriteLine(leagueOnly
            ? "[DAILY] Samo prioritetnite ligi; prvo eden po liga, potoa slednite po istiot redosled do baraniot broj."
            : "[DAILY] Site ligi, bez liga-filter ili ogranicuvanje po liga.");
        Console.WriteLine($"[DAILY] Kvalifikuvani={selection.Eligible} | Iskluceni ligi={selection.ExcludedLeagues} | " +
            $"Izbrani={selection.Picks.Count}/{count}");
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
            Console.Error.WriteLine("[DAILY] Nema potvrdeni izbori; prazen tiket ne e izvezen.");
            Environment.ExitCode = 1;
            return;
        }
        string path = Path.Combine(Directory.GetCurrentDirectory(),
            $"{command}-{date:yyyy-MM-dd}-{selection.Picks.Count}.xlsx");
        try
        {
            ExportDailyTicket(path, command, date, now, count, selection.Picks);
            Console.WriteLine($"[DAILY] Excel snimen: {path}");
            Console.WriteLine("[DAILY] Ne e istoriska simulacija; nema izmisleni Accuracy, EV ili sansa za cel tiket.");
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"[DAILY] Excel ne mozese da se snimi (dali e otvoren?): {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static void ExportDailyTicket(
        string path, string command, DateOnly date, DateTime createdAtUtc,
        int requested, IReadOnlyList<DailyTicketPick> picks)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Tiket");
        string[] headers = ["#", "Natprevar", "Liga", "Pazar", "Izbor", "Kvota",
            "Source Prob. % (1X2)", "1X2 soglasnost", "Osnovna kvota", "Signali", "Kickoff UTC"];
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
        info.Cell(6, 1).Value = "Samo dnevnite izvori i tekovni Maxbet kvoti; bez istorija ili ML-treniranje.";
        info.Cell(7, 1).Value = "Source Prob. e prosecna prognoza za 1X2, NE verojatnost na kombinacijata.";
        info.Cell(8, 1).Value = "Rang: poznata Source Prob., povisoka Prob., soglasnost, broj izvori; ne garancija.";
        info.Cell(9, 1).Value = "Alternativa: najniska potvrdena kvota nad minimumot megju dozvolenite tipovi.";
        info.Columns().AdjustToContents();
        workbook.SaveAs(path);
    }
}

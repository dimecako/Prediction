using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Prediction.Data;
using Prediction.Entities;
using Prediction.Services;

public partial class Program
{
    private const int TicketMinHistory = 100;
    private const int TicketDefaultCount = 15;

    private sealed record TicketCandidate(
        string Market,
        string Pick,
        string Key,
        string Detail);

    private static async Task RunTicketAsync(string[] args)
    {
        var flags = args.Skip(2)
            .Where(x => x.Equals("nodc", StringComparison.OrdinalIgnoreCase) ||
                        x.Equals("safe", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.ToLowerInvariant())
            .ToHashSet();
        bool allowDoubleChance = !flags.Contains("nodc");
        bool rankBySafety = flags.Contains("safe");
        string? countArg = args.Skip(2).FirstOrDefault(x => !flags.Contains(x.ToLowerInvariant()));
        int count = TicketDefaultCount;

        if (args.Length < 2 ||
            !DateOnly.TryParseExact(
                args[1],
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date) ||
            (countArg != null && (!int.TryParse(countArg, out count) || count < 1)))
        {
            Console.Error.WriteLine("[TICKET] Invalid arguments. Use: ticket yyyy-MM-dd [count] [nodc] [safe]");
            Environment.ExitCode = 1;
            return;
        }

        string? connection = Environment.GetEnvironmentVariable("ConnectionStrings__FootballDb");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException(
                "ConnectionStrings__FootballDb environment variable is not configured.");

        var dbOptions = new DbContextOptionsBuilder<FootballDbContext>()
            .UseNpgsql(connection)
            .Options;

        await using var db = new FootballDbContext(dbOptions);

        // Only results known before the target date, so the history is never contaminated by it.
        var history = await db.Matches
            .AsNoTracking()
            .Where(x => x.MatchDate < date && x.Result != null)
            .Include(x => x.Result)
            .Include(x => x.PredictionSnapshots)
            .ToListAsync();

        var calibration = new Tally();
        foreach (var match in history)
        {
            foreach (var candidate in BuildTicketCandidates(match, allowDoubleChance))
                calibration.Add(candidate.Key, IsTicketPickCorrect(candidate, match.Result!));
        }

        var today = await db.Matches
            .AsNoTracking()
            .Where(x => x.MatchDate == date && x.Result == null)
            .Include(x => x.PredictionSnapshots)
            .ToListAsync();

        IReadOnlyList<BookmakerMatch> bookmaker = [];
        try
        {
            bookmaker = await MaxbetOddsProvider.FetchAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TIKET] Kvotite od Maxbet ne se dostapni ({ex.Message}). Rangiranje spored sigurnost, bez kvoti.");
        }

        bool futureOnly = date >= DateOnly.FromDateTime(DateTime.Now);
        int matched = 0;
        int started = 0;

        var best = new List<TicketPick>();

        foreach (var match in today)
        {
            var offer = bookmaker.FirstOrDefault(b =>
                Math.Abs(DateOnly.FromDateTime(b.KickoffUtc).DayNumber - match.MatchDate.DayNumber) <= 1 &&
                TeamNameMatcher.SameFixture(match.HomeTeam, match.AwayTeam, b.Home, b.Away));

            if (offer != null)
            {
                matched++;

                if (futureOnly && offer.KickoffUtc <= DateTime.UtcNow)
                {
                    started++;
                    continue;
                }
            }

            var scored = BuildTicketCandidates(match, allowDoubleChance)
                .Select(c =>
                {
                    var stat = calibration.Get(c.Key);
                    double? odds = offer != null
                        ? MaxbetOddsProvider.GetOdds(offer, c.Market, c.Pick)
                        : null;
                    return (Candidate: c, stat.Total, stat.Correct, Odds: odds);
                })
                .Where(x => x.Total >= TicketMinHistory)
                .Select(x => new TicketPick(
                    $"{match.HomeTeam} vs {match.AwayTeam}",
                    x.Candidate,
                    (double)x.Correct / x.Total,
                    CalibrationAnalyzer.WilsonLower(x.Correct, x.Total),
                    x.Total,
                    x.Odds))
                .OrderByDescending(x => rankBySafety || x.Odds == null ? x.Safe : x.Safe * x.Odds.Value)
                .ToList();

            if (scored.Count == 0)
                continue;

            best.Add(scored[0]);
        }

        Console.WriteLine();
        Console.WriteLine(
            $"[TIKET] Datum: {date:yyyy-MM-dd} | Neodigrani natprevari: {today.Count} | " +
            $"Istorija: {history.Count} natprevari so rezultat");
        Console.WriteLine(
            $"[TIKET] Maxbet: {bookmaker.Count} natprevari vcitani | Sparteni po iminja na timovi: {matched}/{today.Count} | " +
            $"Vekje zapocnati (preskoknati): {started}");
        Console.WriteLine(
            "[TIKET] Za sekoj natprevar se koristi pazarot so najdobra izmerena tocnost x kvota " +
            $"(1X2, dvojna sansa, BTTS, Golovi). Potrebni se {TicketMinHistory}+ istoriski slucai po signal.");

        if (best.Count == 0)
        {
            Console.WriteLine("[TIKET] Nema izbori so dovolno istorija za ovoj den.");
            return;
        }

        // Value picks first (measured accuracy x odds > 1); the rest only fill the ticket.
        var picks = best
            .OrderByDescending(x => !rankBySafety && x.HasValue)
            .ThenByDescending(x => rankBySafety || x.Odds == null ? x.Safe : x.Safe * x.Odds.Value)
            .ThenByDescending(x => x.Accuracy)
            .Take(count)
            .ToList();

        double combined = 1.0;
        double combinedSafe = 1.0;
        double combinedOdds = 1.0;
        bool allOdds = true;

        for (int i = 0; i < picks.Count; i++)
        {
            var p = picks[i];
            combined *= p.Accuracy;
            combinedSafe *= p.Safe;

            if (p.Odds.HasValue)
                combinedOdds *= p.Odds.Value;
            else
                allOdds = false;

            string odds = p.Odds.HasValue ? $"{p.Odds.Value:F2}" : "n/a";
            string value = p.Odds.HasValue ? $"{(p.Accuracy * p.Odds.Value - 1) * 100:+0;-0}%" : "n/a";
            string flag = p.HasValue ? "VREDNOST" : "nema vrednost";

            Console.WriteLine(
                $"[TIKET] {i + 1,2}. {p.Fixture,-44} {p.Candidate.Market,-5} {p.Candidate.Pick,-5} | " +
                $"Kvota={odds,5} | Istor.={p.Accuracy * 100:F1}% (n={p.Sample}, sigurno {p.Safe * 100:F1}%) | " +
                $"EV={value,5} {flag}");
        }

        Console.WriteLine();
        Console.WriteLine(
            $"[TIKET] Natprevari so upotrebliv izbor: {best.Count} | Prikazani izbori: {picks.Count} (barani {count}) | " +
            $"Izbori so vrednost: {picks.Count(x => x.HasValue)}");

        if (picks.Count < count)
            Console.WriteLine("[TIKET] Pomalku izbori od baranite: ostanatite nemaa pouzdana istorija.");

        foreach (var group in picks.GroupBy(x => x.Candidate.Market))
            Console.WriteLine($"[TIKET] Pazar {group.Key}: {group.Count()} izbori");

        Console.WriteLine(
            $"[TIKET] Sansa da pominat SITE {picks.Count}: {combined * 100:F2}% " +
            $"(konzervativno {combinedSafe * 100:F2}%).");

        if (allOdds)
        {
            Console.WriteLine(
                $"[TIKET] Vkupna kvota: {combinedOdds:F2} | Ocekuvan povrat za 1 uplateno: {combined * combinedOdds:F2} " +
                $"(konzervativno {combinedSafe * combinedOdds:F2}). Nad 1.00 znaci vrednost.");
        }
        else
        {
            Console.WriteLine(
                "[TIKET] Nekoi izbori nemaat Maxbet kvota (imeto ne e sparteno ili pazarot ne postoi), " +
                "pa vkupnata kvota i ocekuvaniot povrat ne se prikazuvaat.");
        }

        Console.WriteLine(
            "[TIKET] Ocekuvaniot povrat se bazira na minata tocnost; ne e garancija, a kvotite se menuvaat.");

        string safeSuffix = rankBySafety ? "-safe" : "";
        string dcSuffix = allowDoubleChance ? "" : "-nodc";
        string exportPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            $"ticket-{date:yyyy-MM-dd}-{picks.Count}{dcSuffix}{safeSuffix}.xlsx");

        try
        {
            ExportTicketToExcel(exportPath, date, picks, combined, combinedSafe, combinedOdds, allOdds);
            Console.WriteLine($"[TIKET] Excel snimen: {exportPath}");
        }
        catch (IOException ex)
        {
            Console.WriteLine($"[TIKET] Excel ne mozese da se snimi (dali e otvoren?): {ex.Message}");
        }
    }

    private static void ExportTicketToExcel(
        string path,
        DateOnly date,
        IReadOnlyList<TicketPick> picks,
        double combined,
        double combinedSafe,
        double combinedOdds,
        bool allOdds)
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var sheet = workbook.Worksheets.Add("Tiket");

        string[] headers =
        [
            "#", "Natprevar", "Pazar", "Izbor", "Kvota", "Istoriska tocnost %",
            "Sigurna tocnost %", "Primerok (n)", "EV %", "Vrednost"
        ];

        for (int c = 0; c < headers.Length; c++)
            sheet.Cell(1, c + 1).Value = headers[c];

        var headerRow = sheet.Range(1, 1, 1, headers.Length);
        headerRow.Style.Font.Bold = true;
        headerRow.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;

        for (int i = 0; i < picks.Count; i++)
        {
            var p = picks[i];
            int r = i + 2;

            sheet.Cell(r, 1).Value = i + 1;
            sheet.Cell(r, 2).Value = p.Fixture;
            sheet.Cell(r, 3).Value = p.Candidate.Market;
            sheet.Cell(r, 4).Value = p.Candidate.Pick;

            if (p.Odds.HasValue)
            {
                sheet.Cell(r, 5).Value = Math.Round(p.Odds.Value, 2);
                sheet.Cell(r, 9).Value = Math.Round((p.Accuracy * p.Odds.Value - 1) * 100, 1);
            }
            else
            {
                sheet.Cell(r, 5).Value = "n/a";
                sheet.Cell(r, 9).Value = "n/a";
            }

            sheet.Cell(r, 6).Value = Math.Round(p.Accuracy * 100, 1);
            sheet.Cell(r, 7).Value = Math.Round(p.Safe * 100, 1);
            sheet.Cell(r, 8).Value = p.Sample;
            sheet.Cell(r, 10).Value = p.HasValue ? "VREDNOST" : "nema vrednost";
        }

        int s = picks.Count + 3;
        sheet.Cell(s, 2).Value = $"Datum: {date:yyyy-MM-dd}";
        sheet.Cell(s + 1, 2).Value = "Sansa da pominat site (%)";
        sheet.Cell(s + 1, 5).Value = Math.Round(combined * 100, 2);
        sheet.Cell(s + 2, 2).Value = "Konzervativna sansa (%)";
        sheet.Cell(s + 2, 5).Value = Math.Round(combinedSafe * 100, 2);

        if (allOdds)
        {
            sheet.Cell(s + 3, 2).Value = "Vkupna kvota";
            sheet.Cell(s + 3, 5).Value = Math.Round(combinedOdds, 2);
            sheet.Cell(s + 4, 2).Value = "Ocekuvan povrat za 1 uplateno";
            sheet.Cell(s + 4, 5).Value = Math.Round(combined * combinedOdds, 2);
        }

        sheet.Cell(s + 6, 2).Value = "Ocekuvaniot povrat se bazira na minata tocnost; ne e garancija.";

        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();
        workbook.SaveAs(path);
    }

    private sealed record TicketPick(
        string Fixture,
        TicketCandidate Candidate,
        double Accuracy,
        double Safe,
        int Sample,
        double? Odds)
    {
        public bool HasValue => Odds.HasValue && Accuracy * Odds.Value > 1.0;
    }

    private static IEnumerable<TicketCandidate> BuildTicketCandidates(Match match, bool allowDoubleChance)
    {
        var snapshots = CalibrationAnalyzer.LatestPerSource(match);
        var consensus = CalibrationAnalyzer.BuildResultConsensus(snapshots);

        if (consensus?.AverageProbability is double probability)
        {
            string bucket = CalibrationAnalyzer.ProbBucket(probability);
            string detail = $"Prob={probability:F1}% {consensus.MatchingVotes}/{consensus.TotalVotes}";

            yield return new("1X2", consensus.Pick, $"1X2|{bucket}", detail);

            if (allowDoubleChance && consensus.Pick != "X")
                yield return new(
                    "DC",
                    consensus.Pick == "1" ? "1X" : "X2",
                    $"DC|{bucket}",
                    detail);
        }

        int bttsTotal = snapshots.Count(x => x.Btts is "YES" or "NO");

        foreach (var group in snapshots
            .Where(x => x.Btts is "YES" or "NO")
            .GroupBy(x => x.Btts!))
        {
            int votes = Math.Min(group.Count(), 3);
            yield return new(
                "BTTS",
                group.Key,
                $"BTTS|{group.Key}|{votes}",
                $"{group.Count()}/{bttsTotal} sources");
        }

        var goalVotes = snapshots
            .Select(x => x.Goals?.Trim().ToUpperInvariant())
            .Where(x => x is "O0.5" or "U0.5" or "O1.5" or "U1.5" or "O2.5" or "U2.5" or "O3.5" or "U3.5")
            .ToList();

        foreach (var group in goalVotes.GroupBy(x => x!))
        {
            int votes = Math.Min(group.Count(), 3);
            yield return new(
                "Goals",
                group.Key,
                $"GOALS|{group.Key}|{votes}",
                $"{group.Count()}/{goalVotes.Count} sources");
        }
    }

    private static bool IsTicketPickCorrect(TicketCandidate candidate, MatchResult result) =>
        candidate.Market switch
        {
            "1X2" => candidate.Pick == result.Result,
            "DC" => candidate.Pick == "1X" ? result.Result != "2" : result.Result != "1",
            "BTTS" => candidate.Pick == (result.Btts ? "YES" : "NO"),
            _ => IsGoalsPickCorrect(candidate.Pick, result.TotalGoals)
        };

    private static bool IsGoalsPickCorrect(string pick, int totalGoals)
    {
        double line = double.Parse(pick[1..], CultureInfo.InvariantCulture);
        return pick[0] == 'O' ? totalGoals > line : totalGoals < line;
    }
}

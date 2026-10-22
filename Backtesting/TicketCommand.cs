using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Prediction.Data;
using Prediction.Entities;
using Prediction.Services;

public partial class Program
{
    private const int TicketDefaultCount = 15;

    private static async Task RunTicketAsync(string[] args)
    {
        bool only1To3P3Plus = args[0].Equals("ticket-1-3p3+", StringComparison.OrdinalIgnoreCase);
        var flags = args.Skip(2)
            .Where(x => x.Equals("nodc", StringComparison.OrdinalIgnoreCase) ||
                        x.Equals("safe", StringComparison.OrdinalIgnoreCase) ||
                        x.Equals("why", StringComparison.OrdinalIgnoreCase) ||
                        x.Equals("1-3p3+", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.ToLowerInvariant())
            .ToHashSet();
        only1To3P3Plus |= flags.Contains("1-3p3+");
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
            Console.Error.WriteLine("[TICKET] Invalid arguments. Use: ticket[-1-3p3+] yyyy-MM-dd [count] [nodc] [safe] [why]");
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
        await db.Database.MigrateAsync();

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
            foreach (var candidate in TicketMarkets.BuildCalibrationCandidates(match))
            {
                if (TicketMarkets.IsCorrect(candidate, match.Result!) is bool correct)
                    calibration.Add(candidate.Key, correct);
            }
        }

        var today = await db.Matches
            .Where(x => x.MatchDate == date && x.Result == null)
            .Include(x => x.PredictionSnapshots)
            .ToListAsync();

        IReadOnlyList<BookmakerMatch> bookmaker = [];
        try
        {
            bookmaker = await MaxbetOddsProvider.FetchAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or OperationCanceledException)
        {
            Console.WriteLine($"[TIKET] Kvotite od Maxbet ne se dostapni ({ex.Message}). " +
                "Bez potvrdena osnovna kvota nema izbori.");
        }

        int matched = 0;
        int started = 0;

        var best = new List<TicketPick>();
        var proposals = new List<TicketPick>();
        var rejected = new Dictionary<string, int>();
        void Reject(string reason) => rejected[reason] = rejected.GetValueOrDefault(reason) + 1;

        var paired = new List<(Match Match, BookmakerMatch Offer)>();
        foreach (var match in today)
        {
            var offers = bookmaker.Where(b =>
                DateOnly.FromDateTime(b.KickoffUtc.ToLocalTime()) == match.MatchDate &&
                TeamNameMatcher.SameFixture(match.HomeTeam, match.AwayTeam, b.Home, b.Away)).ToList();
            if (offers.Count != 1)
            {
                Reject(offers.Count == 0 ? "Nema spartena ponuda za datumot." : "Povekje ponudi: nejasen natprevar.");
                continue;
            }
            paired.Add((match, offers[0]));
        }

        // The same Maxbet fixture can be stored under several team spellings; keep the best-covered record.
        var unique = paired
            .GroupBy(p => (p.Offer.Home, p.Offer.Away, p.Offer.KickoffUtc))
            .Select(g => g.OrderByDescending(p => CalibrationAnalyzer.LatestPerSource(p.Match).Count).First())
            .ToList();
        int duplicates = paired.Count - unique.Count;
        var rejectedDetails = new List<string>();

        foreach (var (match, offer) in paired)
        {
            if (!string.IsNullOrWhiteSpace(offer.League))
                match.League = offer.League;
        }
        await db.SaveChangesAsync();

        foreach (var (match, offer) in unique)
        {
            matched++;

            if (offer.KickoffUtc <= DateTime.UtcNow)
            {
                started++;
                continue;
            }

            var decision = only1To3P3Plus
                ? TicketSelector.SelectFirstHalf1To3And3Plus(match, calibration,
                    c => MaxbetOddsProvider.GetOdds(offer, c.Market, c.Pick))
                : TicketSelector.Select(match, calibration,
                    c => MaxbetOddsProvider.GetOdds(offer, c.Market, c.Pick), rankBySafety);
            string SignalText(TicketCandidate? c)
            {
                if (c == null)
                    return "n/a";
                var stat = calibration.Get(c.Key);
                string label = c.Pick switch
                {
                    "YES" => "YES",
                    "NO" => "NO",
                    "O2.5" => "Over 2.5",
                    "U2.5" => "Under 2.5",
                    _ => c.DisplayPick
                };
                return stat.Total == 0 ? $"{label}: n/a (n=0)" :
                    $"{label}: {(double)stat.Correct / stat.Total:P1} (n={stat.Total})";
            }
            var fix = TicketMarkets.BuildCandidates(match).FirstOrDefault(x => x.Market == "1X2");
            var bttsSignal = TicketMarkets.BinarySignal(match, "BTTS");
            var goalsSignal = TicketMarkets.BinarySignal(match, "Goals");
            var snapshots = CalibrationAnalyzer.LatestPerSource(match);
            int overVotes = snapshots.Count(x => ProductionConsensusAggregator.NormalizeGoalsTo25(x.Goals) == "O2.5");
            int bttsVotes = snapshots.Count(x => x.Btts == "YES");
            var goalsStat = goalsSignal != null ? calibration.Get(goalsSignal.Key) : default;
            double signalPriority = CalibrationAnalyzer.WilsonLower(goalsStat.Correct, goalsStat.Total) * 100.0
                + overVotes * 0.1
                + (bttsSignal is { Pick: "YES" } ? bttsVotes * 0.01 : 0.0);
            string evidence = $"Pobeda {SignalText(fix)}; " +
                $"BTTS {SignalText(bttsSignal)}; " +
                $"Goals {SignalText(goalsSignal)}";
            TicketPick ToPick(ScoredTicketCandidate c) => new(
                match.Id, match.League, offer.KickoffUtc,
                $"{match.HomeTeam} vs {match.AwayTeam}", c.Candidate, c.Accuracy,
                c.Safe, c.Sample, c.Odds, decision.BaseOdds!.Value, evidence, signalPriority);
            if (decision.Confirmed != null)
                best.Add(ToPick(decision.Confirmed));
            else if (decision.Proposal != null)
                proposals.Add(ToPick(decision.Proposal));
            else
            {
                Reject(decision.Reason);
                rejectedDetails.Add(
                    $"{match.HomeTeam} vs {match.AwayTeam} | osnovna kvota={(decision.BaseOdds.HasValue ? decision.BaseOdds.Value.ToString("F2") : "n/a")} | " +
                    $"{decision.Reason} | {evidence}");
            }
        }

        var pairedOffers = paired.Select(p => (p.Offer.Home, p.Offer.Away, p.Offer.KickoffUtc)).ToHashSet();
        int dayOffers = bookmaker.Count(b => DateOnly.FromDateTime(b.KickoffUtc.ToLocalTime()) == date);

        Console.WriteLine();
        Console.WriteLine(
            $"[TIKET] Datum: {date:yyyy-MM-dd} | Neodigrani natprevari: {today.Count} | " +
            $"Istorija: {history.Count} natprevari so rezultat");
        Console.WriteLine(
            $"[TIKET] Maxbet: {bookmaker.Count} natprevari vcitani, od niv {dayOffers} za {date:yyyy-MM-dd} | " +
            $"Sparteni ponudi: {matched} (duplikati so razlicno ime spojuvani: {duplicates}) | " +
            $"Vekje zapocnati (preskoknati): {started}");
        if (flags.Contains("why"))
        {
            foreach (string pick in TicketMarkets.HalfGoalPicks)
            {
                var stat = calibration.Get($"HALFGOALS|{pick}");
                Console.WriteLine($"[ISTORIJA] {pick}: " +
                    (stat.Total == 0 ? "n/a" : $"{(double)stat.Correct / stat.Total:P1} (n={stat.Total})"));
            }
            foreach (var detail in rejectedDetails)
                Console.WriteLine($"[ODBIEN] {detail}");
            foreach (var offer in bookmaker
                .Where(b => DateOnly.FromDateTime(b.KickoffUtc.ToLocalTime()) == date &&
                            !pairedOffers.Contains((b.Home, b.Away, b.KickoffUtc)))
                .OrderBy(b => b.KickoffUtc))
                Console.WriteLine($"[BEZ-PAR] Maxbet ponuda bez nas natprevar: {offer.Home} vs {offer.Away}");
        }
        int halfTimeHistory = history.Count(x =>
            x.Result!.HalfTimeHomeGoals.HasValue && x.Result.HalfTimeAwayGoals.HasValue);
        Console.WriteLine($"[TIKET] Istorija so poluvreme: {halfTimeHistory}/{history.Count}. " +
            "Nepoznato poluvreme ne se ocenuva; tipovite bez 100+ primeri se preskoknuvaat.");
        if (flags.Contains("nodc"))
            Console.WriteLine("[TIKET] nodc e zadrzan za kompatibilnost; site novi tiketi se bez dvojna sansa.");
        Console.WriteLine("[TIKET] Nepotvrdenite kombinacii se ODVOENI predlozi, ne izbori vo tiketot.");
        Console.WriteLine(
            "[TIKET] Osnovna kvota <1.40: PK/PK & 2+ ili pobeda & golovi; 1.40-<1.70: pobeda & golovi; " +
            "1.70-2.00: samo fix; >2.00: BTTS (>=1.65), 3+, BTTS & 3+ ili golovi po poluvreme. " +
            "Drugite alternativi baraat kvota >=1.70. Fix/BTTS/3+: tocnost >=55% (n>=100); " +
            "kombinacii: >=55% ili EV>=+5% (n>=50). Signal BTTS >=55%, Goals >=60% (n>=100).");

        foreach (var rejection in rejected.OrderByDescending(x => x.Value))
            Console.WriteLine($"[TIKET] Preskoknati {rejection.Value}: {rejection.Key}");
        var picks = best
            .OrderByDescending(x => !rankBySafety && x.HasValue)
            .ThenByDescending(x => rankBySafety ? x.Safe : x.Safe * x.Odds!.Value)
            .ThenByDescending(x => x.Accuracy)
            .ThenByDescending(x => x.SignalPriority)
            .Take(count)
            .ToList();
        var suggested = proposals
            .OrderByDescending(x => x.Safe)
            .ThenByDescending(x => x.Accuracy)
            .ThenByDescending(x => x.SignalPriority)
            .Take(count - picks.Count).ToList();

        SimulatedTicket? simulation = null;
        if (picks.Count > 0)
        {
            string ticketMode = only1To3P3Plus
                ? (rankBySafety ? "1-3p3+-safe" : "1-3p3+")
                : (rankBySafety ? "safe" : "value");
            var snapshot = TicketLedger.Create(date, rankBySafety, picks.Select((p, i) =>
                new SimulatedTicketSelection
                {
                    Position = i + 1, MatchId = p.MatchId, Fixture = p.Fixture, League = p.League,
                    KickoffUtc = p.KickoffUtc, Market = p.Candidate.Market, Pick = p.Candidate.Pick,
                    CalibrationKey = p.Candidate.Key, Odds = Convert.ToDecimal(p.Odds!.Value),
                    Accuracy = p.Accuracy, Safe = p.Safe, Sample = p.Sample,
                    BaseOdds = Convert.ToDecimal(p.BaseOdds), Evidence = p.Evidence
                }), DateTime.UtcNow, ticketMode);
            var saved = await TicketLedger.SaveAsync(db, snapshot);
            simulation = saved.Ticket;
            picks = simulation.Selections.OrderBy(x => x.Position).Select(p => new TicketPick(
                p.MatchId, p.League, p.KickoffUtc, p.Fixture,
                new(p.Market, p.Pick, p.CalibrationKey, ""), p.Accuracy, p.Safe, p.Sample,
                (double)p.Odds, (double)p.BaseOdds, p.Evidence)).ToList();
            Console.WriteLine($"[SIMULATION] #{simulation.Id} | {(saved.Created ? "Saved" : "Already saved; not duplicated")} | " +
                $"Mode={simulation.Mode} | Stake={simulation.Stake:F2} units for the WHOLE ticket. Not an actual bet.");
        }
        else
            Console.WriteLine("[SIMULATION] No confirmed selections; no empty simulation saved.");

        Console.WriteLine($"[PREDLOZI] Bez potvrdena kvota: {proposals.Count}; prikazani: {suggested.Count}. NE se del od tiketot.");
        foreach (var p in suggested)
        {
            Console.WriteLine($"[PREDLOG] {p.Fixture} | {p.Candidate.DisplayPick} | " +
                $"Osnovna kvota={p.BaseOdds:F2} | Kvota=n/a | Istor.={p.Accuracy:P1} (n={p.Sample})");
            Console.WriteLine($"[SIGNALI] {p.Evidence}");
        }

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
                $"[TIKET] {i + 1,2}. {p.Fixture,-44} {p.Candidate.Market,-5} {p.Candidate.DisplayPick,-30} | " +
                $"Kvota={odds,5} | Istor.={p.Accuracy * 100:F1}% (n={p.Sample}, sigurno {p.Safe * 100:F1}%) | " +
                $"EV={value,5} {flag}");
            Console.WriteLine($"[SIGNALI] Osnovna kvota={p.BaseOdds:F2}; {p.Evidence}");
        }

        Console.WriteLine();
        Console.WriteLine(
            $"[TIKET] Natprevari so upotrebliv izbor: {best.Count} | Prikazani izbori: {picks.Count} (barani {count}) | " +
            $"Izbori so vrednost: {picks.Count(x => x.HasValue)}");

        if (picks.Count < count)
            Console.WriteLine("[TIKET] Pomalku potvrdeni izbori od baranite: nema dopolnuvanje so izbori pod pragovite.");

        foreach (var group in picks.GroupBy(x => x.Candidate.Market))
            Console.WriteLine($"[TIKET] Pazar {group.Key}: {group.Count()} izbori");

        if (picks.Count > 0)
            Console.WriteLine(
                $"[TIKET] Sansa da pominat SITE {picks.Count}: {combined * 100:F2}% " +
                $"(konzervativno {combinedSafe * 100:F2}%; pretpostavka na nezavisnost).");

        if (picks.Count > 0 && allOdds)
        {
            Console.WriteLine(
                $"[TIKET] Vkupna kvota: {combinedOdds:F2} | Ocekuvan povrat za 1 uplateno: {combined * combinedOdds:F2} " +
                $"(konzervativno {combinedSafe * combinedOdds:F2}). Nad 1.00 znaci vrednost.");
        }
        else if (picks.Count > 0)
        {
            Console.WriteLine(
                "[TIKET] Nekoi izbori nemaat Maxbet kvota (imeto ne e sparteno ili pazarot ne postoi), " +
                "pa vkupnata kvota i ocekuvaniot povrat ne se prikazuvaat.");
        }

        Console.WriteLine(
            "[TIKET] Ocekuvaniot povrat se bazira na minata tocnost; ne e garancija, a kvotite se menuvaat.");

        string modePrefix = only1To3P3Plus ? "ticket-1-3p3+" : "ticket";
        string safeSuffix = rankBySafety ? "-safe" : "";
        string dcSuffix = flags.Contains("nodc") ? "-nodc" : "";
        string simulationSuffix = simulation == null ? "" : $"-sim-{simulation.Id}";
        string exportPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            $"{modePrefix}-{date:yyyy-MM-dd}-{picks.Count}{dcSuffix}{safeSuffix}{simulationSuffix}.xlsx");

        try
        {
            ExportTicketToExcel(exportPath, date, picks, suggested, combined, combinedSafe, combinedOdds, allOdds, simulation);
            Console.WriteLine($"[TIKET] Excel snimen: {exportPath}");
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"[TIKET] Excel ne mozese da se snimi (dali e otvoren?): {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static void ExportTicketToExcel(
        string path,
        DateOnly date,
        IReadOnlyList<TicketPick> picks,
        IReadOnlyList<TicketPick> proposals,
        double combined,
        double combinedSafe,
        double combinedOdds,
        bool allOdds,
        SimulatedTicket? simulation)
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var sheet = workbook.Worksheets.Add("Tiket");

        string[] headers =
        [
            "#", "Natprevar", "Pazar", "Izbor", "Kvota", "Istoriska tocnost %",
            "Sigurna tocnost %", "Primerok (n)", "EV %", "Vrednost", "Osnovna kvota", "Signali", "Liga"
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
            sheet.Cell(r, 4).Value = p.Candidate.DisplayPick;

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
            sheet.Cell(r, 11).Value = p.BaseOdds;
            sheet.Cell(r, 12).Value = p.Evidence;
            sheet.Cell(r, 13).Value = TicketPerformanceAnalyzer.LeagueName(p.League);
        }

        int s = picks.Count + 3;
        sheet.Cell(s, 2).Value = $"Datum: {date:yyyy-MM-dd}";
        sheet.Cell(s + 1, 2).Value = picks.Count == 0 ? "Nema potvrdeni izbori." : "Sansa da pominat site (%, nezavisnost)";
        if (picks.Count > 0)
        {
            sheet.Cell(s + 1, 5).Value = Math.Round(combined * 100, 2);
            sheet.Cell(s + 2, 2).Value = "Konzervativna sansa (%)";
            sheet.Cell(s + 2, 5).Value = Math.Round(combinedSafe * 100, 2);
        }

        if (picks.Count > 0 && allOdds)
        {
            sheet.Cell(s + 3, 2).Value = "Vkupna kvota";
            sheet.Cell(s + 3, 5).Value = Math.Round(combinedOdds, 2);
            sheet.Cell(s + 4, 2).Value = "Ocekuvan povrat za 1 uplateno";
            sheet.Cell(s + 4, 5).Value = Math.Round(combined * combinedOdds, 2);
        }

        sheet.Cell(s + 6, 2).Value = "Ocekuvaniot povrat se bazira na minata tocnost; ne e garancija.";

        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();
        sheet.Column(12).Width = 90;
        sheet.Column(12).Style.Alignment.WrapText = true;

        var suggestions = workbook.Worksheets.Add("Predlozi - nepotvrdeni");
        string[] suggestionHeaders = ["Natprevar", "Predlog (NE e vo tiket)", "Kvota", "Istoriska tocnost %",
            "Primerok (n)", "Osnovna kvota", "Signali"];
        for (int c = 0; c < suggestionHeaders.Length; c++)
            suggestions.Cell(1, c + 1).Value = suggestionHeaders[c];
        suggestions.Row(1).Style.Font.Bold = true;
        for (int i = 0; i < proposals.Count; i++)
        {
            var p = proposals[i];
            int r = i + 2;
            suggestions.Cell(r, 1).Value = p.Fixture;
            suggestions.Cell(r, 2).Value = p.Candidate.DisplayPick;
            suggestions.Cell(r, 3).Value = "n/a";
            suggestions.Cell(r, 4).Value = Math.Round(p.Accuracy * 100, 1);
            suggestions.Cell(r, 5).Value = p.Sample;
            suggestions.Cell(r, 6).Value = p.BaseOdds;
            suggestions.Cell(r, 7).Value = p.Evidence;
        }
        suggestions.SheetView.FreezeRows(1);
        suggestions.Columns().AdjustToContents();
        suggestions.Column(7).Width = 90;
        suggestions.Column(7).Style.Alignment.WrapText = true;
        var tracking = workbook.Worksheets.Add("Simulation");
        tracking.Cell(1, 1).Value = "SIMULATION ONLY - not an actual bet";
        tracking.Cell(2, 1).Value = "Ticket ID";
        tracking.Cell(3, 1).Value = "Created at UTC";
        tracking.Cell(4, 1).Value = "Mode";
        tracking.Cell(5, 1).Value = "Stake for whole ticket (units)";
        if (simulation != null)
        {
            tracking.Cell(2, 2).Value = simulation.Id;
            tracking.Cell(3, 2).Value = simulation.CreatedAtUtc.ToString("O");
            tracking.Cell(4, 2).Value = simulation.Mode;
            tracking.Cell(5, 2).Value = simulation.Stake;
        }
        else
            tracking.Cell(2, 2).Value = "No confirmed selections; not saved";
        tracking.Cell(7, 1).Value = $"Results and ROI: ticket-report {date:yyyy-MM-dd} {date:yyyy-MM-dd}";
        tracking.Columns().AdjustToContents();
        workbook.SaveAs(path);
    }

    private sealed record TicketPick(
        long MatchId,
        string? League,
        DateTime KickoffUtc,
        string Fixture,
        TicketCandidate Candidate,
        double Accuracy,
        double Safe,
        int Sample,
        double? Odds,
        double BaseOdds,
        string Evidence,
        double SignalPriority = 0)
    {
        public bool HasValue => Odds.HasValue && Accuracy * Odds.Value > 1.0;
    }

}

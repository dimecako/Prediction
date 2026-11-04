using System.Globalization;
using ClosedXML.Excel;
using Prediction.Entities;

namespace Prediction.Services;

public static class DailyTicketArchive
{
    public static SimulatedTicket Read(XLWorkbook workbook, DateOnly date, IReadOnlyList<Match> matches)
    {
        var info = workbook.Worksheet("Info");
        string command = info.Cell(1, 1).GetString();
        if (command is not ("daily-ticket" or "daily-ticket-league") ||
            info.Cell(2, 2).GetString() != date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            throw new InvalidDataException("Daily workbook command/date does not match the requested archive.");
        var created = DateTime.ParseExact(info.Cell(3, 2).GetString(), "O",
            CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var sheet = workbook.Worksheet("Tiket");
        var picks = new List<DailyTicketPick>();
        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            string fixture = row.Cell(2).GetString();
            var matching = matches.Where(m => m.MatchDate == date &&
                $"{m.HomeTeam} vs {m.AwayTeam}" == fixture &&
                (row.Cell(12).IsEmpty() || m.Id == row.Cell(12).GetValue<long>())).ToList();
            if (matching.Count != 1)
                throw new InvalidDataException($"Cannot uniquely archive '{fixture}': {matching.Count} database matches.");
            string market = row.Cell(4).GetString();
            string display = row.Cell(5).GetString();
            string pick = (market, display) switch
            {
                ("1X2", "fix 1") => "1",
                ("1X2", "fix 2") => "2",
                ("BTTS", "BTTS") => "YES",
                ("Goals", "3+") => "O2.5",
                ("HalfGoals", _) when TicketMarkets.HalfGoalPicks.Contains(display) => display,
                ("Combo", _) when display == "BTTS & 3+" || new[] { "1", "2" }.Any(side =>
                    new[]
                    {
                        $"{side}-{side}", $"{side}-{side} & 2+",
                        $"{side} & T{side} 2+", $"{side} & 1+ prvo",
                        $"{side} & 1+ prvo & 1+ vtoro",
                        $"{side} & 2-5 golovi", $"{side} & 2-6 golovi"
                    }.Contains(display)) => display,
                _ => throw new InvalidDataException($"Unsupported archived selection: {market}/{display}")
            };
            var candidate = new TicketCandidate(market, pick, $"DAILY|{market}|{pick}", "");
            picks.Add(new(matching[0].Id, fixture, row.Cell(3).GetString(),
                DateTime.ParseExact(row.Cell(11).GetString(), "O",
                    CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                candidate, row.Cell(6).GetDouble(), row.Cell(9).GetDouble(),
                null, 0, 0, row.Cell(10).GetString()));
            if (row.Cell(1).GetValue<int>() != picks.Count)
                throw new InvalidDataException("Daily workbook positions must be consecutive.");
        }
        var ticket = TicketLedger.CreateDaily(date, picks, created, command == "daily-ticket-league");
        string counts = info.Cell(4, 2).GetString();
        if (counts.Split('/')[0] != picks.Count.ToString(CultureInfo.InvariantCulture))
            throw new InvalidDataException("Daily workbook selection count does not match its Info sheet.");
        return ticket;
    }
}

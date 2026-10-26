using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Prediction.Data;
using Prediction.Services;

public partial class Program
{
    private static async Task RunTicketImportAsync(string[] args)
    {
        if (args.Length != 2 || !DateOnly.TryParseExact(args[1], "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            Console.Error.WriteLine("[TICKET-IMPORT] Use: ticket-import yyyy-MM-dd");
            Environment.ExitCode = 1;
            return;
        }
        await using var db = new FootballDbContextFactory().CreateDbContext([]);
        await db.Database.MigrateAsync();
        var matches = await db.Matches.AsNoTracking().Where(m => m.MatchDate == date).ToListAsync();
        var files = Directory.EnumerateFiles(Directory.GetCurrentDirectory(), "*.xlsx")
            .Where(path => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(path),
                $@"^(daily-ticket(?:-league)?|ticket(?:-[A-Za-z0-9+]+)*)-{date:yyyy-MM-dd}(?:-.*)?\.xlsx$"))
            .OrderBy(path => path).ToList();
        await using var transaction = await db.Database.BeginTransactionAsync();
        foreach (string path in files)
        {
            using var workbook = new XLWorkbook(path);
            if (Path.GetFileName(path).StartsWith("daily-ticket-", StringComparison.Ordinal))
            {
                var saved = await TicketLedger.SaveAsync(db, DailyTicketArchive.Read(workbook, date, matches));
                Console.WriteLine($"[TICKET-IMPORT] {Path.GetFileName(path)} -> #{saved.Ticket.Id} " +
                    (saved.Created ? "saved" : "already saved"));
                continue;
            }
            var sheet = workbook.Worksheet("Tiket");
            var rows = sheet.RowsUsed().Skip(1)
                .Where(row => row.Cell(1).TryGetValue<int>(out _)).ToList();
            if (rows.Count == 0)
            {
                Console.WriteLine($"[TICKET-IMPORT] {Path.GetFileName(path)}: no confirmed picks to archive.");
                continue;
            }
            if (!workbook.TryGetWorksheet("Simulation", out var tracking) ||
                !tracking.Cell(2, 2).TryGetValue<long>(out var id))
                throw new InvalidDataException($"{Path.GetFileName(path)} has no saved simulation ID; cleanup refused.");
            var ticket = await db.SimulatedTickets.AsNoTracking().Include(t => t.Selections)
                .SingleOrDefaultAsync(t => t.Id == id && t.MatchDate == date);
            if (ticket == null || ticket.Selections.Count != rows.Count ||
                rows.Any(row => !ticket.Selections.Any(s =>
                    s.Position == row.Cell(1).GetValue<int>() && s.Fixture == row.Cell(2).GetString() &&
                    s.Market == row.Cell(3).GetString() &&
                    new TicketCandidate(s.Market, s.Pick, s.CalibrationKey, "").DisplayPick == row.Cell(4).GetString() &&
                    s.Odds == row.Cell(5).GetValue<decimal>())))
                throw new InvalidDataException($"{Path.GetFileName(path)} does not match its saved selections; cleanup refused.");
            Console.WriteLine($"[TICKET-IMPORT] {Path.GetFileName(path)} -> #{id} verified.");
        }
        await transaction.CommitAsync();
        Console.WriteLine($"[TICKET-IMPORT] {date:yyyy-MM-dd}: {files.Count} workbooks archived/verified. No files deleted.");
    }
}

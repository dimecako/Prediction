using System;
using System.Collections.Generic;
using System.Linq;
using ClosedXML.Excel;

public partial class ProductionConsensusAggregator
{
    public static string GetConfidenceLevel(
        int matchingVotes,
        int totalVotes,
        bool conflict = false)
    {
        if (totalVotes <= 0)
            return "WEAK";

        if (totalVotes == 1)
            return "SINGLE SOURCE";

        if (conflict)
            return "CONFLICT";

        if (matchingVotes >= 4 && matchingVotes * 5 >= totalVotes * 4)
            return "VERY STRONG";

        if (matchingVotes >= 3 && matchingVotes * 3 >= totalVotes * 2)
            return "STRONG";

        if (matchingVotes >= 2 && matchingVotes * 2 > totalVotes)
            return "MEDIUM";

        return "WEAK";
    }

    public void PrintGuaranteedReport()
    {
        Console.WriteLine(
            $"\n📊 СИТЕ ПОДАТОЦИ ЗА ДАТУМ: {targetDate}\n");

        Console.WriteLine(new string('-', 150));

       Console.WriteLine(
        $"{"Натпревар",-42} | " +
        $"{"Тип",-8} | " +
        $"{"Гласови",-9} | " +
        $"{"Confidence",-13} | " +
        $"{"Prob.",-10} | " +        
        $"{"Извори",-40} | " +
        $"{"BTTS",-14} | " +
        $"{"BTTS Prob.",-11} | " +
        $"{"Goals Prob.",-12} | " +
        $"{"Goals",-14}");

        Console.WriteLine(new string('-', 150));

        var orderedMatches = unifiedDb.Values
            .Select(match =>
            {
                var activeSources = match.Sources
                    .Where(x =>
                        x.Value != null &&
                        (x.Value.Tip == "1" ||
                        x.Value.Tip == "X" ||
                        x.Value.Tip == "2"))
                    .ToList();

                var tips = activeSources
                    .Select(x => x.Value.Tip)
                    .ToList();

                var tipGroups = tips
                    .GroupBy(x => x)
                    .OrderByDescending(g => g.Count())
                    .ToList();

                int totalVotes = tips.Count;

                int matchingVotes =
                    tipGroups.Count > 0
                        ? tipGroups[0].Count()
                        : 0;

                bool conflict =
                    tipGroups.Count > 1 &&
                    tipGroups[0].Count() == tipGroups[1].Count();

                string finalTip =
                    totalVotes == 0 || conflict
                        ? null
                        : tipGroups[0].Key;

                var probabilityValues = activeSources
                    .Where(x =>
                        finalTip != null &&
                        x.Value.Tip == finalTip &&
                        x.Value.Prob.HasValue)
                    .Select(x => x.Value.Prob.Value)
                    .ToList();

                double? averageProbability =
                    probabilityValues.Count > 0
                        ? probabilityValues.Average()
                        : (double?)null;

                return new
                {
                    Match = match,
                    ActiveSources = activeSources,
                    MatchingVotes = matchingVotes,
                    TotalVotes = totalVotes,
                    AverageProbability = averageProbability
                };
            })
            .Where(x => x.ActiveSources.Count > 0)

            // prvo po broj na soglasni glasovi
            .OrderByDescending(x => x.MatchingVotes)

            // ako se isti glasovite, prednost ima pomal vkupen broj
            // 3/3 pred 3/4, 2/2 pred 2/3
            .ThenBy(x => x.TotalVotes)

            // potoa po probability
            .ThenByDescending(x => x.AverageProbability ?? -1)

            // samo za stabilen redosled
            .ThenBy(x => x.Match.HomeOrig)
            .ThenBy(x => x.Match.AwayOrig)

            .ToList();

        var exportRows = new List<string[]>();

        string[] headers =
        {
            "Натпревар",
            "Тип",
            "Гласови",
            "Confidence",
            "Prob.",
            "Извори",
            "BTTS",
            "BTTS Prob.",
            "Goals Prob.",
            "Goals"
        };

        foreach (var item in orderedMatches)
        {
            var match = item.Match;
            var activeSources = item.ActiveSources;

            var tips = activeSources
                .Select(x => x.Value.Tip)
                .ToList();

            var tipGroups = tips
                .GroupBy(x => x)
                .OrderByDescending(g => g.Count())
                .ToList();

            int totalVotes = tips.Count;
            int matchingVotes = tipGroups.First().Count();

            bool conflict =
                tipGroups.Count > 1 &&
                tipGroups[0].Count() == tipGroups[1].Count();

            string finalTip =
                totalVotes == 1
                    ? tips[0]
                    : conflict
                        ? "CONFLICT"
                        : tipGroups.First().Key;

            string confidence =
                GetConfidenceLevel(matchingVotes, totalVotes, conflict);

            var probabilityValues = activeSources
                .Where(x =>
                    x.Value.Prob.HasValue &&
                    x.Value.Tip == finalTip)
                .Select(x => x.Value.Prob.Value)
                .ToList();

            string probabilityLabel = "-";

            if (probabilityValues.Count > 0)
            {
                double averageProbability =
                    probabilityValues.Average();

                probabilityLabel =
                    $"{averageProbability:F1}%";
            }

            string sourcesLabel =
                string.Join(",",
                    activeSources.Select(x => x.Key));

            var allPresentSources = match.Sources
                .Where(x => x.Value != null)
                .ToList();

            // -----------------------------
            // BTTS consensus
            // -----------------------------

            var bttsList = new List<string>();

            foreach (var source in allPresentSources)
            {
                var src = source.Value;

                if (!string.IsNullOrWhiteSpace(src.BttsMarket) &&
                    src.BttsMarket != "-")
                {
                    bttsList.Add(src.BttsMarket);
                }
                else if (!string.IsNullOrWhiteSpace(src.Btts) &&
                        src.Btts != "-")
                {
                    if (int.TryParse(
                            src.Btts.Replace("%", "").Trim(),
                            out int pct))
                    {
                        bttsList.Add(
                            pct >= 50 ? "YES" : "NO");
                    }
                }
            }

            string bttsConsensus = "-";
            string bttsProbabilityLabel = "-";

            if (bttsList.Count > 0)
            {
                var bttsGroups = bttsList
                    .GroupBy(x => x)
                    .OrderByDescending(g => g.Count())
                    .ToList();

                int bttsVotes = bttsGroups[0].Count();

                double bttsPercent =
                    (double)bttsVotes / bttsList.Count * 100.0;

                bttsProbabilityLabel =
                    $"{bttsPercent:F1}%";

                if (bttsGroups.Count > 1 &&
                    bttsGroups[0].Count() == bttsGroups[1].Count())
                {
                    bttsConsensus = "CONFLICT";
                }
                else
                {
                    bttsConsensus =
                        $"{bttsGroups[0].Key} " +
                        $"({bttsVotes}/{bttsList.Count})";
                }
            }

            // -----------------------------
            // GOALS consensus
            // -----------------------------

            var goalsList = allPresentSources
                .Where(x =>
                    !string.IsNullOrWhiteSpace(
                        x.Value.GoalsMarket) &&
                    x.Value.GoalsMarket != "-")
                .Select(x => x.Value.GoalsMarket)
                .ToList();

            string goalsConsensus = "-";
            string goalsProbabilityLabel = "-";

            if (goalsList.Count > 0)
            {
                var goalsGroups = goalsList
                    .GroupBy(x => x)
                    .OrderByDescending(g => g.Count())
                    .ToList();

                int goalsVotes = goalsGroups[0].Count();

                double goalsPercent =
                    (double)goalsVotes / goalsList.Count * 100.0;

                goalsProbabilityLabel =
                    $"{goalsPercent:F1}%";

                if (goalsGroups.Count > 1 &&
                    goalsGroups[0].Count() == goalsGroups[1].Count())
                {
                    goalsConsensus = "CONFLICT";
                }
                else
                {
                    goalsConsensus =
                        $"{goalsGroups[0].Key} " +
                        $"({goalsVotes}/{goalsList.Count})";
                }
            }

            string votesLabel =
                $"{matchingVotes}/{totalVotes}";

            string fixture =
                $"{match.HomeOrig} vs {match.AwayOrig}";

            exportRows.Add(new[]
            {
                fixture,
                finalTip,
                votesLabel,
                confidence,
                probabilityLabel,
                sourcesLabel,
                bttsConsensus,
                bttsProbabilityLabel,
                goalsProbabilityLabel,
                goalsConsensus
            });

            Console.WriteLine(
                $"{fixture,-42} | " +
                $"{finalTip,-8} | " +
                $"{votesLabel,-9} | " +
                $"{confidence,-13} | " +
                $"{probabilityLabel,-10} | " +                
                $"{sourcesLabel,-40} | " +
                $"{bttsConsensus,-14} | " +
                $"{bttsProbabilityLabel,-11} | " +
                $"{goalsProbabilityLabel,-12} | " +
                $"{goalsConsensus,-14}");


        }

        Console.WriteLine(new string('-', 165));

        Console.WriteLine(
            $"TOTAL MATCHES: {orderedMatches.Count}");

        string exportPath =
            Path.GetFullPath($"predictions-{targetDate}.xlsx");

        ExportExcelReport(exportPath, targetDate, headers, exportRows);

        Console.WriteLine($"[EXCEL] Report exported: {exportPath}");
    }

    private static void ExportExcelReport(
        string exportPath,
        string reportDate,
        IReadOnlyList<string> headers,
        IReadOnlyList<string[]> rows)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Predictions");
        const int columnCount = 10;

        worksheet.Cell(1, 1).Value = "FOOTBALL PREDICTIONS";
        worksheet.Range(1, 1, 1, columnCount).Merge();
        worksheet.Cell(1, 1).Style.Font.Bold = true;
        worksheet.Cell(1, 1).Style.Font.FontSize = 18;
        worksheet.Cell(1, 1).Style.Font.FontColor = XLColor.White;
        worksheet.Cell(1, 1).Style.Fill.BackgroundColor =
            XLColor.FromHtml("#17365D");
        worksheet.Cell(1, 1).Style.Alignment.Vertical =
            XLAlignmentVerticalValues.Center;
        worksheet.Row(1).Height = 32;

        worksheet.Cell(2, 1).Value =
            $"Датум: {reportDate}     |     Вкупно натпревари: {rows.Count}";
        worksheet.Range(2, 1, 2, columnCount).Merge();
        worksheet.Cell(2, 1).Style.Font.FontColor =
            XLColor.FromHtml("#44546A");
        worksheet.Cell(2, 1).Style.Font.Italic = true;
        worksheet.Row(2).Height = 23;

        const int headerRow = 4;
        for (int column = 0; column < headers.Count; column++)
        {
            worksheet.Cell(headerRow, column + 1).Value = headers[column];
        }

        for (int row = 0; row < rows.Count; row++)
        {
            for (int column = 0; column < rows[row].Length; column++)
            {
                worksheet.Cell(headerRow + row + 1, column + 1).Value =
                    rows[row][column];
            }
        }

        int lastRow = headerRow + rows.Count;
        var tableRange = worksheet.Range(headerRow, 1, lastRow, columnCount);
        worksheet.Row(headerRow).Height = 30;
        tableRange.FirstRow().Style.Fill.BackgroundColor =
            XLColor.FromHtml("#1F4E78");
        tableRange.FirstRow().Style.Font.FontColor = XLColor.White;
        tableRange.FirstRow().Style.Font.Bold = true;
        worksheet.Row(headerRow).Style.Alignment.WrapText = true;
        worksheet.Row(headerRow).Style.Alignment.Vertical =
            XLAlignmentVerticalValues.Center;

        if (rows.Count > 0)
        {
            var table = tableRange.CreateTable("PredictionsTable");
            table.Theme = XLTableTheme.TableStyleMedium2;
            table.ShowAutoFilter = true;

            var dataRange =
                worksheet.Range(headerRow + 1, 1, lastRow, columnCount);
            dataRange.Style.Alignment.Vertical =
                XLAlignmentVerticalValues.Center;
            dataRange.Style.Alignment.WrapText = true;
            dataRange.Style.Border.BottomBorder =
                XLBorderStyleValues.Hair;

            for (int row = headerRow + 1; row <= lastRow; row++)
            {
                worksheet.Row(row).Height = 30;
                var confidenceCell = worksheet.Cell(row, 4);
                confidenceCell.Style.Font.Bold = true;
                confidenceCell.Style.Font.FontColor =
                    confidenceCell.GetString() switch
                    {
                        "VERY STRONG" or "STRONG" => XLColor.FromHtml("#008000"),
                        "MEDIUM" => XLColor.FromHtml("#BF9000"),
                        "WEAK" or "SINGLE SOURCE" => XLColor.FromHtml("#C65911"),
                        "CONFLICT" => XLColor.FromHtml("#C00000"),
                        _ => XLColor.Black
                    };

                if (worksheet.Cell(row, 2).GetString() == "CONFLICT")
                {
                    worksheet.Cell(row, 2).Style.Font.FontColor =
                        XLColor.FromHtml("#C00000");
                    worksheet.Cell(row, 2).Style.Font.Bold = true;
                }
            }
        }

        worksheet.Column(1).Width = 37;
        worksheet.Column(2).Width = 13;
        worksheet.Column(3).Width = 11;
        worksheet.Column(4).Width = 18;
        worksheet.Column(5).Width = 12;
        worksheet.Column(6).Width = 42;
        worksheet.Column(7).Width = 23;
        worksheet.Column(8).Width = 14;
        worksheet.Column(9).Width = 14;
        worksheet.Column(10).Width = 23;
        worksheet.SheetView.FreezeRows(headerRow);
        worksheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        worksheet.PageSetup.FitToPages(1, 0);

        workbook.SaveAs(exportPath);
    }
    
    private void PrintPotentialMergeMisses()
    {
        Console.WriteLine();
        Console.WriteLine("--- POTENTIAL MERGE MISSES ---");

        var singles = unifiedDb.Values
            .Where(m =>
                m.Sources.Values.Count(s => s != null) == 1)
            .Select(m => new
            {
                Match = m,

                Source = m.Sources
                    .First(x => x.Value != null)
                    .Key,

                Home = NormalizeName(m.HomeOrig),
                Away = NormalizeName(m.AwayOrig)
            })
            .ToList();

        var candidates = new List<Tuple<
            string,
            string,
            string,
            string,
            double>>();

        for (int i = 0; i < singles.Count; i++)
        {
            for (int j = i + 1; j < singles.Count; j++)
            {
                var a = singles[i];
                var b = singles[j];

                // Не споредуваме два натпревари
                // од истиот source.
                if (a.Source == b.Source)
                    continue;

                double homeSim =
                    GetSimilarity(a.Home, b.Home);

                double awaySim =
                    GetSimilarity(a.Away, b.Away);

                double avg =
                    (homeSim + awaySim) / 2.0;

                // ОВА НЕ ПРАВИ MERGE.
                // Само бара потенцијални кандидати.
                if (homeSim >= 0.60 &&
                    awaySim >= 0.60 &&
                    avg >= 0.70)
                {
                    candidates.Add(
                        Tuple.Create(
                            a.Source,
                            $"{a.Match.HomeOrig} vs {a.Match.AwayOrig}",

                            b.Source,
                            $"{b.Match.HomeOrig} vs {b.Match.AwayOrig}",

                            avg));
                }
            }
        }

        foreach (var x in candidates
            .OrderByDescending(x => x.Item5)
            .Take(50))
        {
            Console.WriteLine();

            Console.WriteLine(
                $"[{x.Item1}] {x.Item2}");

            Console.WriteLine(
                $"[{x.Item3}] {x.Item4}");

            Console.WriteLine(
                $"Similarity: {x.Item5:P1}");
        }

        Console.WriteLine();

        Console.WriteLine(
            $"Potential candidates: {candidates.Count}");
    }
}
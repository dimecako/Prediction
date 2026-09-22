using System;
using System.Collections.Generic;
using System.Linq;

public partial class ProductionConsensusAggregator
{
    private string GetConfidenceLevel(int matchingVotes, int totalVotes)
    {
        if (totalVotes == 4 && matchingVotes == 4)
            return "VERY STRONG";

        if (totalVotes == 3 && matchingVotes == 3)
            return "STRONG";

        if (totalVotes == 4 && matchingVotes == 3)
            return "STRONG";

        if (totalVotes == 2 && matchingVotes == 2)
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

            string finalTip;
            string confidence;

            if (totalVotes == 1)
            {
                finalTip = tips[0];
                confidence = "SINGLE SOURCE";
            }
            else if (conflict)
            {
                finalTip = "CONFLICT";
                confidence = "CONFLICT";
            }
            else
            {
                finalTip = tipGroups.First().Key;

                if (totalVotes == 4 && matchingVotes == 4)
                    confidence = "VERY STRONG";
                else if (totalVotes == 4 && matchingVotes == 3)
                    confidence = "STRONG";
                else if (totalVotes == 3 && matchingVotes == 3)
                    confidence = "STRONG";
                else if (totalVotes == 3 && matchingVotes == 2)
                    confidence = "MEDIUM";
                else if (totalVotes == 2 && matchingVotes == 2)
                    confidence = "MEDIUM";
                else
                    confidence = "WEAK";
            }

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

            // -----------------------------
            // BTTS consensus
            // -----------------------------

            var bttsList = new List<string>();

            foreach (var source in activeSources)
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

            var goalsList = activeSources
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
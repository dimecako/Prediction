using System.Collections.Generic;

public partial class ProductionConsensusAggregator
{
    public void ExecutePipeline(List<SiteMatch> forebet, List<SiteMatch> statarea, List<SiteMatch> predictz, List<SiteMatch> wdw, List<SiteMatch> zulubet)
    {
        Console.WriteLine("\n--- ФАЗА НА СПОЈУВАЊЕ (MERGE) ---");
        Console.WriteLine($"Forebet: {forebet.Count} | PredictZ: {predictz.Count} | Statarea: {statarea.Count} | WinDrawWin: {wdw.Count} | Zulubet: {zulubet.Count}");

        foreach (var m in forebet)   MergeMatch(m);
        foreach (var m in predictz)  MergeMatch(m);
        foreach (var m in statarea)  MergeMatch(m);
        foreach (var m in wdw)       MergeMatch(m);
        foreach (var match in zulubet) MergeMatch(match);

        Console.WriteLine();
        Console.WriteLine("--- MERGE DIAGNOSTICS ---");

        var distribution = unifiedDb.Values
            .GroupBy(m => m.Sources.Values.Count(s => s != null))
            .OrderBy(x => x.Key);

        foreach (var group in distribution)
        {
            Console.WriteLine(
                $"{group.Key} source(s): {group.Count()} matches");
        }

        Console.WriteLine($"TOTAL unified fixtures: {unifiedDb.Count}");

        PrintPotentialMergeMisses();

        Console.WriteLine();
        Console.WriteLine("--- SINGLE SOURCE DIAGNOSTICS ---");

        var singleSourceBySite = unifiedDb.Values
            .Where(m => m.Sources.Values.Count(s => s != null) == 1)
            .Select(m => new
            {
                Match = m,
                Source = m.Sources
                    .First(x => x.Value != null)
                    .Key
            })
            .GroupBy(x => x.Source)
            .OrderByDescending(g => g.Count());

        foreach (var group in singleSourceBySite)
        {
            Console.WriteLine(
                $"{group.Key}: {group.Count()} single-source matches");
        }

        Console.WriteLine();
        Console.WriteLine("--- SINGLE SOURCE SAMPLES ---");

        foreach (var source in unifiedDb.Values
            .Where(m => m.Sources.Values.Count(s => s != null) == 1)
            .Select(m => m.Sources.First(x => x.Value != null).Key)
            .Distinct())
        {
            Console.WriteLine();
            Console.WriteLine($"[{source}]");

            var samples = unifiedDb.Values
                .Where(m =>
                    m.Sources.Values.Count(s => s != null) == 1 &&
                    m.Sources.Any(x =>
                        x.Key == source &&
                        x.Value != null))
                .Take(10);

            foreach (var match in samples)
            {
                Console.WriteLine(
                    $"  {match.HomeOrig} vs {match.AwayOrig}");
            }
        }
            
    }
}
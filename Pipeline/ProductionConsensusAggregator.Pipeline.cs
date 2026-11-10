using System.Collections.Generic;
using Prediction.Services;

public partial class ProductionConsensusAggregator
{
    public void ExecutePipeline(
    List<SiteMatch> forebet,
    List<SiteMatch> statarea,
    List<SiteMatch> predictz,
    List<SiteMatch> wdw,
    List<SiteMatch> zulubet,
    List<SiteMatch> vitibet,
    List<SiteMatch> feedinco,
    List<SiteMatch> soccerVista,
    List<SiteMatch> footyStats,
    List<SiteMatch> oddslot,
    List<SiteMatch> theDatabetics)
    {
        var sourceLists = new[]
        {
            forebet, statarea, predictz, wdw, zulubet, vitibet, feedinco,
            soccerVista, footyStats, oddslot, theDatabetics
        };
        var leagueAnchors = sourceLists.SelectMany(source => source)
            .Where(match => !string.IsNullOrWhiteSpace(match.League)).ToList();
        int filtered = 0;
        bool TryResolveAllowedLeague(SiteMatch match, out string league)
        {
            var matches = leagueAnchors.Where(anchor =>
                TeamNameMatcher.SameFixture(match.HomeTeam, match.AwayTeam,
                    anchor.HomeTeam, anchor.AwayTeam)).ToList();
            var identities = matches.Select(anchor =>
                    LeagueTicketSelector.GetCompetitionIdentity(anchor.League))
                .Where(identity => identity != null).Distinct(StringComparer.Ordinal).ToList();
            if (identities.Count != 1 ||
                !LeagueTicketSelector.IsAllowedLeague(matches[0].League))
            {
                league = "";
                return false;
            }

            league = matches[0].League!.Trim();
            return true;
        }

        foreach (var sourceMatches in sourceLists)
        {
            int before = sourceMatches.Count;
            sourceMatches.RemoveAll(match =>
            {
                if (!TryResolveAllowedLeague(match, out string league))
                    return true;
                match.League = league;
                return false;
            });
            filtered += before - sourceMatches.Count;
        }
        Console.WriteLine($"[LEAGUE FILTER] Removed {filtered} source predictions without a unique " +
            "league-bearing prediction-source match in the configured league allowlist.");

        Console.WriteLine("\n--- MERGE PHASE ---");
        Console.WriteLine(
    $"Forebet: {forebet.Count} | " +
    $"PredictZ: {predictz.Count} | " +
    $"Statarea: {statarea.Count} | " +
    $"WinDrawWin: {wdw.Count} | " +
    $"ZuluBet: {zulubet.Count} | " +
    $"Vitibet: {vitibet.Count} | " +
    $"Feedinco: {feedinco.Count} | " +
    $"SoccerVista: {soccerVista.Count} | " +
    $"FootyStats: {footyStats.Count} | " +
    $"Oddslot: {oddslot.Count} | " +
    $"TheDatabetics: {theDatabetics.Count}");

        foreach (var m in forebet)   MergeMatch(m);
        foreach (var m in predictz)  MergeMatch(m);
        foreach (var m in statarea)  MergeMatch(m);
        foreach (var m in wdw)       MergeMatch(m);
        foreach (var match in zulubet) MergeMatch(match);
        foreach (var match in vitibet) MergeMatch(match);
        foreach (var match in feedinco) MergeMatch(match);
        foreach (var match in soccerVista) MergeMatch(match);
        foreach (var match in footyStats) MergeMatch(match);
        foreach (var match in oddslot) MergeMatch(match);
        foreach (var match in theDatabetics) MergeMatch(match);

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

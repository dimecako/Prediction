using Prediction.Data;
using Prediction.Entities;
using Prediction.Services;

public partial class Program
{
    private static async Task RunMergeDuplicateMatchesAsync(string[] args)
    {
        bool apply = args.Skip(1).Any(x => x.Equals("--apply", StringComparison.OrdinalIgnoreCase));
        await using var db = new FootballDbContextFactory().CreateDbContext([]);
        var plan = await DuplicateMatchMerger.LoadPlanAsync(db);
        static string Row(Match x) => $"#{x.Id} {x.HomeTeam} - {x.AwayTeam}" +
            $" (snapshots={x.PredictionSnapshots.Count}, result={(x.Result == null ? "-" : $"{x.Result.HomeGoals}-{x.Result.AwayGoals}")})";

        Console.WriteLine(apply
            ? "[MERGE] APPLY: verified same-date duplicates are merged in one transaction."
            : "[MERGE] DRY-RUN: nothing is changed. Re-run with --apply to merge.");
        foreach (var merge in plan.Merges)
        {
            Console.WriteLine($"[MERGE] {merge.Canonical.MatchDate:yyyy-MM-dd} via {merge.Source} {merge.SourceMatchId}");
            Console.WriteLine($"[MERGE]   keep   {Row(merge.Canonical)}");
            foreach (var duplicate in merge.Duplicates)
                Console.WriteLine($"[MERGE]   remove {Row(duplicate)}");
        }
        foreach (var skip in plan.Skipped)
            Console.WriteLine($"[MERGE] SKIP ({skip.Reason}): " +
                string.Join(" | ", skip.Matches.Select(Row)));
        Console.WriteLine($"[MERGE] Groups to merge={plan.Merges.Count}; rows to remove=" +
            $"{plan.Merges.Sum(x => x.Duplicates.Count)}; skipped groups={plan.Skipped.Count}.");

        if (!apply)
            return;
        await DuplicateMatchMerger.ApplyAsync(db, plan.Merges);
        var remaining = await DuplicateMatchMerger.LoadPlanAsync(db);
        Console.WriteLine($"[MERGE] Applied. Remaining mergeable groups={remaining.Merges.Count}; " +
            $"skipped groups={remaining.Skipped.Count}.");
    }
}

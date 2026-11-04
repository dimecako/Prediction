using Microsoft.EntityFrameworkCore;
using Prediction.Data;
using Prediction.Entities;

namespace Prediction.Services;

public sealed record DuplicateMatchMerge(Match Canonical, IReadOnlyList<Match> Duplicates,
    string Source, string SourceMatchId);

public sealed record DuplicateMatchSkip(IReadOnlyList<Match> Matches, string Reason);

public sealed record DuplicateMatchPlan(IReadOnlyList<DuplicateMatchMerge> Merges,
    IReadOnlyList<DuplicateMatchSkip> Skipped);

public static class DuplicateMatchMerger
{
    // Only rows proven identical by a shared provider ID on the same date are merged.
    public static DuplicateMatchPlan Plan(IReadOnlyCollection<Match> matches,
        IReadOnlySet<long> ticketedMatchIds)
    {
        var byId = matches.ToDictionary(x => x.Id);
        var parent = byId.Keys.ToDictionary(x => x, x => x);
        long Find(long id) => parent[id] == id ? id : parent[id] = Find(parent[id]);

        foreach (var group in matches.SelectMany(m => m.PredictionSnapshots
                     .Where(s => !string.IsNullOrWhiteSpace(s.SourceMatchId))
                     .Select(s => (m.MatchDate, s.Source, s.SourceMatchId, MatchId: m.Id)))
                 .GroupBy(x => (x.MatchDate, x.Source, x.SourceMatchId)))
        {
            var ids = group.Select(x => x.MatchId).Distinct().ToList();
            foreach (long id in ids.Skip(1))
                parent[Find(id)] = Find(ids[0]);
        }

        var merges = new List<DuplicateMatchMerge>();
        var skipped = new List<DuplicateMatchSkip>();
        foreach (var component in byId.Keys.GroupBy(Find).Select(g => g.Select(id => byId[id])
                     .OrderBy(x => x.Id).ToList()).Where(x => x.Count > 1).OrderBy(x => x[0].Id))
        {
            var snapshots = component.SelectMany(x => x.PredictionSnapshots).ToList();
            var shared = component
                .Select(m => m.PredictionSnapshots.Where(s => !string.IsNullOrWhiteSpace(s.SourceMatchId))
                    .Select(s => (s.Source, SourceMatchId: s.SourceMatchId!)).ToHashSet())
                .Aggregate((left, right) => { left.IntersectWith(right); return left; })
                .OrderBy(x => x.Source == "Forebet" ? 0 : 1).ThenBy(x => x.Source).ToList();
            if (shared.Count == 0)
            {
                skipped.Add(new(component, "no single source ID is shared by every row"));
                continue;
            }
            var verified = shared.Where(link => ResultMatchResolver.AreVerifiedDuplicates(
                component, snapshots, link.Source, link.SourceMatchId)).ToList();
            if (verified.Count == 0)
            {
                skipped.Add(new(component, "different source IDs, scores or team names"));
                continue;
            }
            var ticketed = component.Where(x => ticketedMatchIds.Contains(x.Id)).ToList();
            if (ticketed.Count > 1)
            {
                skipped.Add(new(component, "more than one row is used by saved tickets"));
                continue;
            }
            var canonical = ticketed.SingleOrDefault() ?? component
                .OrderByDescending(x => x.Result != null)
                .ThenByDescending(x => x.PredictionSnapshots.Count)
                .ThenBy(x => x.Id).First();
            merges.Add(new(canonical, component.Where(x => x.Id != canonical.Id).ToList(),
                verified[0].Source, verified[0].SourceMatchId));
        }
        return new(merges, skipped);
    }

    public static async Task<DuplicateMatchPlan> LoadPlanAsync(FootballDbContext db)
    {
        var links = await db.PredictionSnapshots.AsNoTracking()
            .Where(x => x.SourceMatchId != null && x.SourceMatchId != "")
            .Select(x => new { x.MatchId, x.Match.MatchDate, x.Source, x.SourceMatchId })
            .Distinct().ToListAsync();
        var ids = links.GroupBy(x => (x.MatchDate, x.Source, x.SourceMatchId))
            .Where(g => g.Select(x => x.MatchId).Distinct().Count() > 1)
            .SelectMany(g => g.Select(x => x.MatchId)).ToHashSet();
        if (ids.Count == 0)
            return new([], []);
        var matches = await db.Matches.AsNoTracking().AsSplitQuery()
            .Include(x => x.PredictionSnapshots).Include(x => x.Result)
            .Where(x => ids.Contains(x.Id)).ToListAsync();
        var ticketed = (await db.SimulatedTickets.AsNoTracking().SelectMany(x => x.Selections)
            .Where(x => ids.Contains(x.MatchId)).Select(x => x.MatchId).Distinct().ToListAsync()).ToHashSet();
        return Plan(matches, ticketed);
    }

    public static async Task ApplyAsync(FootballDbContext db, IReadOnlyCollection<DuplicateMatchMerge> merges)
    {
        if (merges.Count == 0)
            return;
        var owned = db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync() : null;
        try
        {
            foreach (var merge in merges)
                await MergeAsync(db, merge);
            if (owned != null)
                await owned.CommitAsync();
        }
        finally
        {
            if (owned != null)
                await owned.DisposeAsync();
        }
    }

    private static async Task MergeAsync(FootballDbContext db, DuplicateMatchMerge merge)
    {
        long canonicalId = merge.Canonical.Id;
        var duplicateIds = merge.Duplicates.Select(x => x.Id).ToList();
        if (await db.SimulatedTickets.SelectMany(x => x.Selections)
                .AnyAsync(x => duplicateIds.Contains(x.MatchId)))
            throw new InvalidOperationException(
                $"Match rows {string.Join(", ", duplicateIds)} are used by tickets; merge aborted.");

        var canonical = await db.Matches.Include(x => x.Result).SingleAsync(x => x.Id == canonicalId);
        var duplicates = await db.Matches.Include(x => x.Result)
            .Where(x => duplicateIds.Contains(x.Id)).ToListAsync();
        canonical.League ??= duplicates.Select(x => x.League)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        canonical.KickoffUtc ??= duplicates.Select(x => x.KickoffUtc).FirstOrDefault(x => x.HasValue);
        var donor = duplicates.Select(x => x.Result).Where(x => x != null)
            .OrderByDescending(x => x!.HalfTimeHomeGoals.HasValue).FirstOrDefault();
        long? movedResultId = canonical.Result == null ? donor?.Id : null;
        if (canonical.Result is { HalfTimeHomeGoals: null } result && donor?.HalfTimeHomeGoals != null)
        {
            result.HalfTimeHomeGoals = donor.HalfTimeHomeGoals;
            result.HalfTimeAwayGoals = donor.HalfTimeAwayGoals;
        }
        canonical.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        if (movedResultId != null)
            await db.MatchResults.Where(x => x.Id == movedResultId)
                .ExecuteUpdateAsync(x => x.SetProperty(r => r.MatchId, canonicalId));

        await db.PredictionSnapshots.Where(x => duplicateIds.Contains(x.MatchId))
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.MatchId, canonicalId));
        await db.Matches.Where(x => duplicateIds.Contains(x.Id)).ExecuteDeleteAsync();
        db.ChangeTracker.Clear();
    }
}

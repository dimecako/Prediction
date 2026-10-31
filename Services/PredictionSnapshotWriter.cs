using Microsoft.EntityFrameworkCore;
using Prediction.Data;
using Prediction.Entities;
using System.Globalization;

namespace Prediction.Services;

public sealed class PredictionSnapshotWriter
{
    private readonly FootballDbContext db;

    public PredictionSnapshotWriter(FootballDbContext db)
    {
        this.db = db;
    }

    private static string? CleanValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string cleaned = value.Trim();

        return cleaned == "-"
            ? null
            : cleaned;
    }

    public async Task SaveAsync(
    string targetDate,
    IReadOnlyCollection<UnifiedMatch> matches)
    {
        DateTime capturedAtUtc = DateTime.UtcNow;

        if (!DateOnly.TryParseExact(
        targetDate,
        "yyyy-MM-dd",
        CultureInfo.InvariantCulture,
        DateTimeStyles.None,
        out var matchDate))
        {
            throw new ArgumentException(
                $"Invalid targetDate: {targetDate}",
                nameof(targetDate));
        }

        DateTime kickoffUtc =
            DateTime.SpecifyKind(
                matchDate.ToDateTime(TimeOnly.MinValue),
                DateTimeKind.Utc);

        int matchesCreated = 0;
        int matchesExisting = 0;
        int snapshotsCreated = 0;

        Console.WriteLine(
            $"[DB] Saving {matches.Count} unified matches for {targetDate}");

        foreach (var unified in matches)
        {
            string externalMatchId =
                MatchIdentity.BuildExternalMatchId(
                    targetDate,
                    unified.HomeOrig,
                    unified.AwayOrig);

            var match =
                db.Matches.Local
                    .FirstOrDefault(
                        x => x.ExternalMatchId == externalMatchId)
                ??
                await db.Matches
                    .SingleOrDefaultAsync(
                        x => x.ExternalMatchId == externalMatchId);

            var sourceLinks = unified.Sources.Where(x => x.Value != null &&
                !string.IsNullOrWhiteSpace(x.Value.SourceMatchId)).ToList();
            var linkedMatchIds = new HashSet<long>();
            var localLinks = db.Matches.Local.Where(x => x.MatchDate == matchDate &&
                sourceLinks.Any(link => x.PredictionSnapshots.Any(s =>
                    s.Source == link.Key && s.SourceMatchId == link.Value!.SourceMatchId))).ToList();
            foreach (var link in sourceLinks)
            {
                string source = link.Key;
                string sourceId = link.Value!.SourceMatchId!;
                var linked = await db.PredictionSnapshots
                    .Where(x => x.Match.MatchDate == matchDate &&
                        x.Source == source && x.SourceMatchId == sourceId)
                    .Select(x => x.MatchId).Distinct().ToListAsync();
                linkedMatchIds.UnionWith(linked);
                linkedMatchIds.UnionWith(db.Matches.Local.Where(x => x.MatchDate == matchDate &&
                    x.Id != 0 && x.PredictionSnapshots.Any(s => s.Source == source &&
                        s.SourceMatchId == sourceId)).Select(x => x.Id));
            }
            if (localLinks.Any(x => x.Id == 0))
            {
                if (localLinks.Count != 1 || linkedMatchIds.Count > 0 ||
                    match != null && !ReferenceEquals(match, localLinks[0]))
                    throw new InvalidOperationException(
                        $"Conflicting source-ID links in this capture for {unified.HomeOrig} vs {unified.AwayOrig}.");
                match = localLinks[0];
                Console.WriteLine("[DB] Reusing unsaved fixture by source ID; canonical names preserved.");
            }
            if (match == null && linkedMatchIds.Count == 1)
            {
                long linkedId = linkedMatchIds.Single();
                match = await db.Matches.SingleAsync(x => x.Id == linkedId);
                Console.WriteLine($"[DB] Reusing #{linkedId} by stored source ID; canonical names preserved.");
            }
            else if (match == null && linkedMatchIds.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Conflicting stored source IDs for {unified.HomeOrig} vs {unified.AwayOrig}; no new duplicate created.");
            }
            if (match != null && linkedMatchIds.Any(x => x != match.Id))
            {
                var group = await db.Matches.Include(x => x.PredictionSnapshots)
                    .Where(x => linkedMatchIds.Contains(x.Id) || x.Id == match.Id).ToListAsync();
                var groupSnapshots = group.SelectMany(x => x.PredictionSnapshots).ToList();
                bool verified = sourceLinks.Any(link =>
                    ResultMatchResolver.AreVerifiedDuplicates(group, groupSnapshots,
                        link.Key, link.Value!.SourceMatchId!));
                if (!verified)
                    throw new InvalidOperationException(
                        $"Conflicting source-ID links for {unified.HomeOrig} vs {unified.AwayOrig}; predictions not saved.");
            }

            if (match == null)
            {
                match = new Prediction.Entities.Match
                {
                    ExternalMatchId = externalMatchId,
                    HomeTeam = unified.HomeOrig,
                    AwayTeam = unified.AwayOrig,
                    MatchDate = matchDate,
                    KickoffUtc = kickoffUtc,
                    CreatedAtUtc = capturedAtUtc,
                    UpdatedAtUtc = capturedAtUtc
                };

                db.Matches.Add(match);

                matchesCreated++;
            }
            else
            {
                match.MatchDate = matchDate;

                if (!match.KickoffUtc.HasValue)
                    match.KickoffUtc = kickoffUtc;

                match.UpdatedAtUtc = capturedAtUtc;

                matchesExisting++;
            }

            foreach (var sourceEntry in unified.Sources)
            {
                MatchSourceData? sourceData = sourceEntry.Value;

                if (sourceData == null)
                    continue;

                string source = sourceEntry.Key;

                string? predictedResult =
                    CleanValue(sourceData.Tip);

                string? predictedScore =
                    CleanValue(sourceData.Score);

                string? btts =
                    CleanValue(sourceData.BttsMarket);

                string? goals =
                    CleanValue(sourceData.GoalsMarket);

                double? confidence =
                    sourceData.Prob.HasValue
                        ? sourceData.Prob.Value / 100.0
                        : null;

                PredictionSnapshot? lastSnapshot = null;

                if (match.Id != 0)
                {
                    lastSnapshot = await db.PredictionSnapshots
                        .AsNoTracking()
                        .Where(x =>
                            x.MatchId == match.Id &&
                            x.Source == source)
                        .OrderByDescending(x => x.CapturedAtUtc)
                        .FirstOrDefaultAsync();
                }

                bool unchanged =
                    lastSnapshot != null &&
                    lastSnapshot.SourceMatchId == sourceData.SourceMatchId &&
                    lastSnapshot.PredictedResult == predictedResult &&
                    lastSnapshot.PredictedScore == predictedScore &&
                    lastSnapshot.Btts == btts &&
                    lastSnapshot.Goals == goals &&
                    lastSnapshot.Confidence == confidence;

                if (unchanged)
                    continue;

                match.PredictionSnapshots.Add(
                    new PredictionSnapshot
                    {
                        Source = source,
                        SourceMatchId = sourceData.SourceMatchId,
                        PredictedResult = predictedResult,
                        PredictedScore = predictedScore,
                        Btts = btts,
                        Goals = goals,
                        Confidence = confidence,
                        CapturedAtUtc = capturedAtUtc
                    });

                snapshotsCreated++;
            }
        }

        int changes = await db.SaveChangesAsync();

        Console.WriteLine(
            $"[DB] Saved. New matches: {matchesCreated}, " +
            $"existing matches: {matchesExisting}, " +
            $"snapshots: {snapshotsCreated}, " +
            $"EF changes: {changes}");
    }
}

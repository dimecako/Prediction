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

            var match = await db.Matches
                .SingleOrDefaultAsync(
                    x => x.ExternalMatchId == externalMatchId);

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
                match.HomeTeam = unified.HomeOrig;
                match.AwayTeam = unified.AwayOrig;
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
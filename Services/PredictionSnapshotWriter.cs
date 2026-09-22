using Microsoft.EntityFrameworkCore;
using Prediction.Data;
using Prediction.Entities;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

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

        int matchesCreated = 0;
        int matchesExisting = 0;
        int snapshotsCreated = 0;

        Console.WriteLine(
            $"[DB] Saving {matches.Count} unified matches for {targetDate}");

        foreach (var unified in matches)
        {
            string externalMatchId = BuildExternalMatchId(
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
                    KickoffUtc = null,
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
                match.UpdatedAtUtc = capturedAtUtc;

                matchesExisting++;
            }

            foreach (var sourceEntry in unified.Sources)
            {
                MatchSourceData? sourceData = sourceEntry.Value;

                if (sourceData == null)
                    continue;

                match.PredictionSnapshots.Add(
                    new PredictionSnapshot
                    {
                        Source = sourceEntry.Key,

                        PredictedResult =
                            CleanValue(sourceData.Tip),

                        PredictedScore =
                            CleanValue(sourceData.Score),

                        Btts =
                            CleanValue(sourceData.BttsMarket),

                        Goals =
                            CleanValue(sourceData.GoalsMarket),

                        Confidence =
                            sourceData.Prob.HasValue
                                ? sourceData.Prob.Value / 100.0
                                : null,

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

    private static string BuildExternalMatchId(
    string targetDate,
    string homeTeam,
    string awayTeam)
    {
        string normalizedHome = NormalizeTeam(homeTeam);
        string normalizedAway = NormalizeTeam(awayTeam);

        string raw =
            $"{targetDate}|{normalizedHome}|{normalizedAway}";

        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(raw));

        return $"match-{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static string NormalizeTeam(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string text = value
            .Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder();

        foreach (char c in text)
        {
            UnicodeCategory category =
                CharUnicodeInfo.GetUnicodeCategory(c);

            if (category != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }

        text = builder
            .ToString()
            .Normalize(NormalizationForm.FormC)
            .ToLowerInvariant();

        text = Regex.Replace(
            text,
            @"[^a-z0-9]+",
            " ");

        return Regex.Replace(
            text.Trim(),
            @"\s+",
            " ");
    }
}
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Prediction.Data;
using Prediction.Entities;

namespace Prediction.Services;

public sealed record FtShadowSnapshot(long Id, string Source, string? Result, string? Goals,
    string? Btts, double? Confidence, DateTime CapturedAtUtc);

public sealed record FtShadowOffer(string Pick, string CalibrationKey, double? Odds,
    ScoredTicketCandidate BaselineScore, ScoredTicketCandidate StrictScore,
    bool BaselineQualified, bool StrictQualified, string BaselineReason, string StrictReason)
{
    public int? BaselinePosition { get; set; }
    public int? StrictPosition { get; set; }
}

public sealed record FtShadowFixture(long MatchId, string Fixture, string? League, DateTime KickoffUtc,
    double? BaseOdds, IReadOnlyList<FtShadowSnapshot> Snapshots, IReadOnlyList<FtShadowOffer> Offers,
    string Abstention);

public sealed record FtShadowCapture(string Experiment, string BaselineVersion, string ChallengerVersion,
    DateOnly Date, DateTime CapturedAtUtc, int RequestedCount, int HistoricalMatches,
    int InputFixtures, int Unmatched, int Ambiguous, int Started, int KnownResults, int Duplicates,
    IReadOnlyList<FtShadowFixture> Fixtures);

public static class FtShadowEvaluation
{
    public const string Experiment = "ft-paired-v1";
    public const string BaselineVersion = "ft-baseline-exact-market-v2";
    public const string ChallengerVersion = "ft-strict-exact-market-v1";

    public static FtShadowCapture Capture(IReadOnlyList<Match> matches, IReadOnlyList<BookmakerMatch> offers,
        DateOnly date, DateTime nowUtc, int count, IReadOnlyList<Match> history, Action<string> log)
    {
        if (nowUtc.Kind != DateTimeKind.Utc || date < DailyRunPlan.Today(new DateTimeOffset(nowUtc)) ||
            count is < 1 or > TicketQualityPolicy.MaximumSelections)
            throw new ArgumentException("Shadow capture requires UTC now, a current/future Skopje date and count 1-15. No backfill.");
        Match AsOf(Match match) => new()
        {
            Id = match.Id, HomeTeam = match.HomeTeam, AwayTeam = match.AwayTeam, MatchDate = match.MatchDate,
            KickoffUtc = match.KickoffUtc, League = match.League, Result = match.Result,
            PredictionSnapshots = match.PredictionSnapshots.Where(s => s.CapturedAtUtc != default &&
                s.CapturedAtUtc <= nowUtc).ToList()
        };
        var preparedHistory = TicketQualityPolicy.PrepareHistory(history
            .Where(m => m.Result != null && m.Result.CollectedAtUtc <= nowUtc).Select(AsOf), date, log);
        var tally = TicketQualityPolicy.Calibrate(preparedHistory);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Skopje");
        int unmatched = 0, ambiguous = 0, started = 0, knownResults = 0;
        var input = matches.Where(m => m.MatchDate == date).ToList();
        var pairs = new List<(Match Match, BookmakerMatch Offer)>();
        foreach (var match in input)
        {
            if (match.Result != null) { knownResults++; continue; }
            var matching = offers.Where(o => DateOnly.FromDateTime(
                    TimeZoneInfo.ConvertTimeFromUtc(o.KickoffUtc, zone)) == date &&
                TeamNameMatcher.SameFixture(match.HomeTeam, match.AwayTeam, o.Home, o.Away)).ToList();
            if (matching.Count == 0) { unmatched++; continue; }
            if (matching.Count != 1) { ambiguous++; continue; }
            if (matching[0].KickoffUtc.Kind != DateTimeKind.Utc)
                throw new InvalidOperationException($"Shadow kickoff must be UTC for match {match.Id}.");
            if (matching[0].KickoffUtc <= nowUtc) { started++; continue; }
            pairs.Add((match, matching[0]));
        }
        var unique = pairs.GroupBy(p => (p.Offer.Home, p.Offer.Away, p.Offer.KickoffUtc))
            .Select(g => g.OrderByDescending(p => CalibrationAnalyzer.LatestPerSource(AsOf(p.Match)).Count)
                .ThenBy(p => p.Match.Id).First()).OrderBy(p => p.Match.Id).ToList();
        if (unique.Count == 0)
            throw new InvalidOperationException("No uniquely matched future fixtures for a prospective paired evaluation.");
        var fixtures = new List<FtShadowFixture>();
        foreach (var (original, offer) in unique)
        {
            var match = AsOf(original);
            if (match.PredictionSnapshots.Any(s => string.IsNullOrWhiteSpace(s.Source) ||
                    s.CapturedAtUtc.Kind != DateTimeKind.Utc ||
                    s.Confidence is double confidence && (!double.IsFinite(confidence) || confidence is < 0 or > 1)))
                throw new InvalidOperationException($"Invalid as-of shadow snapshot for match {match.Id}.");
            double? Price(TicketCandidate c) => MaxbetOddsProvider.GetOdds(offer, c.Market, c.Pick);
            if (!TicketSelector.HasRequiredData(match, Price))
                throw new InvalidOperationException($"Shadow capture aborted: missing/invalid source data or base prices for match {match.Id}.");
            var fix = TicketMarkets.BuildCandidates(match).FirstOrDefault(c => c.Market == "1X2");
            var events = new List<FtShadowOffer>();
            foreach (var candidate in TicketMarkets.BuildCandidates(match, includeTeamGoals: true)
                         .Where(TicketQualityPolicy.IsWinnerGoals))
            {
                var price = Price(candidate);
                if (price is double value && (!double.IsFinite(value) || value <= 1))
                    throw new InvalidOperationException($"Invalid shadow price for match {match.Id}, {candidate.Pick}.");
                double? OnlyEvent(TicketCandidate c) => c.Market == "1X2" ? Price(c) :
                    c.Pick == candidate.Pick && c.Market == candidate.Market ? price : null;
                var baseline = TicketSelector.Select(match, tally, OnlyEvent, true, true, true);
                var strict = TicketSelector.Select(match, tally, OnlyEvent, true, true, true, strictFt: true);
                events.Add(new(candidate.Pick, candidate.Key, price,
                    TicketQualityPolicy.ScoreForGeneration(candidate, tally, price),
                    TicketQualityPolicy.Score(candidate, tally, price),
                    baseline.Confirmed != null, strict.Confirmed != null, baseline.Reason, strict.Reason));
            }
            fixtures.Add(new(match.Id, $"{match.HomeTeam} vs {match.AwayTeam}", offer.League, offer.KickoffUtc,
                fix == null ? null : Price(fix), CalibrationAnalyzer.LatestPerSource(match).Select(s =>
                    new FtShadowSnapshot(s.Id, s.Source, s.PredictedResult, s.Goals, s.Btts, s.Confidence,
                        s.CapturedAtUtc)).ToList(), events, fix == null ? "No unambiguous 1/2 signal with probability." : ""));
        }
        SelectArm(strict: false);
        SelectArm(strict: true);
        var capture = new FtShadowCapture(Experiment, BaselineVersion, ChallengerVersion, date, nowUtc, count, preparedHistory.Count,
            input.Count, unmatched, ambiguous, started, knownResults, pairs.Count - unique.Count, fixtures);
        Validate(capture);
        return capture;

        void SelectArm(bool strict)
        {
            ScoredTicketCandidate Score(FtShadowOffer o) => strict ? o.StrictScore : o.BaselineScore;
            var eligible = fixtures.Select(f => (Fixture: f, Offer: f.Offers
                    .Where(o => strict ? o.StrictQualified : o.BaselineQualified)
                    .OrderByDescending(o => Score(o).Safe).ThenByDescending(o => Score(o).Accuracy).FirstOrDefault()))
                .Where(x => x.Offer != null)
                .OrderByDescending(x => Score(x.Offer!).Safe).ThenByDescending(x => Score(x.Offer!).Accuracy)
                .ThenBy(x => x.Fixture.MatchId).Take(count).ToList();
            for (int i = 0; i < eligible.Count; i++)
                if (strict) eligible[i].Offer!.StrictPosition = i + 1;
                else eligible[i].Offer!.BaselinePosition = i + 1;
        }
    }

    public static Task<FtShadowRun?> FindAsync(FootballDbContext db, DateOnly date) =>
        db.FtShadowRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Experiment == Experiment && r.MatchDate == date);

    public static async Task<(FtShadowRun Run, bool Created)> SaveAsync(FootballDbContext db, FtShadowCapture capture)
    {
        var existing = await FindAsync(db, capture.Date);
        if (existing != null) return (existing, false);
        Validate(capture);
        var run = new FtShadowRun
        {
            Experiment = capture.Experiment, MatchDate = capture.Date, CapturedAtUtc = capture.CapturedAtUtc,
            RequestedCount = capture.RequestedCount, FrozenJson = JsonSerializer.Serialize(capture)
        };
        db.FtShadowRuns.Add(run);
        try
        {
            await db.SaveChangesAsync();
            return (run, true);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_ft_shadow_runs_Experiment_MatchDate"
        })
        {
            db.ChangeTracker.Clear();
            return (await FindAsync(db, capture.Date) ??
                throw new InvalidOperationException("Concurrent shadow run disappeared."), false);
        }
    }

    public static FtShadowCapture Read(FtShadowRun run)
    {
        var capture = JsonSerializer.Deserialize<FtShadowCapture>(run.FrozenJson) ??
            throw new InvalidDataException($"Empty frozen shadow run #{run.Id}.");
        if (run.Experiment != Experiment || capture.Experiment != run.Experiment ||
            capture.Date != run.MatchDate || capture.CapturedAtUtc != run.CapturedAtUtc ||
            capture.RequestedCount != run.RequestedCount ||
            capture.BaselineVersion != BaselineVersion || capture.ChallengerVersion != ChallengerVersion)
            throw new InvalidDataException($"Inconsistent frozen shadow run #{run.Id}.");
        Validate(capture);
        return capture;
    }

    private static void Validate(FtShadowCapture capture)
    {
        if (capture.Experiment != Experiment || capture.BaselineVersion != BaselineVersion ||
            capture.ChallengerVersion != ChallengerVersion ||
            capture.CapturedAtUtc == default || capture.CapturedAtUtc.Kind != DateTimeKind.Utc ||
            capture.Date < DailyRunPlan.Today(new DateTimeOffset(capture.CapturedAtUtc)) ||
            capture.RequestedCount is < 1 or > TicketQualityPolicy.MaximumSelections ||
            capture.HistoricalMatches < 0 || capture.Fixtures == null || capture.Fixtures.Count == 0 ||
            new[] { capture.InputFixtures, capture.Unmatched, capture.Ambiguous, capture.Started,
                capture.KnownResults, capture.Duplicates }.Any(n => n < 0) ||
            (long)capture.Fixtures.Count + capture.Unmatched + capture.Ambiguous + capture.Started +
                capture.KnownResults + capture.Duplicates != capture.InputFixtures ||
            capture.Fixtures.Any(f => f == null) ||
            capture.Fixtures.Select(f => f.MatchId).Distinct().Count() != capture.Fixtures.Count)
            throw new InvalidDataException("Invalid frozen FT shadow capture metadata.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Skopje");
        foreach (var fixture in capture.Fixtures)
        {
            if (fixture.MatchId <= 0 || string.IsNullOrWhiteSpace(fixture.Fixture) ||
                fixture.KickoffUtc.Kind != DateTimeKind.Utc || fixture.KickoffUtc <= capture.CapturedAtUtc ||
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(fixture.KickoffUtc, zone)) != capture.Date ||
                fixture.Snapshots == null || fixture.Snapshots.Count == 0 || fixture.Offers == null ||
                fixture.Snapshots.Any(s => s == null || string.IsNullOrWhiteSpace(s.Source) ||
                    s.CapturedAtUtc == default || s.CapturedAtUtc.Kind != DateTimeKind.Utc ||
                    s.CapturedAtUtc > capture.CapturedAtUtc ||
                    s.Confidence is double p && (!double.IsFinite(p) || p is < 0 or > 1)) ||
                fixture.Snapshots.Select(s => s.Source).Distinct().Count() != fixture.Snapshots.Count ||
                fixture.Offers.Any(o => o == null) ||
                fixture.Offers.Select(o => o.Pick).Distinct().Count() != fixture.Offers.Count ||
                fixture.Offers.Count(o => o.BaselinePosition.HasValue) > 1 ||
                fixture.Offers.Count(o => o.StrictPosition.HasValue) > 1)
                throw new InvalidDataException($"Invalid frozen FT shadow fixture {fixture.MatchId}.");
            foreach (var offer in fixture.Offers)
            {
                if (!ValidScore(offer.BaselineScore, offer, baseline: true) ||
                    !ValidScore(offer.StrictScore, offer, baseline: false) ||
                    offer.BaselineScore.Candidate with { Key = offer.CalibrationKey } != offer.StrictScore.Candidate ||
                    (offer.Odds.HasValue && (!double.IsFinite(offer.Odds.Value) || offer.Odds <= 1 ||
                        offer.Odds > (double)decimal.MaxValue)) ||
                    ((offer.BaselineQualified || offer.StrictQualified) && !offer.Odds.HasValue) ||
                    (offer.BaselinePosition.HasValue && !offer.BaselineQualified) ||
                    (offer.StrictPosition.HasValue && !offer.StrictQualified))
                    throw new InvalidDataException($"Invalid frozen FT shadow event {fixture.MatchId}, {offer.Pick}.");
            }
        }
        foreach (bool strict in new[] { false, true })
        {
            var positions = capture.Fixtures.SelectMany(f => f.Offers)
                .Select(o => strict ? o.StrictPosition : o.BaselinePosition)
                .Where(p => p.HasValue).Select(p => p!.Value).Order().ToList();
            if (positions.Count > capture.RequestedCount ||
                !positions.SequenceEqual(Enumerable.Range(1, positions.Count)))
                throw new InvalidDataException("Invalid frozen FT shadow selection positions.");
        }

        static bool ValidScore(ScoredTicketCandidate? score, FtShadowOffer offer, bool baseline) =>
            score?.Candidate != null && TicketQualityPolicy.IsWinnerGoals(score.Candidate) &&
            score.Candidate.Pick == offer.Pick && !string.IsNullOrWhiteSpace(offer.CalibrationKey) &&
            score.Candidate.Key == (baseline ? TicketQualityPolicy.FtBaselinePrefix : "") + offer.CalibrationKey &&
            score.Odds == offer.Odds && score.Sample >= 0 &&
            double.IsFinite(score.Accuracy) && score.Accuracy is >= 0 and <= 1 &&
            double.IsFinite(score.Safe) && score.Safe >= 0 && score.Safe <= score.Accuracy;
    }
}

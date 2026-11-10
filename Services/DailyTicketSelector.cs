using Prediction.Entities;
using Prediction.ML;

namespace Prediction.Services;

public sealed record DailyTicketPick(
    long MatchId, string Fixture, string? League, DateTime KickoffUtc,
    TicketCandidate Candidate, double Odds, double BaseOdds,
    double? SourceProbability, int MatchingVotes, int TotalVotes, string Evidence,
    double? MlProbability = null)
{
    public double HistoricalAccuracy { get; init; }
    public double HistoricalLower { get; init; }
    public int HistoricalSample { get; init; }
    public double Agreement => TotalVotes == 0 ? 0 : (double)MatchingVotes / TotalVotes;
    public double CombinedConfidence =>
        (Agreement * 100.0 + (MlProbability.HasValue ? MlProbability.Value * 100.0 : SourceProbability ?? 0.0)) / 2.0;
}

public sealed record DailyTicketDecision(DailyTicketPick? Pick, string Reason);

public sealed record DailyTicketSelection(
    IReadOnlyList<DailyTicketPick> Picks, IReadOnlyList<string> Rejections,
    int Matched, int Duplicates, int Eligible, int ExcludedLeagues)
{
    public IReadOnlyList<LeagueDiagnosis> LeagueDiagnoses { get; init; } = [];
    public bool NoSelection { get; init; }
}

public static class DailyTicketSelector
{
    public static DailyTicketSelection BuildFixed(
        IReadOnlyList<Match> matches, IReadOnlyList<BookmakerMatch> offers,
        DateOnly date, DateTime nowUtc, int count,
        Func<Match, TicketCandidate, double?>? predict = null, Tally? calibration = null)
    {
        if (count is < 1 or > TicketQualityPolicy.MaximumSelections)
            throw new ArgumentOutOfRangeException(nameof(count), "Fixed ticket count must be between 1 and 15.");
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Fixed ticket selection needs a UTC clock.");

        var rejections = new List<string>();
        var paired = new List<(Match Match, BookmakerMatch Offer)>();
        int excluded = 0;
        foreach (var match in matches.Where(m => m.MatchDate == date))
        {
            var matching = offers.Where(o => DateOnly.FromDateTime(o.KickoffUtc.ToLocalTime()) == date &&
                TeamNameMatcher.SameFixture(match.HomeTeam, match.AwayTeam, o.Home, o.Away)).ToList();
            if (matching.Count == 1)
                paired.Add((match, matching[0]));
            else
                rejections.Add($"{match.HomeTeam} vs {match.AwayTeam}: " +
                    (matching.Count == 0 ? "Nema Maxbet ponuda za datumot." : "Nejasno sparuvanje so povekje ponudi."));
        }

        var unique = paired.GroupBy(x => (x.Offer.Home, x.Offer.Away, x.Offer.KickoffUtc))
            .Select(g => g.OrderByDescending(x => CalibrationAnalyzer.LatestPerSource(x.Match).Count).First())
            .ToList();
        var eligible = new List<DailyTicketPick>();
        bool validFixture = false;
        bool invalidFixture = false;
        foreach (var (match, offer) in unique)
        {
            if (!LeagueTicketSelector.IsAllowedLeague(match.League))
            {
                rejections.Add($"{match.HomeTeam} vs {match.AwayTeam}: saved league is outside the configured allowlist.");
                excluded++;
                continue;
            }
            if (offer.KickoffUtc <= nowUtc)
            {
                rejections.Add($"{match.HomeTeam} vs {match.AwayTeam}: Natprevarot vekje zapocnal.");
                continue;
            }
            bool valid = TicketSelector.HasRequiredData(match,
                c => MaxbetOddsProvider.GetOdds(offer, c.Market, c.Pick));
            validFixture |= valid;
            invalidFixture |= !valid;
            if (!valid)
            {
                rejections.Add($"{match.HomeTeam} vs {match.AwayTeam}: Missing or invalid source votes, probabilities or base prices.");
                continue;
            }
            var snapshots = CalibrationAnalyzer.LatestPerSource(match);
            var consensus = CalibrationAnalyzer.BuildResultConsensus(snapshots);
            if (consensus is not { Pick: "1" or "2" })
            {
                rejections.Add($"{match.HomeTeam} vs {match.AwayTeam}: Nema nedvosmislen 1/2 konsenzus.");
                continue;
            }
            if (consensus.AverageProbability is not double probability ||
                !double.IsFinite(probability) || probability < 55)
            {
                rejections.Add($"{match.HomeTeam} vs {match.AwayTeam}: Source Prob. e pod 55%.");
                continue;
            }
            var candidate = TicketMarkets.BuildCandidates(match)
                .First(c => c.Market == "1X2") with
            {
                Pick = consensus.Pick
            };
            double? odds = MaxbetOddsProvider.GetOdds(offer, "1X2", consensus.Pick);
            if (!ValidFixedOdds(odds))
            {
                rejections.Add($"{match.HomeTeam} vs {match.AwayTeam}: Kvota pod 1.60 ili nevalidna.");
                continue;
            }
            var score = TicketQualityPolicy.Score(candidate, calibration ?? new Tally(), odds);
            if (!TicketQualityPolicy.HasCoverage(match, candidate) || !TicketQualityPolicy.Passes(score, 1.60))
            {
                rejections.Add($"{match.HomeTeam} vs {match.AwayTeam}: Exact-market quality/coverage threshold failed.");
                continue;
            }

            var evidence = $"FIX 1X2={consensus.Pick} {consensus.MatchingVotes}/{consensus.TotalVotes}; " +
                $"Source Prob.={probability:F1}%; Min odds=1.60; " + TicketQualityPolicy.Evidence(score);
            double? ml = null;
            if (predict != null)
            {
                ml = predict(match, candidate);
                if (ml is not double value || !double.IsFinite(value) || value is < 0.55 or > 1)
                {
                    rejections.Add($"{match.HomeTeam} vs {match.AwayTeam}: AI confidence below 55% or unavailable.");
                    continue;
                }
                evidence += $" AI 1X2={candidate.Pick} {ml.Value:P1}; raw uncalibrated model signal";
            }
            eligible.Add(new(match.Id, $"{match.HomeTeam} vs {match.AwayTeam}", match.League,
                offer.KickoffUtc, candidate, odds!.Value, odds.Value,
                probability, consensus.MatchingVotes, consensus.TotalVotes, evidence, ml)
            {
                HistoricalAccuracy = score.Accuracy, HistoricalLower = score.Safe, HistoricalSample = score.Sample
            });
        }

        var ranked = eligible
            .OrderByDescending(p => p.HistoricalLower)
            .ThenByDescending(p => p.HistoricalAccuracy)
            .ThenByDescending(p => p.SourceProbability)
            .ThenByDescending(p => p.Odds >= 1.8)
            .ThenByDescending(p => p.Odds)
            .ThenByDescending(p => p.Agreement)
            .ToList();
        var picks = LeagueTicketSelector.SelectAllowedLeagues(ranked, count, p => p.League);
        return new(picks, rejections, unique.Count, paired.Count - unique.Count,
            eligible.Count, excluded)
        {
            NoSelection = picks.Count == 0 && !invalidFixture &&
                (validFixture || excluded > 0) && calibration != null
        };
    }

    public static DailyTicketDecision Select(Match match, BookmakerMatch offer, MlMatchRanker? mlRanker = null,
        Tally? calibration = null)
    {
        if (!LeagueTicketSelector.IsAllowedLeague(match.League))
            return new(null, "Saved league is outside the configured allowlist.");
        var snapshots = CalibrationAnalyzer.LatestPerSource(match);
        var consensus = CalibrationAnalyzer.BuildResultConsensus(snapshots);
        if (consensus is not { Pick: "1" or "2" })
            return new(null, "Nema nedvosmislen dneven 1/2 konsenzus.");
        if (consensus.AverageProbability is double sourceProb &&
            (!double.IsFinite(sourceProb) || sourceProb is < 0 or > 100))
            return new(null, "Nevalidna Source Prob. vo dnevnite prognozi.");
        var fix = TicketMarkets.BuildCandidates(match, requireProbability: false)
            .First(c => c.Market == "1X2");
        double? baseOdds = MaxbetOddsProvider.GetOdds(offer, fix.Market, fix.Pick);
        if (!ValidOdds(baseOdds))
            return new(null, "Nema potvrdena osnovna Maxbet kvota.");
        if (calibration == null)
            return new(null, "Exact-market history is required; daily signals alone cannot qualify a selection.");
        var goals = TicketMarkets.BinarySignal(match, "Goals");
        var btts = TicketMarkets.BinarySignal(match, "BTTS");
        var decision = TicketSelector.Select(match, calibration,
            c => MaxbetOddsProvider.GetOdds(offer, c.Market, c.Pick), safe: true, includeTeamGoals: true);
        if (decision.Confirmed is not ScoredTicketCandidate score)
            return new(null, decision.Reason);
        var chosen = score.Candidate;
        var odds = score.Odds;

        string probability = consensus.AverageProbability is double p ? $"{p:F1}%" : "n/a";
        string BinaryEvidence(TicketCandidate? signal) => signal == null ? "n/a" :
            $"{signal.Pick} {TicketMarkets.SourceAgreement(match, signal).MatchingVotes}/" +
            $"{TicketMarkets.SourceAgreement(match, signal).TotalVotes}";
        double? mlProbability = mlRanker?.Predict(match)?.GetCandidateProbability(chosen);
        string evidence = $"1X2={consensus.Pick} {consensus.MatchingVotes}/{consensus.TotalVotes}; " +
            $"Source Prob.={probability}; {consensus.Level}; " +
            $"BTTS={BinaryEvidence(btts)}; Goals={BinaryEvidence(goals)}; " +
            TicketQualityPolicy.Evidence(score) +
            (mlProbability.HasValue ? $"; ML={mlProbability.Value:P1} (raw, uncalibrated)" : "; ML=n/a for exact market");
        return new(new(match.Id, $"{match.HomeTeam} vs {match.AwayTeam}", match.League,
            offer.KickoffUtc, chosen, odds!.Value, baseOdds!.Value,
            consensus.AverageProbability, consensus.MatchingVotes, consensus.TotalVotes, evidence, mlProbability)
        {
            HistoricalAccuracy = score.Accuracy, HistoricalLower = score.Safe, HistoricalSample = score.Sample
        }, "");
    }

    public static DailyTicketSelection Build(
        IReadOnlyList<Match> matches, IReadOnlyList<BookmakerMatch> offers,
        DateOnly date, DateTime nowUtc, int count, bool leagueOnly,
        MlMatchRanker? mlRanker = null, Tally? calibration = null)
    {
        if (count is < 1 or > TicketQualityPolicy.MaximumSelections || nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Daily selection needs a positive count and UTC clock.");
        var rejections = new List<string>();
        var paired = new List<(Match Match, BookmakerMatch Offer)>();
        foreach (var match in matches.Where(m => m.MatchDate == date))
        {
            var matching = offers.Where(o => DateOnly.FromDateTime(o.KickoffUtc.ToLocalTime()) == date &&
                TeamNameMatcher.SameFixture(match.HomeTeam, match.AwayTeam, o.Home, o.Away)).ToList();
            if (matching.Count != 1)
            {
                rejections.Add($"{match.HomeTeam} vs {match.AwayTeam}: " +
                    (matching.Count == 0 ? "Nema Maxbet ponuda za datumot." : "Nejasno sparuvanje so povekje ponudi."));
                continue;
            }
            paired.Add((match, matching[0]));
        }

        var unique = paired.GroupBy(x => (x.Offer.Home, x.Offer.Away, x.Offer.KickoffUtc))
            .Select(g => g.OrderByDescending(x => CalibrationAnalyzer.LatestPerSource(x.Match).Count).First())
            .ToList();
        var eligible = new List<DailyTicketPick>();
        bool evaluatedValidFixture = false;
        bool invalidFixtureData = false;
        foreach (var (match, offer) in unique)
        {
            if (offer.KickoffUtc <= nowUtc)
            {
                rejections.Add($"{match.HomeTeam} vs {match.AwayTeam}: Natprevarot vekje zapocnal.");
                continue;
            }
            if (!LeagueTicketSelector.IsAllowedLeague(match.League))
            {
                rejections.Add($"{match.HomeTeam} vs {match.AwayTeam}: saved league is outside the configured allowlist.");
                continue;
            }
            bool validFixture = TicketSelector.HasRequiredData(match,
                    c => MaxbetOddsProvider.GetOdds(offer, c.Market, c.Pick)) &&
                LeagueTicketSelector.IsAllowedLeague(match.League);
            evaluatedValidFixture |= validFixture;
            invalidFixtureData |= !validFixture;
            var decision = Select(match, offer, mlRanker, calibration);
            if (decision.Pick != null)
                eligible.Add(decision.Pick);
            else
                rejections.Add($"{match.HomeTeam} vs {match.AwayTeam}: {decision.Reason}");
        }

        int excluded = unique.Count(pair => !LeagueTicketSelector.IsAllowedLeague(pair.Match.League));
        var ranked = Rank(eligible);
        IReadOnlyList<DailyTicketPick> picks =
            LeagueTicketSelector.SelectAllowedLeagues(ranked, count, p => p.League);
        var targetMatches = matches.Where(match => match.MatchDate == date).ToList();
        bool hasSourceData = targetMatches.Count > 0 &&
            targetMatches.All(match => CalibrationAnalyzer.LatestPerSource(match)
                .Any(s => s.PredictedResult is "1" or "X" or "2"));
        return new(picks, rejections, unique.Count, paired.Count - unique.Count, eligible.Count, excluded)
        {
            LeagueDiagnoses = LeagueTicketSelector.Diagnose(unique
                .Where(pair => pair.Offer.KickoffUtc > nowUtc).Select(pair => pair.Match.League)),
            NoSelection = (picks.Count == 0 && calibration != null &&
                evaluatedValidFixture && !invalidFixtureData) || LeagueTicketSelector.IsNoSelection(true, picks.Count,
                unique.Where(pair => pair.Offer.KickoffUtc > nowUtc).Select(pair => pair.Match.League),
                offers.Count > 0 && (eligible.Count > 0
                    ? eligible.All(pick => pick.TotalVotes > 0)
                    : hasSourceData && !invalidFixtureData), evaluatedValidFixture)
        };
    }

    private static IOrderedEnumerable<DailyTicketPick> Rank(IEnumerable<DailyTicketPick> picks) =>
        picks.OrderByDescending(p => p.HistoricalLower)
            .ThenByDescending(p => p.HistoricalAccuracy)
            .ThenByDescending(p => p.HistoricalSample)
            .ThenByDescending(p => p.Agreement)
            .ThenByDescending(p => p.MatchingVotes)
            .ThenBy(p => p.Odds)
            .ThenBy(p => p.Fixture, StringComparer.Ordinal);

    private static bool ValidOdds(double? odds) => odds.HasValue && double.IsFinite(odds.Value) && odds > 1;
    private static bool ValidFixedOdds(double? odds) =>
        odds.HasValue && double.IsFinite(odds.Value) && odds.Value >= 1.60;
}

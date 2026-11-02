using Prediction.Entities;

namespace Prediction.Services;

public sealed record DailyTicketPick(
    long MatchId, string Fixture, string? League, DateTime KickoffUtc,
    TicketCandidate Candidate, double Odds, double BaseOdds,
    double? SourceProbability, int MatchingVotes, int TotalVotes, string Evidence)
{
    public double Agreement => TotalVotes == 0 ? 0 : (double)MatchingVotes / TotalVotes;
}

public sealed record DailyTicketDecision(DailyTicketPick? Pick, string Reason);

public sealed record DailyTicketSelection(
    IReadOnlyList<DailyTicketPick> Picks, IReadOnlyList<string> Rejections,
    int Matched, int Duplicates, int Eligible, int ExcludedLeagues);

public static class DailyTicketSelector
{
    public static DailyTicketDecision Select(Match match, BookmakerMatch offer)
    {
        var snapshots = CalibrationAnalyzer.LatestPerSource(match);
        var consensus = CalibrationAnalyzer.BuildResultConsensus(snapshots);
        if (consensus is not { Pick: "1" or "2" })
            return new(null, "Nema nedvosmislen dneven 1/2 konsenzus.");
        if (consensus.AverageProbability is double sourceProb &&
            (!double.IsFinite(sourceProb) || sourceProb is < 0 or > 100))
            return new(null, "Nevalidna Source Prob. vo dnevnite prognozi.");
        var candidates = TicketMarkets.BuildCandidates(match,
            includeTeamGoals: true, requireProbability: false).ToList();
        var fix = candidates.First(c => c.Market == "1X2");
        double? baseOdds = MaxbetOddsProvider.GetOdds(offer, fix.Market, fix.Pick);
        if (!ValidOdds(baseOdds))
            return new(null, "Nema potvrdena osnovna Maxbet kvota.");

        var goals = TicketMarkets.BinarySignal(match, "Goals");
        var btts = TicketMarkets.BinarySignal(match, "BTTS");
        bool over = goals is { Pick: "O2.5" };
        bool bttsYes = btts is { Pick: "YES" };
        bool highOdds = baseOdds > TicketSelector.MaxFixOdds;
        bool Allowed(TicketCandidate c)
        {
            if (highOdds)
                return (c.Market is "Goals" or "HalfGoals" && over) ||
                    (c.Market == "BTTS" && bttsYes) ||
                    (c.Pick == "BTTS & 3+" && bttsYes && over);
            if (baseOdds < TicketSelector.MinBaseOdds && c.Pick == $"{fix.Pick}-{fix.Pick}")
                return true;
            if (baseOdds < TicketSelector.MinBaseOdds && c.Pick == $"{fix.Pick}-{fix.Pick} & 2+")
                return over;
            return c.Market == "Combo" &&
                c.Pick.StartsWith($"{fix.Pick} & ", StringComparison.Ordinal) &&
                (c.Pick == $"{fix.Pick} & T{fix.Pick} 2+" || over);
        }

        TicketCandidate? chosen;
        double? odds;
        if (baseOdds >= TicketSelector.KeepFixOdds && baseOdds <= TicketSelector.MaxFixOdds)
        {
            chosen = fix;
            odds = baseOdds;
        }
        else
        {
            var alternatives = candidates.Where(Allowed)
                .Select(c => (Candidate: c, Odds: MaxbetOddsProvider.GetOdds(offer, c.Market, c.Pick)))
                .Where(x => ValidOdds(x.Odds) && x.Odds >=
                    (x.Candidate.Market == "BTTS" ? TicketSelector.MinBttsOdds : TicketSelector.MinFinalOdds))
                .OrderBy(x => x.Odds)
                .ThenBy(x => x.Candidate.DisplayPick, StringComparer.Ordinal)
                .ToList();
            if (alternatives.Count == 0)
                return new(null, "Nema dozvolen tip so dneven signal i potvrdena kvota nad minimumot.");
            (chosen, odds) = alternatives[0];
        }

        string probability = consensus.AverageProbability is double p ? $"{p:F1}%" : "n/a";
        string BinaryEvidence(TicketCandidate? signal) => signal == null ? "n/a" :
            $"{signal.Pick} {TicketMarkets.SourceAgreement(match, signal).MatchingVotes}/" +
            $"{TicketMarkets.SourceAgreement(match, signal).TotalVotes}";
        string evidence = $"1X2={consensus.Pick} {consensus.MatchingVotes}/{consensus.TotalVotes}; " +
            $"Source Prob.={probability}; {consensus.Level}; " +
            $"BTTS={BinaryEvidence(btts)}; Goals={BinaryEvidence(goals)}";
        return new(new(match.Id, $"{match.HomeTeam} vs {match.AwayTeam}", offer.League,
            offer.KickoffUtc, chosen, odds!.Value, baseOdds!.Value,
            consensus.AverageProbability, consensus.MatchingVotes, consensus.TotalVotes, evidence), "");
    }

    public static DailyTicketSelection Build(
        IReadOnlyList<Match> matches, IReadOnlyList<BookmakerMatch> offers,
        DateOnly date, DateTime nowUtc, int count, bool leagueOnly)
    {
        if (count < 1 || nowUtc.Kind != DateTimeKind.Utc)
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
        foreach (var (match, offer) in unique)
        {
            if (offer.KickoffUtc <= nowUtc)
            {
                rejections.Add($"{match.HomeTeam} vs {match.AwayTeam}: Natprevarot vekje zapocnal.");
                continue;
            }
            var decision = Select(match, offer);
            if (decision.Pick != null)
                eligible.Add(decision.Pick);
            else
                rejections.Add($"{match.HomeTeam} vs {match.AwayTeam}: {decision.Reason}");
        }

        int excluded = leagueOnly ? eligible.Count(p => LeagueTicketSelector.GetPriority(p.League) < 0) : 0;
        var ranked = Rank(eligible.Where(p => !leagueOnly || LeagueTicketSelector.GetPriority(p.League) >= 0));
        IReadOnlyList<DailyTicketPick> picks;
        if (leagueOnly)
        {
            var leagues = ranked.GroupBy(p => LeagueTicketSelector.GetPriority(p.League))
                .OrderBy(g => g.Key).Select(g => g.ToList()).ToList();
            // One per league first, then second choices, continuing until the requested count.
            picks = Enumerable.Range(0, leagues.Count == 0 ? 0 : leagues.Max(g => g.Count))
                .SelectMany(round => leagues.Where(g => g.Count > round).Select(g => g[round]))
                .Take(count).ToList();
        }
        else
            picks = ranked.Take(count).ToList();
        return new(picks, rejections, unique.Count, paired.Count - unique.Count, eligible.Count, excluded);
    }

    private static IOrderedEnumerable<DailyTicketPick> Rank(IEnumerable<DailyTicketPick> picks) =>
        picks.OrderByDescending(p => p.SourceProbability.HasValue)
            .ThenByDescending(p => p.SourceProbability)
            .ThenByDescending(p => p.Agreement)
            .ThenByDescending(p => p.MatchingVotes)
            .ThenBy(p => p.Odds)
            .ThenBy(p => p.Fixture, StringComparer.Ordinal);

    private static bool ValidOdds(double? odds) => odds.HasValue && double.IsFinite(odds.Value) && odds > 1;
}

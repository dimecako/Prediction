using System.Globalization;
using System.Text;

namespace Prediction.Services;

public enum LeagueEligibility
{
    Missing,
    IntentionallyExcluded,
    Unrecognized,
    Eligible
}

public sealed record LeagueDiagnosis(string League, LeagueEligibility Eligibility, int Count);

public static class LeagueTicketSelector
{
    private static readonly string[][] AllowedLeagueAliases =
    [
        ["ENGLAND 1", "ENGLAND PREMIER", "PREMIER LEAGUE"],
        ["FRANCE 1", "FRANCE LIGUE 1", "LIGUE 1"],
        ["GERMANY 1", "GERMANY BUNDESLIGA", "BUNDESLIGA"],
        ["ITALY 1", "ITALY SERIE A", "SERIE A"],
        ["SPAIN 1", "SPAIN LALIGA", "SPAIN LA LIGA", "LA LIGA", "LALIGA"],
        ["BELGIUM 1", "BELGIUM JUPILER PRO LEAGUE", "JUPILER PRO LEAGUE"],
        ["NETHERLANDS 1", "NETHERLANDS EREDIVISIE", "EREDIVISIE"],
        ["PORTUGAL 1", "PORTUGAL LIGA PORTUGAL", "LIGA PORTUGAL"],
        ["TURKEY 1", "TURKEY SUPER LIG", "SUPER LIG"],
        ["SCOTLAND 1", "SCOTLAND PREMIERSHIP", "SCOTTISH PREMIERSHIP"],
        ["SWITZERLAND 1", "SWITZERLAND SUPER LEAGUE", "SWISS SUPER LEAGUE"],
        ["AUSTRIA 1", "AUSTRIA BUNDESLIGA", "AUSTRIAN BUNDESLIGA"],
        ["POLAND 1", "POLAND EKSTRAKLASA", "EKSTRAKLASA"],
        ["SWEDEN 1", "SWEDEN ALLSVENSKAN", "ALLSVENSKAN"],
        ["SLOVAKIA 1", "SLOVAKIA NIKE LIGA", "NIKE LIGA"],
        ["SLOVENIA 1", "SLOVENIA PRVA LIGA", "PRVA LIGA"],
        ["RUSSIA 1", "RUSSIA PREMIER LEAGUE", "RUSSIAN PREMIER LEAGUE"],
        ["ROMANIA 1", "ROMANIA SUPERLIGA", "SUPERLIGA"],
        ["DENMARK 2", "DENMARK 1ST DIVISION", "DANISH 1ST DIVISION"],
        ["CZECH REPUBLIC 1", "CZECHIA 1", "CZECH 1", "CHANCE LIGA"],
        ["FRANCE 2", "FRANCE LIGUE 2", "LIGUE 2"],
        ["ENGLAND 2", "ENGLAND CHAMPIONSHIP", "CHAMPIONSHIP"],
        ["GERMANY 2", "GERMANY BUNDESLIGA 2", "BUNDESLIGA 2"],
        ["ITALY 2", "ITALY SERIE B", "SERIE B"],
        ["SPAIN 2", "SPAIN LALIGA 2", "SPAIN LA LIGA 2", "LA LIGA 2", "LALIGA 2"],
        ["AUSTRIA 2", "AUSTRIA 2 LIGA", "2 LIGA AUSTRIA"],
        ["BULGARIA 1", "BULGARIA FIRST LEAGUE", "BULGARIA FIRST PROFESSIONAL LEAGUE"],
        ["CHILE LIGA DE PRIMERA", "LIGA DE PRIMERA", "CHILE PRIMERA DIVISION"],
        ["CYPRUS", "CYPRUS 1", "CYPRUS FIRST DIVISION", "CYPRUS 1ST DIVISION"],
        ["GREECE SUPER LEAGUE", "SUPER LEAGUE GREECE", "GREECE 1"],
        ["HUNGARY NB1", "HUNGARY NB I", "HUNGARY OTP BANK LIGA", "NB1"],
        ["NETHERLANDS EERSTE", "NETHERLANDS EERSTE DIVISIE", "NETHERLANDS 2", "EERSTE DIVISIE"],
        ["NORWAY ELITESERIEN", "ELITESERIEN"],
        ["PORTUGAL LIGA PORTUGAL 2", "PORTUGAL 2", "LIGA PORTUGAL 2"],
        ["SCOTLAND CHAMPIONSHIP", "SCOTTISH CHAMPIONSHIP", "SCOTLAND CHAMPIONSHIPS", "SCOTLAND 2"],
        ["SWITZERLAND CHALLENGE LEAGUE", "SWISS CHALLENGE LEAGUE", "CHALLENGE LEAGUE"],
        ["USA MLS", "UNITED STATES MLS", "MAJOR LEAGUE SOCCER", "MLS"],
        ["DENMARK SUPERLIGA", "DANISH SUPERLIGA", "SUPERLIGA DENMARK"],
        ["UEFA CHAMPIONS LEAGUE", "CHAMPIONS LEAGUE"],
        ["UEFA EUROPA LEAGUE", "EUROPA LEAGUE"],
        ["UEFA CONFERENCE LEAGUE", "UEFA EUROPA CONFERENCE LEAGUE", "CONFERENCE LEAGUE"]
    ];

    private static readonly Dictionary<string, string> LeagueIdentityByAlias =
        AllowedLeagueAliases.SelectMany(aliases =>
            aliases.Select(alias => (Alias: Normalize(alias), Identity: Normalize(aliases[0])))
        ).ToDictionary(x => x.Alias, x => x.Identity, StringComparer.Ordinal);

    // Exact competition identities only: a substring such as "cup" or "U19" is not evidence.
    private static readonly HashSet<string> ExcludedCompetitions = new(
        new[]
        {
            "UEFA YOUTH LEAGUE", "UEFA SUPER CUP",
            "FA CUP", "ENGLAND FA CUP", "EFL CUP", "ENGLAND EFL CUP",
            "COPA DEL REY", "SPAIN COPA DEL REY",
            "DFB POKAL", "GERMANY DFB POKAL",
            "COPPA ITALIA", "ITALY COPPA ITALIA",
            "COUPE DE FRANCE", "FRANCE COUPE DE FRANCE"
        }.Select(Normalize), StringComparer.Ordinal);

    public static bool IsAllowedLeague(string? league) =>
        GetAllowedLeagueIdentity(league) != null;

    public static string? GetAllowedLeagueIdentity(string? league) =>
        !string.IsNullOrWhiteSpace(league)
            ? LeagueIdentityByAlias.GetValueOrDefault(Normalize(league))
            : null;

    public static string? GetCompetitionIdentity(string? league)
    {
        if (string.IsNullOrWhiteSpace(league))
            return null;
        string normalized = Normalize(league);
        return LeagueIdentityByAlias.TryGetValue(normalized, out string? identity)
            ? identity
            : $"OTHER|{normalized}";
    }

    public static LeagueEligibility Classify(string? league)
    {
        if (string.IsNullOrWhiteSpace(league))
            return LeagueEligibility.Missing;
        if (IsAllowedLeague(league))
            return LeagueEligibility.Eligible;
        return ExcludedCompetitions.Contains(Normalize(league))
            ? LeagueEligibility.IntentionallyExcluded
            : LeagueEligibility.Unrecognized;
    }

    public static IReadOnlyList<LeagueDiagnosis> Diagnose(IEnumerable<string?> leagues) =>
        leagues.GroupBy(league => (
                League: string.IsNullOrWhiteSpace(league) ? "(missing)" :
                    string.Concat(league.Trim().Select(c => char.IsControl(c) ||
                        char.GetUnicodeCategory(c) is UnicodeCategory.LineSeparator or
                            UnicodeCategory.ParagraphSeparator or UnicodeCategory.Format ? ' ' : c)),
                Eligibility: Classify(league)))
            .Select(group => new LeagueDiagnosis(group.Key.League, group.Key.Eligibility, group.Count()))
            .OrderBy(x => x.Eligibility)
            .ThenBy(x => x.League, StringComparer.Ordinal)
            .ToList();

    public static bool IsNoSelection(
        bool leagueMode, int selectedCount, IEnumerable<string?> qualifiedLeagues, bool hasRequiredData,
        bool evaluatedValidFixture = false)
    {
        if (!leagueMode || selectedCount != 0 || !hasRequiredData)
            return false;
        var leagues = qualifiedLeagues.ToList();
        return (leagues.Count > 0 || evaluatedValidFixture) && leagues.All(league =>
            Classify(league) is LeagueEligibility.IntentionallyExcluded or LeagueEligibility.Unrecognized);
    }

    public static IReadOnlyList<T> Select<T>(
        IEnumerable<T> candidates,
        int count,
        Func<T, string?> getLeague,
        Func<T, double> getAgreementShare,
        Func<T, int> getAgreementVotes,
        Func<T, double> getSafety,
        Func<T, double> getMlProbability,
        Func<T, double> getAccuracy,
        Func<T, double> getSignalPriority,
        bool safetyFirst = false)
    {
        if (count <= 0)
            return [];

        var ranked = candidates.Where(candidate => IsAllowedLeague(getLeague(candidate)))
            .OrderByDescending(x => safetyFirst ? getSafety(x) : getAgreementShare(x))
            .ThenByDescending(x => safetyFirst ? getMlProbability(x) : 0)
            .ThenByDescending(getAgreementShare)
            .ThenByDescending(getAgreementVotes)
            .ThenByDescending(getSafety)
            .ThenByDescending(getMlProbability)
            .ThenByDescending(getAccuracy)
            .ThenByDescending(getSignalPriority);
        return SelectAllowedLeagues(ranked, count, getLeague);
    }

    public static IReadOnlyList<T> SelectAllowedLeagues<T>(
        IEnumerable<T> rankedCandidates, int count, Func<T, string?> getLeague)
    {
        if (count <= 0)
            return [];

        var candidates = rankedCandidates
            .Select((candidate, index) => (Candidate: candidate, Index: index,
                League: getLeague(candidate)))
            .Where(x => IsAllowedLeague(x.League))
            .ToList();
        var byLeague = candidates
            .GroupBy(x => Normalize(x.League!))
            .OrderBy(group => group.Min(x => x.Index))
            .Select(group => group.OrderBy(x => x.Index).Select(x => x.Candidate).ToList())
            .ToList();
        return SelectRounds(byLeague, count);
    }

    private static List<T> SelectRounds<T>(IReadOnlyList<List<T>> leagues, int count)
    {
        var selected = new List<T>(count);
        int rounds = leagues.Count == 0 ? 0 : leagues.Max(league => league.Count);
        for (int round = 0; round < rounds && selected.Count < count; round++)
        {
            foreach (var league in leagues)
            {
                if (selected.Count == count)
                    break;
                if (league.Count > round)
                    selected.Add(league[round]);
            }
        }
        return selected;
    }

    private static string Normalize(string value)
    {
        string decomposed = value.Normalize(NormalizationForm.FormD);
        var normalized = new StringBuilder(decomposed.Length);
        foreach (char character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;
            normalized.Append(char.IsLetterOrDigit(character) ? char.ToUpperInvariant(character) : ' ');
        }

        return string.Join(' ', normalized.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

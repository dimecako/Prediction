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
        ["ENGLAND PREMIER LEAGUE", "ENGLAND 1", "ENGLAND PREMIER"],
        ["FRANCE LIGUE 1", "FRANCE 1"],
        ["GERMANY BUNDESLIGA", "GERMANY 1"],
        ["ITALY SERIE A", "ITALY 1"],
        ["SPAIN LA LIGA", "SPAIN 1", "SPAIN LALIGA"],
        ["BELGIUM JUPILER PRO LEAGUE", "BELGIUM 1"],
        ["NETHERLANDS EREDIVISIE", "NETHERLANDS 1"],
        ["PORTUGAL LIGA PORTUGAL", "PORTUGAL 1"],
        ["TURKEY SUPER LIG", "TURKEY 1"],
        ["SCOTLAND PREMIERSHIP", "SCOTLAND 1", "SCOTTISH PREMIERSHIP"],
        ["SWITZERLAND SUPER LEAGUE", "SWITZERLAND 1", "SWISS SUPER LEAGUE"],
        ["AUSTRIA BUNDESLIGA", "AUSTRIA 1", "AUSTRIAN BUNDESLIGA"],
        ["POLAND EKSTRAKLASA", "POLAND 1"],
        ["SWEDEN ALLSVENSKAN", "SWEDEN 1"],
        ["SLOVAKIA NIKE LIGA", "SLOVAKIA 1"],
        ["SLOVENIA PRVA LIGA", "SLOVENIA 1"],
        ["RUSSIA PREMIER LEAGUE", "RUSSIA 1", "RUSSIAN PREMIER LEAGUE"],
        ["ROMANIA SUPERLIGA", "ROMANIA 1"],
        ["DENMARK 1ST DIVISION", "DENMARK 2", "DANISH 1ST DIVISION"],
        ["CZECH REPUBLIC CHANCE LIGA", "CZECH REPUBLIC 1", "CZECHIA 1", "CZECH 1"],
        ["FRANCE LIGUE 2", "FRANCE 2"],
        ["ENGLAND CHAMPIONSHIP", "ENGLAND 2"],
        ["GERMANY BUNDESLIGA 2", "GERMANY 2", "GERMANY 2 BUNDESLIGA"],
        ["ITALY SERIE B", "ITALY 2"],
        ["SPAIN LA LIGA 2", "SPAIN 2", "SPAIN LALIGA 2"],
        ["AUSTRIA 2 LIGA", "AUSTRIA 2", "2 LIGA AUSTRIA"],
        ["BULGARIA FIRST LEAGUE", "BULGARIA 1", "BULGARIA FIRST PROFESSIONAL LEAGUE"],
        ["CHILE LIGA DE PRIMERA", "CHILE PRIMERA DIVISION"],
        ["CYPRUS FIRST DIVISION", "CYPRUS 1", "CYPRUS 1ST DIVISION"],
        ["GREECE SUPER LEAGUE", "SUPER LEAGUE GREECE", "GREECE 1"],
        ["HUNGARY NB1", "HUNGARY NB I", "HUNGARY OTP BANK LIGA"],
        ["NETHERLANDS EERSTE DIVISIE", "NETHERLANDS EERSTE", "NETHERLANDS 2"],
        ["NORWAY ELITESERIEN"],
        ["PORTUGAL LIGA PORTUGAL 2", "PORTUGAL 2"],
        ["SCOTLAND CHAMPIONSHIP", "SCOTTISH CHAMPIONSHIP", "SCOTLAND CHAMPIONSHIPS", "SCOTLAND 2"],
        ["SWITZERLAND CHALLENGE LEAGUE", "SWISS CHALLENGE LEAGUE"],
        ["USA MLS", "UNITED STATES MLS", "USA MAJOR LEAGUE SOCCER"],
        ["DENMARK SUPERLIGA", "DANISH SUPERLIGA", "SUPERLIGA DENMARK"],
        ["UEFA CHAMPIONS LEAGUE"],
        ["UEFA EUROPA LEAGUE"],
        ["UEFA CONFERENCE LEAGUE", "UEFA EUROPA CONFERENCE LEAGUE"]
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

    public static string? QualifySourceLeague(string? country, string? league)
    {
        if (string.IsNullOrWhiteSpace(league))
            return null;
        if (string.IsNullOrWhiteSpace(country))
            return league.Trim();

        string region = Normalize(country) switch
        {
            "EUROPE" => "UEFA",
            "TURKIYE" => "TURKEY",
            "CZECHIA" => "CZECH REPUBLIC",
            "UNITED STATES" or "UNITED STATES OF AMERICA" => "USA",
            var value => value
        };
        string name = Normalize(league);
        string? identity = GetAllowedLeagueIdentity(name);
        if (name.StartsWith(region + " ", StringComparison.Ordinal) ||
            identity?.StartsWith(region + " ", StringComparison.Ordinal) == true)
            return league.Trim();

        return $"{region} {league.Trim()}";
    }

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
            .GroupBy(x => GetAllowedLeagueIdentity(x.League)!)
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

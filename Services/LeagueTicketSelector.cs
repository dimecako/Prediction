using System.Globalization;
using System.Text;

namespace Prediction.Services;

public static class LeagueTicketSelector
{
    private static readonly string[][] LeagueAliases =
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
        ["SPAIN 2", "SPAIN LALIGA 2", "SPAIN LA LIGA 2", "LA LIGA 2", "LALIGA 2"]
    ];

    private static readonly Dictionary<string, int> PriorityByAlias =
        LeagueAliases
            .SelectMany((aliases, priority) => aliases.Select(alias => (Normalize(alias), priority)))
            .ToDictionary(x => x.Item1, x => x.priority, StringComparer.OrdinalIgnoreCase);

    public static int GetPriority(string? league) =>
        string.IsNullOrWhiteSpace(league) ? -1 :
        PriorityByAlias.GetValueOrDefault(Normalize(league), -1);

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

        var byLeague = candidates
            .Select(candidate => (Candidate: candidate, Priority: GetPriority(getLeague(candidate))))
            .Where(x => x.Priority >= 0)
            .GroupBy(x => x.Priority)
            .OrderBy(x => x.Key)
            .Select(group => group
                .Select(x => x.Candidate)
                .OrderByDescending(x => safetyFirst ? getSafety(x) : getAgreementShare(x))
                .ThenByDescending(x => safetyFirst ? getMlProbability(x) : 0)
                .ThenByDescending(getAgreementShare)
                .ThenByDescending(getAgreementVotes)
                .ThenByDescending(getSafety)
                .ThenByDescending(getMlProbability)
                .ThenByDescending(getAccuracy)
                .ThenByDescending(getSignalPriority)
                .ToList())
            .ToList();

        var selected = new List<T>(Math.Min(count, LeagueAliases.Length * 2));

        foreach (var league in byLeague)
        {
            if (selected.Count == count)
                return selected;
            if (league.Count > 0)
                selected.Add(league[0]);
        }

        if (selected.Count < count)
        {
            foreach (var league in byLeague)
            {
                if (selected.Count == count)
                    break;
                if (league.Count > 1)
                    selected.Add(league[1]);
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

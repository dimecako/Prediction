using System.Text.RegularExpressions;

namespace Prediction.Services;

public static class TeamNameMatcher
{
    private static readonly HashSet<string> Noise = new(StringComparer.Ordinal)
    {
        "fc", "cf", "sc", "ac", "as", "fk", "sk", "cd", "ud", "club", "the", "de", "la", "el", "afc", "bk", "if"
    };

    // Youth, women and reserve sides must match exactly, otherwise "Slavia" would match "Slavia U19".
    private static readonly Regex Marker = new(
        @"^(u\d{2}|u\d{2}s|w|women|ii|iii|b|c|res|reserves|youth)$",
        RegexOptions.Compiled);

    public static bool SameFixture(
        string home1, string away1,
        string home2, string away2) =>
        Similar(home1, home2) && Similar(away1, away2);

    public static bool Similar(string a, string b)
    {
        string na = MatchIdentity.NormalizeTeam(a);
        string nb = MatchIdentity.NormalizeTeam(b);

        if (na.Length == 0 || nb.Length == 0)
            return false;

        if (na == nb)
            return true;

        var ta = Tokens(na);
        var tb = Tokens(nb);

        if (ta.Count == 0 || tb.Count == 0)
            return false;

        (ta, tb) = (ExpandInitials(ta, tb), ExpandInitials(tb, ta));

        var ma = ta.Where(x => Marker.IsMatch(x)).OrderBy(x => x).ToList();
        var mb = tb.Where(x => Marker.IsMatch(x)).OrderBy(x => x).ToList();
        if (!ma.SequenceEqual(mb))
            return false;

        ta = ta.Where(x => !Marker.IsMatch(x)).ToList();
        tb = tb.Where(x => !Marker.IsMatch(x)).ToList();

        if (ta.Count == 0 || tb.Count == 0)
            return false;

        var shorter = ta.Count <= tb.Count ? ta : tb;
        var longer = ta.Count <= tb.Count ? tb : ta;

        if (shorter.All(s => longer.Any(l => TokenMatch(s, l))))
            return true;

        string ja = string.Concat(ta);
        string jb = string.Concat(tb);
        return ja.Length >= 5 && jb.Length >= 5 && Ratio(ja, jb) >= 0.85;
    }

    private static List<string> Tokens(string normalized) =>
        MergeInitialRuns(normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(x => x switch { "utd" => "united", "st" => "saint", _ => x })
            .Where(x => !Noise.Contains(x))
            .ToList();

    // "U.A.N.L." arrives as separate letters; three or more in a row form one acronym token.
    private static IEnumerable<string> MergeInitialRuns(IEnumerable<string> tokens)
    {
        var run = new List<string>();
        foreach (string token in tokens.Append(string.Empty))
        {
            if (token.Length == 1)
            {
                run.Add(token);
                continue;
            }

            if (run.Count >= 3)
                yield return string.Concat(run);
            else
                foreach (string initial in run)
                    yield return initial;
            run.Clear();

            if (token.Length > 0)
                yield return token;
        }
    }

    // A lone letter ("Bayern M.", "Guadalajara C.") stands for a word of the other name starting with it.
    // It is replaced only when such a word exists, so "Real Madrid C" never equals "Real Madrid".
    private static List<string> ExpandInitials(List<string> tokens, List<string> other) =>
        tokens.Select(t => t.Length == 1 && char.IsLetter(t[0]) && tokens.Count > 1
                ? other.FirstOrDefault(o => o.Length > 1 && o[0] == t[0] && !tokens.Contains(o)) ?? t
                : t)
            .ToList();

    private static bool TokenMatch(string a, string b)
    {
        if (a == b)
            return true;

        // Abbreviations such as "dep" / "deportivo" or "atl" / "atletico".
        string shortToken = a.Length <= b.Length ? a : b;
        string longToken = a.Length <= b.Length ? b : a;
        return (shortToken.Length >= 3 && longToken.StartsWith(shortToken, StringComparison.Ordinal)) ||
            // Glued club prefixes such as "kusatsu" in "thespakusatsu".
            (shortToken.Length >= 6 && longToken.EndsWith(shortToken, StringComparison.Ordinal));
    }

    private static double Ratio(string a, string b)
    {
        int[] previous = Enumerable.Range(0, b.Length + 1).ToArray();

        for (int i = 1; i <= a.Length; i++)
        {
            int[] current = new int[b.Length + 1];
            current[0] = i;

            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            previous = current;
        }

        return 1.0 - (double)previous[b.Length] / Math.Max(a.Length, b.Length);
    }
}

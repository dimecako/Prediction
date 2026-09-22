using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using System.Globalization;

public partial class ProductionConsensusAggregator
{
     private string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";

        name = HtmlEntity.DeEntitize(name);

        name = RemoveDiacritics(name)
            .ToLowerInvariant()
            .Trim();

        // punctuation
        name = name
            .Replace("'", "")
            .Replace("’", "")
            .Replace(".", "")
            .Replace(",", "")
            .Replace("-", " ")
            .Replace("_", " ")
            .Replace("&", " and ");

        name = Regex.Replace(name, @"\s+", " ").Trim();

        // -------------------------------------------------
        // KNOWN TEAM ALIASES - BEFORE generic replacements
        // -------------------------------------------------

        var aliases = new Dictionary<string, string>
        {
            { "man utd", "manchester united" },
            { "man united", "manchester united" },
            { "man city", "manchester city" },

            { "wolves", "wolverhampton" },
            { "wolverhampton wanderers", "wolverhampton" },

            { "heart of midlothian", "hearts" },

            { "grasshopper club zurich", "grasshoppers" },
            { "grasshopper club", "grasshoppers" },

            { "queen of the south", "queen of south" },

            { "sparta prague", "sparta praha" },
            { "slavia prague", "slavia praha" },

            { "olympiakos piraeus", "olympiacos" },
            { "olympiakos", "olympiacos" },

            { "jagiellonia bialystok", "jagiellonia" },

            { "iraklis gerolakkou", "iraklis yerolakkos" },

            { "nk mladost zdralovi", "mladost zdralovi" },

            { "sarpsborg 08 ff", "sarpsborg 08" },

            { "cambrian and clydach", "cambrian clydach" },

            { "puskas academy", "puskas akademia" },

            { "rapid vienna", "rapid wien" },

            { "ud logrones", "logrones" },
            { "logrones cf", "logrones" },

            { "nk sesvete", "radnik sesvete" },

            { "lyngby bk", "lyngby" },

            { "rkc waalwijk", "waalwijk" },

            { "as nancy", "nancy" },

            { "queens park fc", "queens park" },

            { "ayr utd", "ayr united" },

            { "az", "az alkmaar" },

            { "psg", "paris saint germain" },

            { "inter milan", "inter" },
            { "internazionale", "inter" },

            // Current cross-source aliases
            { "sheffield wed", "sheffield wednesday" },

            { "bradford city", "bradford" },
            { "crewe alexandra", "crewe" },
            { "luton town", "luton" },
            { "leicester city", "leicester" },

            { "juventus sp", "juventus" },
            { "uniao sao joao sp", "uniao sao joao" },

            { "skchf sevastopol", "sevastopol" },

            { "csm resita", "resita" },
            { "csm scolar resita", "resita" },
            { "fcm resita", "resita" },
            { "celtic b", "celtic youth" },
            { "celtic fc youth", "celtic youth" },

            { "hibernian b", "hibernian youth" },

            { "newcastle united b", "newcastle united youth" },
            { "aston villa b", "aston villa youth" },
            { "fulham b", "fulham youth" },
            { "ipswich town b", "ipswich town youth" },
            { "ipswich youth", "ipswich town youth" },
            { "newcastle youth", "newcastle united youth" }
        };

        if (aliases.TryGetValue(name, out string alias))
            name = alias;

        // -------------------------------------------------
        // Generic normalization
        // -------------------------------------------------

        name = Regex.Replace(name, @"\butd\b", "united");
        name = Regex.Replace(name, @"\bst\b", "saint");

        // Women
        name = Regex.Replace(
            name,
            @"\b(women|woman)\b",
            "w",
            RegexOptions.IgnoreCase);

        name = Regex.Replace(
            name,
            @"\(\s*w\s*\)",
            " w",
            RegexOptions.IgnoreCase);

        // II -> B
        name = Regex.Replace(name, @"\bii\b", "b");

        // -------------------------------------------------
        // Reserve / youth team normalization
        // -------------------------------------------------

        name = Regex.Replace(
            name,
            @"\bu23s?\b",
            "youth");

        name = Regex.Replace(
            name,
            @"\bu21s?\b",
            "youth");

        // Club prefixes/suffixes
        name = Regex.Replace(
            name,
            @"\b(fc|fk|afc|sc|cf|ac|sv)\b",
            " ");

        // Known transliteration differences
        name = name
            .Replace("skoevde", "skovde")
            .Replace("kaspiy", "kaspij");

        name = Regex.Replace(
            name,
            @"\band\b",
            " ");

        name = Regex.Replace(
            name,
            @"^(tsv|msv|deportivo|sporting|club|real)\s+",
            "");

        name = Regex.Replace(
            name,
            @"\s+(bgc)$",
            "");

        name = Regex.Replace(name, @"\s+", " ").Trim();

        // Run aliases again because generic normalization above
        // can produce a value that itself has a canonical alias.
        // Example:
        // Celtic II -> celtic b -> celtic youth
        if (aliases.TryGetValue(name, out string finalAlias))
            name = finalAlias;

        return name;
    }

    private string RemoveDiacritics(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        string normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();

        foreach (char c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    
    private double GetSimilarity(string s, string t)
    {
        if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(t)) return 0;
        int n = s.Length, m = t.Length;
        int[,] d = new int[n + 1, m + 1];
        for (int i = 0; i <= n; d[i, 0] = i++) ;
        for (int j = 0; j <= m; d[0, j] = j++) ;
        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }
        return 1.0 - ((double)d[n, m] / Math.Max(s.Length, t.Length));
    }

    private double GetTokenSimilarity(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) ||
            string.IsNullOrWhiteSpace(b))
        {
            return 0;
        }

        var tokensA = a
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Distinct()
            .ToList();

        var tokensB = b
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Distinct()
            .ToList();

        if (tokensA.Count == 0 || tokensB.Count == 0)
            return 0;

        int intersection =
            tokensA.Intersect(tokensB).Count();

        int union =
            tokensA.Union(tokensB).Count();

        return union == 0
            ? 0
            : (double)intersection / union;
    }

    private bool TeamsMatchDirect(
    string h1,
    string a1,
    string h2,
    string a2)
    {
        string nH1 = NormalizeName(h1);
        string nA1 = NormalizeName(a1);
        string nH2 = NormalizeName(h2);
        string nA2 = NormalizeName(a2);

        // 1. Perfect normalized match
        if (nH1 == nH2 && nA1 == nA2)
            return true;

        double homeLev =
            GetSimilarity(nH1, nH2);

        double awayLev =
            GetSimilarity(nA1, nA2);

        double homeToken =
            GetTokenSimilarity(nH1, nH2);

        double awayToken =
            GetTokenSimilarity(nA1, nA2);

        // ---------------------------------------
        // Exact one side + strong other side
        // ---------------------------------------

        bool exactHome = nH1 == nH2;
        bool exactAway = nA1 == nA2;

        if (exactHome &&
            (awayLev >= 0.80 || awayToken >= 0.50))
        {
            return true;
        }

        if (exactAway &&
            (homeLev >= 0.80 || homeToken >= 0.50))
        {
            return true;
        }

        // ---------------------------------------
        // Both names extremely similar
        // ---------------------------------------

        if (homeLev >= 0.90 &&
            awayLev >= 0.90)
        {
            return true;
        }

        // ---------------------------------------
        // Composite score
        // ---------------------------------------

        double homeScore =
            Math.Max(homeLev, homeToken);

        double awayScore =
            Math.Max(awayLev, awayToken);

        double fixtureScore =
            (homeScore + awayScore) / 2.0;

        if (fixtureScore >= 0.88 &&
            homeScore >= 0.78 &&
            awayScore >= 0.78)
        {
            return true;
        }

        return false;
    }

     private string GetOrCreateMatch(string home, string away)
    {
        string normHome = NormalizeName(home);
        string normAway = NormalizeName(away);

        foreach (var kvp in unifiedDb)
        {
            var parts = kvp.Key.Split('|');
            if (parts.Length == 2 && TeamsMatchDirect(normHome, normAway, parts[0], parts[1]))
            {
                return kvp.Key;
            }
        }

        string searchKey = $"{normHome}|{normAway}";
        unifiedDb.GetOrAdd(searchKey, _ => new UnifiedMatch { HomeOrig = home, AwayOrig = away });
        return searchKey;
    }

    private void MergeMatch(SiteMatch siteMatch)
    {
        if (siteMatch == null) return;
        
        string dbKey = GetOrCreateMatch(siteMatch.HomeTeam, siteMatch.AwayTeam);
        if (unifiedDb.TryGetValue(dbKey, out var match))
        {
            match.Sources[siteMatch.SiteName] = new MatchSourceData
            {
                Tip = siteMatch.Tip,
                Prob = siteMatch.Prob,
                Score = siteMatch.Score,
                Btts = siteMatch.Btts,
                BttsMarket = siteMatch.BttsMarket,
                GoalsMarket = siteMatch.GoalsMarket
            };
        }
    }
}
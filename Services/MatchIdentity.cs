using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Prediction.Services;

public static class MatchIdentity
{
    public static string BuildExternalMatchId(
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

    public static string NormalizeTeam(string value)
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
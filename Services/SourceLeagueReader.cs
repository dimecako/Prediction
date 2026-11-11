using Newtonsoft.Json.Linq;

namespace Prediction.Services;

public static class SourceLeagueReader
{
    public static string? Read(JToken group, params string[] nameFields)
    {
        string? name = ReadText(nameFields.Select(field => group[field]));
        string? country = ReadText(
            [group["countryName"], group["country_name"], group["country"] is JObject countryObject
                ? countryObject["name"] : group["country"]]);
        return LeagueTicketSelector.QualifySourceLeague(country, name);
    }

    private static string? ReadText(IEnumerable<JToken?> values) =>
        values.Where(value => value?.Type == JTokenType.String)
            .Select(value => value!.Value<string>()?.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

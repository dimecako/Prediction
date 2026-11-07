using System.Text.Json;
using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class ResultMatchAiSuggesterTests
{
    private static readonly HashSet<long> CandidateIds = [30611, 30758];

    [Fact]
    public void ParsesSuggestionOnlyWhenIdIsOneOfTheCandidates()
    {
        var suggestion = ResultMatchAiSuggester.ParseResponse(
            """{"selected_match_id":30758,"reason":"Names and competition align."}""",
            CandidateIds);

        Assert.Equal(30758, suggestion.MatchId);
        Assert.Equal("Names and competition align.", suggestion.Reason);
    }

    [Fact]
    public void AllowsExplicitAbstention()
    {
        var suggestion = ResultMatchAiSuggester.ParseResponse(
            """{"selected_match_id":null,"reason":"The evidence is ambiguous."}""",
            CandidateIds);

        Assert.Null(suggestion.MatchId);
        Assert.Equal("The evidence is ambiguous.", suggestion.Reason);
    }

    [Fact]
    public void RejectsIdsOutsideTheProvidedCandidateList()
    {
        Assert.Throws<InvalidDataException>(() => ResultMatchAiSuggester.ParseResponse(
            """{"selected_match_id":1,"reason":"Looks plausible."}""",
            CandidateIds));
    }

    [Fact]
    public void RejectsMalformedOrIncompleteResponses()
    {
        Assert.ThrowsAny<JsonException>(() => ResultMatchAiSuggester.ParseResponse("not json", CandidateIds));
        Assert.Throws<InvalidDataException>(() => ResultMatchAiSuggester.ParseResponse(
            """{"selected_match_id":null,"reason":" "}""",
            CandidateIds));
    }

    [Fact]
    public void BoundsModelExplanation()
    {
        string reason = "a".PadRight(400, 'b');
        var suggestion = ResultMatchAiSuggester.ParseResponse(
            JsonSerializer.Serialize(new { selected_match_id = 30611, reason }),
            CandidateIds);

        Assert.Equal(300, suggestion.Reason.Length);
    }
}

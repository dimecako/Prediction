using HtmlAgilityPack;
using Prediction.Entities;
using Prediction.Services;
using Prediction.Web;
using Xunit;

namespace Prediction.Tests;

public sealed class PredictionConsensusTests
{
    [Fact]
    public void AverageUsesOnlyAgreeingSourcesWithValidProbabilities()
    {
        var fixture = Fixture(1, 11, 11);
        fixture.PredictionSnapshots.ElementAt(0).Confidence = .60;
        fixture.PredictionSnapshots.ElementAt(1).Confidence = .55;
        var row = PredictionConsensus.Build(fixture);
        Assert.Equal(1, row.Result.Agreement);
        Assert.Equal(.575, row.Result.AverageProbability!.Value, 10);
        Assert.Equal(2, row.Result.Priced);
        Assert.Equal(11, row.Result.Matching);
        Assert.Null(row.Btts.AverageProbability);
        Assert.Null(row.Goals.AverageProbability);
    }

    [Fact]
    public void OpposingStaleAndInvalidProbabilitiesAreExcludedAndZeroIsValid()
    {
        var fixture = Fixture(1, 5, 6);
        var votes = fixture.PredictionSnapshots.ToArray();
        votes[0].Confidence = 0;
        votes[1].Confidence = .6;
        votes[2].Confidence = double.NaN;
        votes[3].Confidence = 1.1;
        votes[4].Confidence = double.PositiveInfinity;
        votes[5].Confidence = .95;
        fixture.PredictionSnapshots.Add(new()
        {
            Source = votes[0].Source, PredictedResult = "1", Confidence = .99,
            CapturedAtUtc = votes[0].CapturedAtUtc.AddDays(-1)
        });
        var row = PredictionConsensus.Build(fixture);
        Assert.Equal(6, row.Result.Total);
        Assert.Equal(5, row.Result.Matching);
        Assert.Equal(2, row.Result.Priced);
        Assert.Equal(.3, row.Result.AverageProbability!.Value, 10);
    }

    [Fact]
    public void DefaultSortUsesAgreementBeforeVoteCountAndBeforePagination()
    {
        var rows = Enumerable.Range(1, 101).Select(id => Fixture(id, 9, 10)).ToList();
        rows.Add(Fixture(102, 2, 2));
        rows.Add(Fixture(103, 11, 11));
        var ordered = PredictionConsensus.Order(rows);
        Assert.Equal(103, ordered[0].Match.Id);
        Assert.Equal(102, ordered[1].Match.Id);
        Assert.Contains(ordered.Take(100), x => x.Match.Id == 103);
        Assert.Equal(103, ordered.Count);
    }

    [Theory]
    [InlineData("result-probability")]
    [InlineData("btts-agreement")]
    [InlineData("goals-agreement")]
    public void SortSupportsBothDirectionsAndPutsUnknownLast(string key)
    {
        var low = Fixture(1, 2, 3);
        var high = Fixture(2, 3, 3);
        foreach (var vote in low.PredictionSnapshots)
            vote.Confidence = .55;
        foreach (var vote in high.PredictionSnapshots)
            vote.Confidence = .8;
        var unknown = new Match { Id = 3, HomeTeam = "Unknown", AwayTeam = "Away" };
        Assert.Equal(new long[] { 2, 1, 3 }, PredictionConsensus.Order([low, unknown, high], key).Select(x => x.Match.Id));
        Assert.Equal(new long[] { 1, 2, 3 }, PredictionConsensus.Order([low, unknown, high], key, true).Select(x => x.Match.Id));
    }

    [Theory]
    [InlineData("btts-probability")]
    [InlineData("goals-probability")]
    public void MissingMarketProbabilitiesNeverUseResultConfidence(string key)
    {
        var fixture = Fixture(1, 2, 2);
        foreach (var vote in fixture.PredictionSnapshots)
            vote.Confidence = .9;
        var row = Assert.Single(PredictionConsensus.Order([fixture], key));
        Assert.Null(row.Btts.AverageProbability);
        Assert.Null(row.Goals.AverageProbability);
        Assert.Equal(0, row.Btts.Priced);
        Assert.Equal(0, row.Goals.Priced);
    }

    [Theory]
    [InlineData(UiLanguage.English, "Source average", "with probability")]
    [InlineData(UiLanguage.Macedonian, "Просек од извори", "со процент")]
    public void RenderShowsCoverageAndPreservesSortAndLanguage(UiLanguage language, string label, string coverage)
    {
        var fixture = Fixture(1, 11, 11);
        fixture.PredictionSnapshots.First().Confidence = .6;
        fixture.PredictionSnapshots.ElementAt(1).Confidence = .55;
        string html = DatabaseReportPages.Predictions(new(2026, 11, 4), [fixture], 1, 101,
            language, "btts-agreement", true);
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var table = document.DocumentNode.SelectSingleNode("//div[@class='scroll']/table");
        string cell = table.SelectSingleNode("./tr[2]/td[3]").InnerText;
        Assert.Contains("57.5", cell);
        Assert.DoesNotContain("57.5", table.SelectSingleNode("./tr[2]/td[2]").InnerText);
        Assert.Contains(label, table.SelectSingleNode("./tr[1]/th[3]").InnerText);
        Assert.Contains($"2/11 {coverage}", cell);
        Assert.Equal(9, table.SelectNodes("./tr[1]/th").Count);
        Assert.Equal(9, table.SelectNodes("./tr[2]/td").Count);
        foreach (var (column, market) in new[] { (3, "result"), (5, "btts"), (7, "goals") })
        {
            string href = table.SelectSingleNode($"./tr[1]/th[{column}]/a").GetAttributeValue("href", "");
            Assert.Contains($"sort={market}-probability", href);
            Assert.Contains("direction=desc", href);
            Assert.Contains("date=2026-11-04", href);
            Assert.Contains($"lang={(language == UiLanguage.Macedonian ? "mk" : "en")}", href);
        }
        Assert.Contains("sort=btts-agreement&amp;direction=asc", html);
        Assert.Contains("page=2", html);
        Assert.Contains($"lang={(language == UiLanguage.Macedonian ? "mk" : "en")}", html);
        Assert.Contains("sort=goals-probability", html);
    }

    [Theory]
    [InlineData("result-probability", 3)]
    [InlineData("btts-probability", 5)]
    [InlineData("goals-probability", 7)]
    public void SourceAverageColumnHeaderTogglesSortDirection(string sort, int column)
    {
        foreach (bool ascending in new[] { false, true })
        {
            var document = new HtmlDocument();
            document.LoadHtml(DatabaseReportPages.Predictions(new(2026, 11, 4), [Fixture(1, 8, 11)],
                1, 1, sort: sort, ascending: ascending));
            string href = document.DocumentNode.SelectSingleNode(
                $"//div[@class='scroll']/table/tr[1]/th[{column}]/a").GetAttributeValue("href", "");
            Assert.Contains($"sort={sort}", href);
            Assert.Contains($"direction={(ascending ? "desc" : "asc")}", href);
            Assert.Contains("1 stored fixtures", document.DocumentNode.InnerText);
        }
    }

    [Fact]
    public void InvalidSortFailsInsteadOfSilentlyDefaulting()
    {
        Assert.Throws<ArgumentException>(() => PredictionConsensus.Order([], "<script>"));
    }

    private static Match Fixture(long id, int matching, int total) => new()
    {
        Id = id, HomeTeam = $"Home {id}", AwayTeam = "Away",
        PredictionSnapshots = Enumerable.Range(0, total).Select(i => new PredictionSnapshot
        {
            Source = $"Source {i}", PredictedResult = i < matching ? "1" : "2",
            Btts = i < matching ? "YES" : "NO", Goals = i < matching ? "O2.5" : "U2.5",
            CapturedAtUtc = new(2026, 11, 4, 8, 0, 0, DateTimeKind.Utc)
        }).ToList()
    };
}

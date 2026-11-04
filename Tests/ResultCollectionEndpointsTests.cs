using System.Net;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Prediction.Web;
using Xunit;

namespace Prediction.Tests;

public sealed class ResultCollectionEndpointsTests
{
    [Theory]
    [InlineData(ResultCollectionStatus.Succeeded, 200)]
    [InlineData(ResultCollectionStatus.Failed, 502)]
    [InlineData(ResultCollectionStatus.Busy, 409)]
    public async Task CollectionRequiresPostTokenAndValidDate(ResultCollectionStatus outcome, int expectedStatus)
    {
        int calls = 0;
        DateOnly? collectedDate = null;
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAntiforgery();
        await using var app = builder.Build();
        app.MapResultCollection((date, ct) =>
        {
            calls++;
            collectedDate = date;
            return Task.FromResult(outcome);
        });
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        using var client = new HttpClient(new HttpClientHandler { UseCookies = true })
        {
            BaseAddress = new Uri(app.Urls.Single())
        };
        try
        {
            var date = DatabaseReportEndpoints.Today().AddDays(-1);
            string dateText = date.ToString("yyyy-MM-dd");
            string html = await client.GetStringAsync($"/results/collect?date={dateText}&lang=mk");
            Assert.Equal(0, calls);
            Assert.Contains("Собери само резултати", html);
            var document = new HtmlDocument();
            document.LoadHtml(html);
            var input = Assert.IsType<HtmlNode>(document.DocumentNode.SelectSingleNode("//input[@name='__RequestVerificationToken']"));
            string token = WebUtility.HtmlDecode(input.GetAttributeValue("value", ""));
            Assert.NotEmpty(token);

            var missingToken = await client.PostAsync("/results/collect", Form(dateText));
            Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
            var invalid = await client.PostAsync("/results/collect", Form("bad-date", token));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            var future = await client.PostAsync("/results/collect", Form("2999-01-01", token));
            Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,
                (await client.GetAsync("/results/collect?date=bad-date")).StatusCode);
            Assert.Equal(0, calls);

            var result = await client.PostAsync("/results/collect", Form(dateText, token));
            Assert.Equal(expectedStatus, (int)result.StatusCode);
            Assert.Equal(1, calls);
            Assert.Equal(date, collectedDate);
            if (outcome == ResultCollectionStatus.Succeeded)
            {
                string response = await result.Content.ReadAsStringAsync();
                Assert.Contains("Собирањето резултати е завршено.", response);
                Assert.Contains($"date={dateText}&amp;lang=mk", response);
            }
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public void PredictionsCollectionButtonUsesSelectedDateAndLanguageWithoutChangingReadAction()
    {
        var document = new HtmlDocument();
        document.LoadHtml(DatabaseReportPages.Predictions(new(2026, 11, 3), [], 1, 0, UiLanguage.Macedonian));
        var button = Assert.IsType<HtmlNode>(document.DocumentNode.SelectSingleNode("//button[@formaction='/results/collect']"));
        Assert.Equal("Собери само резултати", button.InnerText);
        var form = Assert.IsType<HtmlNode>(button.ParentNode);
        Assert.Equal("get", form.GetAttributeValue("method", ""));
        Assert.Equal("2026-11-03", Assert.IsType<HtmlNode>(form.SelectSingleNode("./label/input[@name='date']")).GetAttributeValue("value", ""));
        Assert.Equal("mk", Assert.IsType<HtmlNode>(form.SelectSingleNode("./input[@name='lang']")).GetAttributeValue("value", ""));
    }

    private static FormUrlEncodedContent Form(string date, string token = "") => new(
        new Dictionary<string, string>
        {
            ["date"] = date, ["lang"] = "mk", ["__RequestVerificationToken"] = token
        });
}

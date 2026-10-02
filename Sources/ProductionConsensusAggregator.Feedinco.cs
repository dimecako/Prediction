using Microsoft.Playwright;

public partial class ProductionConsensusAggregator
{
    public async Task<List<SiteMatch>> ParseFeedincoAsync(IBrowser browser)
    {
        Console.WriteLine("[Feedinco] NOT IMPLEMENTED");

        await Task.CompletedTask;

        return new List<SiteMatch>();
    }
}

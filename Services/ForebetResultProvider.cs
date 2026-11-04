using Prediction.Models;

namespace Prediction.Services;

public sealed class ForebetResultProvider : IMatchResultProvider
{
    public string Name => "Forebet";

    public async Task<IReadOnlyList<ExternalMatchResult>> GetResultsAsync(
        DateOnly date, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await new ProductionConsensusAggregator(date.ToString("yyyy-MM-dd"))
            .GetForebetResultsAsync(date);
    }
}

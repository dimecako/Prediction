using Prediction.Models;

namespace Prediction.Services;

public interface IMatchResultProvider
{
    string Name { get; }

    Task<IReadOnlyList<ExternalMatchResult>> GetResultsAsync(
        DateOnly date,
        CancellationToken cancellationToken = default);
}
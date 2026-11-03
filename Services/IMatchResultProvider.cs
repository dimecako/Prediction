using Prediction.Models;
using Prediction.Entities;

namespace Prediction.Services;

public interface IMatchResultProvider
{
    string Name { get; }

    Task<IReadOnlyList<ExternalMatchResult>> GetResultsAsync(
        DateOnly date,
        CancellationToken cancellationToken = default);
}

public interface ITargetedMatchResultProvider : IMatchResultProvider
{
    Task<IReadOnlyList<ExternalMatchResult>> GetResultsAsync(
        DateOnly date,
        IReadOnlyCollection<Match> matches,
        CancellationToken cancellationToken = default);
}
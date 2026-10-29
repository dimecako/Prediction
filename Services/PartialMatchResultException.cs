using Prediction.Models;

namespace Prediction.Services;

public sealed class PartialMatchResultException(
    IReadOnlyList<ExternalMatchResult> results,
    IReadOnlyList<string> errors)
    : Exception($"{errors.Count} fixture checks failed; retaining {results.Count} verified results.")
{
    public IReadOnlyList<ExternalMatchResult> Results { get; } = results;
    public IReadOnlyList<string> Errors { get; } = errors;
}

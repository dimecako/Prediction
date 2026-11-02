namespace Prediction.Services;

public enum PredictionIdentityConflict
{
    CurrentCapture,
    StoredSourceIds,
    FixtureAndSourceIds
}

public sealed class PredictionIdentityConflictException(
    PredictionIdentityConflict reason, IEnumerable<long> matchIds) : InvalidOperationException
{
    public string Diagnostic { get; } =
        $"prediction identity conflict={reason}; stored match IDs=" +
        string.Join(",", matchIds.Where(id => id > 0).Distinct().Order());
}

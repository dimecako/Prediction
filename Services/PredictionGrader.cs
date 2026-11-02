using Prediction.Entities;

namespace Prediction.Services;

public sealed class PredictionGrade
{
    public bool? CorrectResult { get; init; }
    public bool? CorrectBtts { get; init; }
    public bool? CorrectGoals { get; init; }
}

public static class PredictionGrader
{
    public static PredictionGrade Grade(
        PredictionSnapshot prediction,
        MatchResult result)
    {
        return new PredictionGrade
        {
            CorrectResult =
                GradeValue(
                    prediction.PredictedResult,
                    result.Result),

            CorrectBtts =
                GradeValue(
                    prediction.Btts,
                    result.Btts ? "YES" : "NO"),

            CorrectGoals =
                GradeGoals(
                    prediction.Goals,
                    result.TotalGoals)
        };
    }

    private static bool? GradeValue(
        string? predicted,
        string actual)
    {
        if (IsMissing(predicted))
            return null;

        return string.Equals(
            predicted!.Trim(),
            actual,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool? GradeGoals(
        string? predicted,
        int totalGoals)
    {
        if (IsMissing(predicted))
            return null;

        string value =
            predicted!.Trim().ToUpperInvariant();

        return value switch
        {
            "O3.5" => totalGoals > 3.5,
            "U3.5" => totalGoals < 3.5,

            "O2.5" => totalGoals > 2.5,
            "U2.5" => totalGoals < 2.5,

            "O1.5" => totalGoals > 1.5,
            "U1.5" => totalGoals < 1.5,

            "O0.5" => totalGoals > 0.5,
            "U0.5" => totalGoals < 0.5,

            _ => null
        };
    }

    private static bool IsMissing(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ||
               value.Trim() == "-";
    }
}
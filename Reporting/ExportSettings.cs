namespace Prediction.Reporting;

public static class ExportSettings
{
    public static bool ExcelEnabled => Parse(Environment.GetEnvironmentVariable("Exports__Excel"));

    public static bool Parse(string? value) =>
        string.IsNullOrWhiteSpace(value) || (bool.TryParse(value, out var enabled)
            ? enabled
            : throw new InvalidOperationException("Exports__Excel must be true or false."));
}

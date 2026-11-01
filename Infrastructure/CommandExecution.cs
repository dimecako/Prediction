namespace Prediction.Infrastructure;

// Each invocation (including nested daily-run steps) owns its status, not the process.
public static class CommandExecution
{
    private sealed class State(bool automation)
    {
        public bool Automation { get; } = automation;
        public int ExitCode;
    }

    private static readonly AsyncLocal<State?> Current = new();
    public static bool IsAutomation => Current.Value?.Automation == true;
    public static int ExitCode => Current.Value?.ExitCode ?? 0;

    public static void Fail() => Current.Value!.ExitCode = 1;

    public static async Task<int> RunAsync(Func<Task> command, bool automation = false)
    {
        var previous = Current.Value;
        var state = new State(automation || previous?.Automation == true);
        Current.Value = state;
        try
        {
            await command();
            return state.ExitCode;
        }
        catch (Exception ex)
        {
            // Exception messages can contain credentials supplied by drivers or upstream services.
            Console.Error.WriteLine($"[COMMAND] Failed ({ex.GetType().Name}). Check configuration and provider availability.");
            return 1;
        }
        finally
        {
            Current.Value = previous;
        }
    }
}

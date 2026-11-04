using Npgsql;

namespace Prediction.Infrastructure;

// A transaction-level lock pins a PgBouncer backend, so direct and pooled Neon strings work.
// This transaction does not contain application writes; disposing it releases only the lock.
public sealed class DailyRunLock : IAsyncDisposable
{
    public const long Key = 5787775673988076868L;
    private readonly NpgsqlConnection connection;
    private readonly NpgsqlTransaction transaction;
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim commands = new(1, 1);
    private readonly Task heartbeat;
    private volatile Exception? heartbeatFailure;

    private DailyRunLock(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        this.connection = connection;
        this.transaction = transaction;
        heartbeat = KeepAliveAsync();
    }

    public static async Task<DailyRunLock?> TryAcquireAsync(string connectionString)
    {
        var options = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false };
        var connection = new NpgsqlConnection(options.ConnectionString);
        try
        {
            await connection.OpenAsync();
            var transaction = await connection.BeginTransactionAsync();
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_try_advisory_xact_lock(@key)", connection, transaction);
                command.Parameters.AddWithValue("key", Key);
                if ((bool)(await command.ExecuteScalarAsync())!)
                    return new(connection, transaction);
            }
            catch
            {
                await transaction.DisposeAsync();
                throw;
            }
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
            return null;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task CheckAsync()
    {
        if (heartbeatFailure != null)
            throw new InvalidOperationException("Daily-run lock connection was lost.", heartbeatFailure);
        await commands.WaitAsync();
        try
        {
            await using var command = new NpgsqlCommand("SELECT 1", connection, transaction);
            await command.ExecuteScalarAsync();
        }
        finally
        {
            commands.Release();
        }
    }

    private async Task KeepAliveAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
            while (await timer.WaitForNextTickAsync(stop.Token))
                await CheckAsync();
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            heartbeatFailure = ex;
            Console.Error.WriteLine("[DAILY-RUN] Database lock heartbeat failed; no further steps will start.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        await heartbeat;
        try
        {
            await transaction.DisposeAsync();
        }
        finally
        {
            await connection.DisposeAsync();
            commands.Dispose();
            stop.Dispose();
        }
    }
}

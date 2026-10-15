using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EquipmentManagement.Infrastructure.DataImports;

/// <summary>
/// Runs <see cref="ExpiredImportSessionCleaner"/> once at startup and then every
/// <see cref="ImportLimitsOptions.ExpiredSessionCleanupIntervalMinutes"/>
/// (ADR-077). A failed run is logged and retried on the next tick — it never
/// stops the host. Logs carry only a count, never session contents.
/// </summary>
public sealed partial class ExpiredImportSessionCleanupWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ImportLimitsOptions> options,
    TimeProvider timeProvider,
    ILogger<ExpiredImportSessionCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.Value.ExpiredSessionCleanupIntervalMinutes), timeProvider);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var cleared = await scope.ServiceProvider.GetRequiredService<ExpiredImportSessionCleaner>().ClearExpiredAsync(stoppingToken);
                if (cleared > 0)
                {
                    LogCleared(logger, cleared);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogFailed(logger, ex);
            }
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Cleared the preview snapshot of {Count} expired import session(s).")]
    private static partial void LogCleared(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Expired import session cleanup failed; retrying on the next run.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

using VelaShell.Market.Infrastructure.Storage;

namespace VelaShell.Market.Api.Services;

/// <summary>启动时确保两个桶存在。桶建不出来时直接让服务起不来 —— 没有隔离桶就没有隔离。</summary>
public sealed class StorageInitializer(PackageStorage storage, ILogger<StorageInitializer> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await storage.EnsureBucketsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Object storage is unavailable; the quarantine bucket could not be prepared.");
            throw;
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

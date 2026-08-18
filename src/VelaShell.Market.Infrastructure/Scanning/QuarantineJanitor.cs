using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using VelaShell.Market.Domain;
using VelaShell.Market.Infrastructure.Persistence;
using VelaShell.Market.Infrastructure.Storage;

namespace VelaShell.Market.Infrastructure.Scanning;

/// <summary>
/// 隔离区清扫工。被拒的包在隔离桶里留够保留期(默认 30 天)后删除对象,数据库里的记录与
/// 检测报告**保留** —— 作者仍然看得见"当初为什么被拒",而占空间的那份二进制不必永远留着。
/// <para>
/// 每天跑一次即可,所以刻意不引调度框架:一个 <see cref="PeriodicTimer" /> 就够,
/// 多一个依赖换不来任何东西。
/// </para>
/// </summary>
public sealed class QuarantineJanitor(
    IServiceScopeFactory scopeFactory,
    IOptions<ObjectStorageOptions> options,
    ILogger<QuarantineJanitor> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        // 启动即扫一次:进程重启频繁时,只靠周期触发可能永远等不到那一刻。
        await SweepAsync(stoppingToken).ConfigureAwait(false);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await SweepAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MarketDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<PackageStorage>();

            DateTime cutoff = DateTime.UtcNow.AddDays(-options.Value.RejectedRetentionDays);
            List<PluginVersion> expired = await db.Versions
                                                  .Find(v => v.Status == PluginVersionStatus.Rejected && v.UploadedAt < cutoff)
                                                  .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (PluginVersion version in expired)
            {
                await storage.DeleteQuarantineAsync(version.ObjectKey, cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Purged rejected package {PluginId} {Version} from quarantine after retention.",
                    version.PluginId, version.Version);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 清扫失败不该影响任何在线路径,下一轮再来。
            logger.LogWarning(ex, "Quarantine sweep failed; will retry on the next tick.");
        }
    }
}

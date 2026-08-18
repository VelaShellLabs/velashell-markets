using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using VelaShell.Market.Domain;
using VelaShell.Market.Infrastructure.Persistence;
using VelaShell.Market.Infrastructure.Storage;

namespace VelaShell.Market.Infrastructure.Scanning;

/// <summary>
/// 待检测队列。进程内 Channel 就够:队列丢了不会丢数据 —— 版本记录还停在
/// <see cref="PluginVersionStatus.Quarantined" />,启动时的补扫会把它们重新捡起来。
/// 换 RabbitMQ(EasilyNET.RabbitBus)只需要替换这一层。
/// </summary>
public sealed class ScanQueue
{
    private readonly Channel<ObjectId> _channel = Channel.CreateUnbounded<ObjectId>();

    /// <summary>入队一个待检测版本。</summary>
    public void Enqueue(ObjectId versionId) => _channel.Writer.TryWrite(versionId);

    /// <summary>出队(供后台工作者消费)。</summary>
    public IAsyncEnumerable<ObjectId> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>
/// 隔离检测流水线的后台工作者。一个上传的完整生命线:
/// <code>
/// 上传 → 隔离桶 + Quarantined
///      → Scanning → 静态检查(容器/结构/清单/内容)→ ClamAV 病毒扫描
///      → 通过        → 搬进正式桶 → Published
///      → 需人工复核  → 留在隔离桶 → Quarantined(带 NeedsReview 报告,等管理员裁决)
///      → 拒收        → 留在隔离桶 → Rejected(保留期后清理)
///      → 引擎故障    → 重排队(至多三次),仍失败则 Errored
/// </code>
/// <para>
/// 三条不许动的纪律:
/// 1) **没通过就绝不进正式桶** —— 下载只签正式桶,物理隔离比任何标记位都可靠;
/// 2) **引擎不可用 ≠ 干净** —— <see cref="ClamAvUnavailableException" /> 走重试,不走放行;
/// 3) **拒收的包不立即删** —— 留着才能复盘"到底为什么判它有罪",也给作者申诉的余地。
/// </para>
/// </summary>
public sealed class PackageReviewWorker(
    ScanQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<PackageReviewWorker> logger) : BackgroundService
{
    /// <summary>引擎不可用时的重试次数。</summary>
    private const int MaxAttempts = 6;

    /// <summary>重试退避的基数,实际延迟为 <c>基数 × 已尝试次数</c>(30s、60s、90s…,累计约 10 分钟)。</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 补扫:上一次进程退出时卡在队列里的版本(以及队列本身丢失的),启动时全部捡回来。
        await RequeuePendingAsync(stoppingToken).ConfigureAwait(false);
        await foreach (ObjectId versionId in queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await ProcessAsync(versionId, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 单个包处理失败绝不能带走整个工作者 —— 那会让后续所有上传永久卡在隔离区。
                logger.LogError(ex, "Reviewing version {VersionId} failed unexpectedly.", versionId);
            }
        }
    }

    private async Task RequeuePendingAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDbContext>();
        FilterDefinition<PluginVersion> pending = Builders<PluginVersion>.Filter.In(v => v.Status,
            [PluginVersionStatus.Quarantined, PluginVersionStatus.Scanning]);
        List<PluginVersion> versions = await db.Versions.Find(pending).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (PluginVersion version in versions.Where(v => v.Scan?.Verdict != ScanVerdict.NeedsReview))
        {
            queue.Enqueue(version.Id);
        }
        if (versions.Count > 0)
        {
            logger.LogInformation("Re-queued {Count} package(s) left in quarantine by a previous run.", versions.Count);
        }
    }

    private async Task ProcessAsync(ObjectId versionId, CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<PackageStorage>();
        var clam = scope.ServiceProvider.GetRequiredService<ClamAvScanner>();

        PluginVersion? version = await db.Versions.Find(v => v.Id == versionId)
                                        .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (version is null || version.Status is PluginVersionStatus.Published or PluginVersionStatus.Withdrawn)
        {
            return;
        }

        var report = new ScanReport
        {
            StartedAt = DateTime.UtcNow,
            Attempts = (version.Scan?.Attempts ?? 0) + 1
        };
        await SetStatusAsync(db, version.Id, PluginVersionStatus.Scanning, report, cancellationToken).ConfigureAwait(false);

        // ---- 静态检查 ---------------------------------------------------------
        VpxInspection inspection;
        await using (Stream quarantined = await storage.OpenQuarantineAsync(version.ObjectKey, cancellationToken).ConfigureAwait(false))
        {
            inspection = VpxStaticInspector.Inspect(quarantined, version.PluginId, version.Version);
        }
        report.Findings.AddRange(inspection.Findings);
        report.EntryCount = inspection.EntryCount;
        report.UnpackedBytes = inspection.UnpackedBytes;
        report.Engines["vpx-static"] = typeof(VpxStaticInspector).Assembly.GetName().Version?.ToString() ?? "0";

        // 签名公钥的连续性:同一个插件的新版本必须与已发布版本同一把钥匙。
        // 换钥意味着"这个包是不是还是原作者发的"这件事无从判断 —— 只能转人工。
        if (inspection.Info?.Signature is { } signature)
        {
            report.Engines["signature"] = signature.Algorithm;
            PluginVersion? published = await db.Versions
                                               .Find(v => v.PluginId == version.PluginId
                                                          && v.Status == PluginVersionStatus.Published
                                                          && v.SignaturePublicKey != null)
                                               .SortByDescending(v => v.PublishedAt)
                                               .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (published is not null && !string.Equals(published.SignaturePublicKey, signature.PublicKey, StringComparison.Ordinal))
            {
                report.Findings.Add(new("SIGNATURE_KEY_ROTATED", ScanSeverity.Warning,
                    "本版本的签名公钥与该插件已发布版本使用的公钥不同。换钥可能是正常的密钥轮换," +
                    "也可能是发布身份被劫持 —— 需要人工确认。"));
            }
        }

        // ---- 病毒扫描 ---------------------------------------------------------
        if (!clam.Enabled)
        {
            report.Findings.Add(new("CLAMAV_DISABLED", ScanSeverity.Warning,
                "病毒扫描引擎在本部署中被关闭,本包未经过病毒库比对。"));
        }
        else if (!report.HasBlocking)
        {
            // 已经判死的包不必再扫:省一次全包 IO,结论也不会变。
            try
            {
                await using Stream quarantined = await storage.OpenQuarantineAsync(version.ObjectKey, cancellationToken).ConfigureAwait(false);
                ClamAvResult result = await clam.ScanAsync(quarantined, cancellationToken).ConfigureAwait(false);
                report.Engines["clamav"] = result.EngineVersion;
                report.Findings.Add(result.IsClean
                    ? new("CLAMAV_CLEAN", ScanSeverity.Info, "病毒扫描通过。")
                    : new("CLAMAV_HIT", ScanSeverity.Blocking, $"病毒扫描命中:{result.Signature}"));
            }
            catch (ClamAvUnavailableException ex)
            {
                logger.LogWarning(ex, "ClamAV unavailable while scanning {PluginId} {Version}.", version.PluginId, version.Version);
                if (report.Attempts < MaxAttempts)
                {
                    // 重排队,不下结论。**必须延迟**:立刻重排的话三次重试会在几毫秒内耗完,
                    // 而 clamd 首次启动要拉几分钟病毒库 —— 那样每个上传都会在冷启动时被判 Errored,
                    // 明明是我们这边还没准备好。退避时长按尝试次数递增。
                    report.Verdict = ScanVerdict.Pending;
                    report.CompletedAt = null;
                    await SetStatusAsync(db, version.Id, PluginVersionStatus.Quarantined, report, cancellationToken).ConfigureAwait(false);
                    TimeSpan delay = RetryDelay * report.Attempts;
                    logger.LogInformation("Retrying {PluginId} {Version} in {Delay}.", version.PluginId, version.Version, delay);
                    _ = Task.Delay(delay, cancellationToken)
                            .ContinueWith(_ => queue.Enqueue(version.Id), CancellationToken.None,
                                TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
                    return;
                }
                report.Findings.Add(new("CLAMAV_UNAVAILABLE", ScanSeverity.Blocking,
                    $"病毒扫描引擎连续 {MaxAttempts} 次不可用,本包无法完成检测。这不是包的问题,请稍后重试上传。"));
                report.Verdict = ScanVerdict.Errored;
            }
        }

        // ---- 裁决 -------------------------------------------------------------
        report.CompletedAt = DateTime.UtcNow;
        if (report.Verdict != ScanVerdict.Errored)
        {
            report.Verdict = report.HasBlocking ? ScanVerdict.Failed
                : report.HasWarning ? ScanVerdict.NeedsReview
                : ScanVerdict.Passed;
        }
        report.Findings.Sort((a, b) => b.Severity.CompareTo(a.Severity));

        switch (report.Verdict)
        {
            case ScanVerdict.Passed:
                await PublishAsync(db, storage, version, inspection, report, cancellationToken).ConfigureAwait(false);
                break;
            case ScanVerdict.NeedsReview:
                // 留在隔离桶等人裁决:它既没被拒,也绝不可下载。
                await SetStatusAsync(db, version.Id, PluginVersionStatus.Quarantined, report, cancellationToken).ConfigureAwait(false);
                logger.LogInformation("{PluginId} {Version} needs manual review ({Count} warning(s)).",
                    version.PluginId, version.Version, report.Findings.Count(f => f.Severity == ScanSeverity.Warning));
                break;
            default:
                await SetStatusAsync(db, version.Id, PluginVersionStatus.Rejected, report, cancellationToken).ConfigureAwait(false);
                logger.LogWarning("{PluginId} {Version} rejected: {Reason}", version.PluginId, version.Version,
                    report.Findings.FirstOrDefault(f => f.Severity == ScanSeverity.Blocking)?.Message);
                break;
        }
    }

    /// <summary>通过检测:搬桶 → 落版本元数据 → 更新插件条目的"最新版本"。</summary>
    public static async Task PublishAsync(MarketDbContext db, PackageStorage storage, PluginVersion version,
        VpxInspection inspection, ScanReport report, CancellationToken cancellationToken)
    {
        await storage.PromoteAsync(version.ObjectKey, cancellationToken).ConfigureAwait(false);

        UpdateDefinition<PluginVersion> update = Builders<PluginVersion>.Update
            .Set(v => v.Status, PluginVersionStatus.Published)
            .Set(v => v.Scan, report)
            .Set(v => v.PublishedAt, DateTime.UtcNow);
        if (inspection.Manifest is { } manifest)
        {
            update = update.Set(v => v.ApiLevel, manifest.ApiLevel)
                           .Set(v => v.MinHostVersion, manifest.MinHostVersion)
                           .Set(v => v.HostMode, manifest.HostMode.ToString())
                           .Set(v => v.Entry, manifest.Entry);
        }
        if (inspection.Info is { } info)
        {
            update = update.Set(v => v.PayloadSha256, info.PayloadSha256)
                           .Set(v => v.SignatureState, VpxContainerStateName(info))
                           .Set(v => v.SignaturePublicKey, info.Signature?.PublicKey);
        }
        await db.Versions.UpdateOneAsync(v => v.Id == version.Id, update, cancellationToken: cancellationToken).ConfigureAwait(false);

        // 插件条目的展示信息跟随最新版本。用 semver 比较而不是"最后上传的赢" ——
        // 补发一个旧版本的修订不该把首页显示的版本号打回去。
        List<PluginVersion> published = await db.Versions
                                                .Find(v => v.PluginId == version.PluginId && v.Status == PluginVersionStatus.Published)
                                                .ToListAsync(cancellationToken).ConfigureAwait(false);
        PluginVersion latest = published.OrderByDescending(v => v.Version, SemVerComparer.Instance).First();
        UpdateDefinition<Plugin> pluginUpdate = Builders<Plugin>.Update
            .Set(p => p.LatestVersion, latest.Version)
            .Set(p => p.LatestApiLevel, latest.ApiLevel)
            .Set(p => p.LatestMinHostVersion, latest.MinHostVersion)
            .Set(p => p.UpdatedAt, DateTime.UtcNow);
        if (inspection.Manifest is { } m && latest.Id == version.Id)
        {
            pluginUpdate = pluginUpdate.Set(p => p.DisplayName, m.DisplayName)
                                       .Set(p => p.Summary, m.Description)
                                       .Set(p => p.Author, m.Author ?? m.Publisher)
                                       .Set(p => p.Publisher, m.Publisher)
                                       .Set(p => p.Homepage, m.Homepage)
                                       .Set(p => p.License, m.License);
        }
        await db.Plugins.UpdateOneAsync(p => p.Id == version.PluginId, pluginUpdate, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static string VpxContainerStateName(PluginSdk.Packaging.VpxPackageInfo info) =>
        PluginSdk.Packaging.VpxContainer.VerifySignature(info).ToString();

    private static Task SetStatusAsync(MarketDbContext db, ObjectId id, PluginVersionStatus status,
        ScanReport report, CancellationToken cancellationToken) =>
        db.Versions.UpdateOneAsync(v => v.Id == id,
            Builders<PluginVersion>.Update.Set(v => v.Status, status).Set(v => v.Scan, report),
            cancellationToken: cancellationToken);
}

/// <summary>
/// semver 排序。只比较数字段并让**预发布版本低于同号正式版**(1.0.0-beta &lt; 1.0.0),
/// 够用且可预期;完整 semver 优先级规则(比较预发布标识符)留待真有需要再说。
/// </summary>
public sealed class SemVerComparer : IComparer<string>
{
    /// <summary>共享实例。</summary>
    public static readonly SemVerComparer Instance = new();

    /// <inheritdoc />
    public int Compare(string? x, string? y)
    {
        if (x is null || y is null)
        {
            return string.CompareOrdinal(x, y);
        }
        (int[] Numbers, string Pre) left = Split(x);
        (int[] Numbers, string Pre) right = Split(y);
        for (int i = 0; i < Math.Max(left.Numbers.Length, right.Numbers.Length); i++)
        {
            int a = i < left.Numbers.Length ? left.Numbers[i] : 0;
            int b = i < right.Numbers.Length ? right.Numbers[i] : 0;
            if (a != b)
            {
                return a.CompareTo(b);
            }
        }
        return (left.Pre.Length == 0, right.Pre.Length == 0) switch
        {
            (true, false) => 1,
            (false, true) => -1,
            _ => string.CompareOrdinal(left.Pre, right.Pre)
        };
    }

    private static (int[] Numbers, string Pre) Split(string version)
    {
        int dash = version.IndexOf('-');
        string core = dash < 0 ? version : version[..dash];
        string pre = dash < 0 ? "" : version[(dash + 1)..];
        int[] numbers = core.Split('.')
                            .Select(part => int.TryParse(part, out int value) ? value : 0)
                            .ToArray();
        return (numbers, pre);
    }
}

using System.IO.Compression;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using VelaShell.PluginSdk.Packaging;
using VelaShell.Market.Api.Options;
using VelaShell.Market.Domain;
using VelaShell.Market.Infrastructure.Persistence;
using VelaShell.Market.Infrastructure.Scanning;
using VelaShell.Market.Infrastructure.Storage;

namespace VelaShell.Market.Api.Endpoints;

/// <summary>审核台:处理检测流水线判为"需人工复核"的包,以及下架、描述治理与评价治理。</summary>
public static class ModerationEndpoints
{
    /// <summary>一页最多返回多少条。审核台是人在翻,给再多也看不过来。</summary>
    private const int MaxPageSize = 100;

    /// <summary>挂载端点。</summary>
    public static void MapModerationEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/moderation")
                                     .RequireAuthorization(MarketPolicies.Moderator)
                                     .WithTags("Moderation");

        group.MapGet("/queue", QueueAsync).WithSummary("待人工复核的版本(带完整检测报告)。");
        group.MapGet("/versions/{id}/entries", EntriesAsync).WithSummary("隔离区里那个包的包内清单(文件名、大小、可疑标记)。");
        group.MapGet("/versions/{id}/sample", SampleAsync).WithSummary("下载隔离区里的样本包(只经 API 转发,不签发对外 URL)。");
        group.MapPost("/versions/{id}/approve", ApproveAsync).WithSummary("放行:搬进正式桶并发布。");
        group.MapPost("/versions/{id}/reject", RejectAsync).WithSummary("驳回:留在隔离区,记录原因。");

        group.MapGet("/plugins", PluginsAsync).WithSummary("插件治理列表(含已下架,可搜索)。");
        group.MapPost("/plugins/{pluginId}/unlist", UnlistAsync).WithSummary("软下架:从检索中消失,包仍可下载。");
        group.MapPost("/plugins/{pluginId}/relist", RelistAsync).WithSummary("恢复上架(软下架的反向操作)。");
        group.MapPost("/plugins/{pluginId}/takedown", TakedownAsync)
             .WithSummary("强制下架:下架并**物理删除**正式桶里的全部已发布版本,不可逆。");
        group.MapPost("/plugins/{pluginId}/clear-description", ClearDescriptionAsync)
             .WithSummary("清空违规描述,保留插件本身。");
        group.MapPost("/plugins/{pluginId}/feature", FeatureAsync).WithSummary("设为「编辑推荐」,在浏览页首屏占一张双宽卡片。");
        group.MapPost("/plugins/{pluginId}/unfeature", UnfeatureAsync).WithSummary("取消「编辑推荐」。");

        group.MapGet("/reviews", ReviewsAsync).WithSummary("评价治理列表(含已隐藏,可按插件/关键词筛)。");
        group.MapPost("/reviews/{id}/hide", HideReviewAsync).WithSummary("隐藏违规评价(可撤销,不计入评分)。");
        group.MapPost("/reviews/{id}/unhide", UnhideReviewAsync).WithSummary("取消隐藏。");
        group.MapPost("/reviews/{id}/purge", PurgeReviewAsync)
             .WithSummary("彻底删除评价:从数据库物理移除,不可逆。");
    }

    // ---- 隔离队列 ------------------------------------------------------------

    private static async Task<IResult> QueueAsync(MarketDbContext db)
    {
        List<PluginVersion> pending = await db.Versions
                                              .Find(v => v.Status == PluginVersionStatus.Quarantined)
                                              .SortBy(v => v.UploadedAt)
                                              .Limit(200)
                                              .ToListAsync().ConfigureAwait(false);
        List<PluginVersion> queue = [.. pending.Where(v => v.Scan?.Verdict == ScanVerdict.NeedsReview)];

        // 审核台是左队列右详情的分栏,选中一条就要立刻显示上传者、历史与引擎信息 ——
        // 这些一次带出来,省掉"每点一条再查一次"的往返。
        List<string> pluginIds = [.. queue.Select(v => v.PluginId).Distinct(StringComparer.Ordinal)];
        Dictionary<string, Plugin> plugins = pluginIds.Count == 0
                                                 ? []
                                                 : (await db.Plugins.Find(p => pluginIds.Contains(p.Id)).ToListAsync().ConfigureAwait(false))
                                                 .ToDictionary(p => p.Id, StringComparer.Ordinal);
        Dictionary<string, int> publishedCounts = [];
        foreach (string pluginId in pluginIds)
        {
            publishedCounts[pluginId] = (int)await db.Versions
                                                     .CountDocumentsAsync(v => v.PluginId == pluginId && v.Status == PluginVersionStatus.Published)
                                                     .ConfigureAwait(false);
        }

        return Results.Ok(queue.Select(v => new
        {
            id = v.Id.ToString(),
            v.PluginId,
            displayName = plugins.TryGetValue(v.PluginId, out Plugin? p) ? p.DisplayName : v.PluginId,
            v.Version,
            v.UploadedBySubject,
            uploadedByName = plugins.TryGetValue(v.PluginId, out Plugin? owner) ? owner.OwnerName : null,
            v.UploadedAt,
            v.PackageSize,
            signature = v.SignatureState,
            // 这个作者此前有多少个版本平安通过 —— 判断"换签名密钥"这类告警时最有用的一条背景。
            publishedVersions = publishedCounts.GetValueOrDefault(v.PluginId),
            scan = new
            {
                verdict = v.Scan!.Verdict.ToString(),
                v.Scan.StartedAt,
                v.Scan.CompletedAt,
                v.Scan.Engines,
                v.Scan.EntryCount,
                v.Scan.UnpackedBytes,
                v.Scan.Attempts
            },
            findings = v.Scan.Findings.Select(f => new { f.Code, severity = f.Severity.ToString(), f.Message, f.Path })
        }));
    }

    /// <summary>
    /// 包内清单。审核员判断"这个脚本到底是干什么的"之前,总要先看见包里都有什么。
    ///
    /// 只对**还在隔离区**的版本开放:已发布的包任何人都能下载,没必要再走审核通道;
    /// 而这个端点会把隔离区里的东西读出来,它的存在本身就该被限制在最小范围里。
    /// </summary>
    private static async Task<IResult> EntriesAsync(string id, MarketDbContext db, PackageStorage storage, CancellationToken cancellationToken)
    {
        (PluginVersion? version, IResult? error) = await FindQuarantinedAsync(id, db, cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        string temp = Path.Combine(Path.GetTempPath(), $"vpx-entries-{Guid.NewGuid():N}.vpx");
        try
        {
            await using (Stream remote = await storage.OpenQuarantineAsync(version!.ObjectKey, cancellationToken).ConfigureAwait(false))
            await using (FileStream local = File.Create(temp))
            {
                // S3 的响应流不可 Seek,而读容器要定位 —— 先落一份本地临时文件。
                await remote.CopyToAsync(local, cancellationToken).ConfigureAwait(false);
            }

            await using Stream payload = VpxContainer.OpenPayload(temp);
            using ZipArchive archive = new(payload, ZipArchiveMode.Read);
            var entries = archive.Entries
                                 .Where(e => !string.IsNullOrEmpty(e.Name))
                                 .OrderBy(e => e.FullName, StringComparer.Ordinal)
                                 .Take(2000)
                                 .Select(e => new
                                 {
                                     path = e.FullName,
                                     size = e.Length,
                                     compressed = e.CompressedLength,
                                     flag = VpxStaticInspector.Classify(e.FullName)
                                 })
                                 .ToList();
            return Results.Ok(new { total = archive.Entries.Count, truncated = archive.Entries.Count > entries.Count, entries });
        }
        catch (VpxFormatException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
                // 临时文件删不掉不影响这次读取。
            }
        }
    }

    /// <summary>
    /// 下载隔离区里的样本。
    ///
    /// 刻意**由 API 转发字节流**,而不是像正式桶那样签一个预签名 URL:
    /// 隔离桶永远不该有对外可访问的地址 —— 一旦签得出来,那个链接就会被转发、被缓存、
    /// 被写进工单,而它指向的正是一个尚未通过检测的包。走 API 的话,每一次读取都带着
    /// 审核员自己的令牌,过期即失效,也留得下痕。
    /// </summary>
    private static async Task<IResult> SampleAsync(string id, MarketDbContext db, PackageStorage storage,
        ILoggerFactory loggerFactory, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        (PluginVersion? version, IResult? error) = await FindQuarantinedAsync(id, db, cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }
        loggerFactory.CreateLogger("Moderation")
                     .LogWarning("Moderator {Subject} downloaded quarantined sample {PluginId} {Version}.",
                         user.Subject(), version!.PluginId, version.Version);

        Stream remote = await storage.OpenQuarantineAsync(version.ObjectKey, cancellationToken).ConfigureAwait(false);
        return Results.Stream(remote, "application/vnd.velashell.plugin",
            $"{version.PluginId}-{version.Version}.vpx");
    }

    /// <summary>取一个仍在隔离区的版本。返回的 error 非 null 时直接回给调用方。</summary>
    private static async Task<(PluginVersion? Version, IResult? Error)> FindQuarantinedAsync(string id, MarketDbContext db, CancellationToken cancellationToken)
    {
        if (!MongoDB.Bson.ObjectId.TryParse(id, out MongoDB.Bson.ObjectId objectId))
        {
            return (null, Results.BadRequest(new { error = "非法的版本 id。" }));
        }
        PluginVersion? version = await db.Versions.Find(v => v.Id == objectId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (version is null)
        {
            return (null, Results.NotFound());
        }
        return version.Status is PluginVersionStatus.Quarantined or PluginVersionStatus.Scanning or PluginVersionStatus.Rejected
                   ? (version, null)
                   : (null, Results.Problem(statusCode: StatusCodes.Status409Conflict, detail: "该版本已不在隔离区。"));
    }

    /// <summary>
    /// 放行。<paramref name="note" /> 可空:放行不强制填原因(它不是对作者的处置),
    /// 但填了就一起记进报告 —— 事后要能回答的是"当时为什么判断这个可疑项没问题",
    /// 而这句话只有按下按钮的那个人知道。
    /// </summary>
    private static async Task<IResult> ApproveAsync(string id, [FromBody] ModerationReason? note,
        MarketDbContext db, PackageStorage storage, ClaimsPrincipal user, ILogger<ModerationAudit> logger, CancellationToken cancellationToken)
    {
        if (!MongoDB.Bson.ObjectId.TryParse(id, out MongoDB.Bson.ObjectId objectId))
        {
            return Results.BadRequest(new { error = "非法的版本 id。" });
        }
        PluginVersion? version = await db.Versions.Find(v => v.Id == objectId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (version is null)
        {
            return Results.NotFound();
        }
        if (version.Status != PluginVersionStatus.Quarantined)
        {
            // 只允许从隔离区放行。已拒的要先重新检测,已发布的不必再放行。
            return Results.Conflict(new { error = $"该版本当前状态为 {version.Status},只有隔离中的版本可以放行。" });
        }
        ScanReport report = version.Scan ?? new();
        report.Verdict = ScanVerdict.Passed;
        string reason = note?.Reason?.Trim() is { Length: > 0 } text ? $"已由审核员人工放行:{text}" : "已由审核员人工放行。";
        report.Findings.Add(new("MANUAL_APPROVED", ScanSeverity.Info, reason));
        report.CompletedAt = DateTime.UtcNow;
        logger.LogInformation("审核员 {Moderator} 放行 {PluginId} {Version}。{Note}", user.Subject(), version.PluginId, version.Version, note?.Reason);

        // 复用流水线里那一份发布逻辑:审核放行与自动放行必须走同一条路径,
        // 否则两条路迟早会长出不一样的元数据。
        await PackageReviewWorker.PublishAsync(db, storage, version,
            new(report.Findings, null, null, report.EntryCount, report.UnpackedBytes), report, cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> RejectAsync(string id, [FromBody] ModerationReason reason,
        MarketDbContext db, CancellationToken cancellationToken)
    {
        if (!MongoDB.Bson.ObjectId.TryParse(id, out MongoDB.Bson.ObjectId objectId))
        {
            return Results.BadRequest(new { error = "非法的版本 id。" });
        }
        if (string.IsNullOrWhiteSpace(reason.Reason))
        {
            return Results.BadRequest(new { error = "必须填写驳回原因。" });
        }
        PluginVersion? version = await db.Versions.Find(v => v.Id == objectId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (version is null)
        {
            return Results.NotFound();
        }
        ScanReport report = version.Scan ?? new();
        report.Verdict = ScanVerdict.Failed;
        report.Findings.Add(new("MANUAL_REJECTED", ScanSeverity.Blocking, reason.Reason));
        report.CompletedAt = DateTime.UtcNow;
        await db.Versions.UpdateOneAsync(v => v.Id == objectId,
            Builders<PluginVersion>.Update.Set(v => v.Status, PluginVersionStatus.Rejected).Set(v => v.Scan, report),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    // ---- 插件治理 ------------------------------------------------------------

    /// <summary>
    /// 插件治理列表。与公开检索是两回事:这里**必须**能看见已下架的条目 ——
    /// 审核员要恢复一个被误下架的插件,前提是他找得到它。
    /// </summary>
    private static async Task<IResult> PluginsAsync(MarketDbContext db, string? q = null,
        bool? unlisted = null, int page = 1, int size = 20)
    {
        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, MaxPageSize);
        FilterDefinitionBuilder<Plugin> f = Builders<Plugin>.Filter;
        FilterDefinition<Plugin> filter = f.Empty;
        if (!string.IsNullOrWhiteSpace(q))
        {
            // 用户输入直接拼进正则会让 `.*(a+)+` 这种回溯炸弹打垮数据库,必须转义成字面量。
            string pattern = Regex.Escape(q.Trim());
            filter = f.And(filter, f.Or(
                f.Regex(p => p.Id, new(pattern, "i")),
                f.Regex(p => p.DisplayName, new(pattern, "i")),
                f.Regex(p => p.OwnerSubject, new(pattern, "i"))));
        }
        if (unlisted is not null)
        {
            filter = f.And(filter, f.Eq(p => p.IsUnlisted, unlisted.Value));
        }
        long total = await db.Plugins.CountDocumentsAsync(filter).ConfigureAwait(false);
        List<Plugin> items = await db.Plugins.Find(filter)
                                     .SortByDescending(p => p.UpdatedAt)
                                     .Skip((page - 1) * size).Limit(size)
                                     .ToListAsync().ConfigureAwait(false);
        return Results.Ok(new
        {
            total,
            page,
            size,
            items = items.Select(p => new
            {
                p.Id,
                p.DisplayName,
                p.Summary,
                p.OwnerSubject,
                p.OwnerName,
                p.LatestVersion,
                p.Downloads,
                p.RatingAverage,
                p.RatingCount,
                p.IsFeatured,
                p.FeaturedAt,
                p.IsUnlisted,
                p.UnlistedReason,
                p.UnlistedAt,
                // 描述原文而不是渲染后的 HTML:审核员要判断的是作者写了什么,
                // 渲染会把 <script> 之类恰恰最该被看见的东西吃掉。
                p.DescriptionMarkdown,
                p.DescriptionRemovedReason,
                p.DescriptionRemovedAt,
                p.UpdatedAt
            })
        });
    }

    /// <summary>
    /// 软下架:从检索与详情页移除,但**正式桶里的包仍然可以下载**。
    /// 适用于"不该继续推广、但已装用户不必立刻断供"的情形(如作者弃坑、描述夸大)。
    /// 真正有害的包要用 <see cref="TakedownAsync" />。
    /// </summary>
    private static async Task<IResult> UnlistAsync(string pluginId, [FromBody] ModerationReason reason,
        MarketDbContext db, ClaimsPrincipal user, ILogger<ModerationAudit> logger, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason.Reason))
        {
            return Results.BadRequest(new { error = "必须填写下架原因。" });
        }
        UpdateResult result = await db.Plugins.UpdateOneAsync(p => p.Id == pluginId,
            Builders<Plugin>.Update
                            .Set(p => p.IsUnlisted, true)
                            .Set(p => p.UnlistedReason, reason.Reason)
                            .Set(p => p.UnlistedAt, DateTime.UtcNow),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.MatchedCount == 0)
        {
            return Results.NotFound();
        }
        logger.LogWarning("审核员 {Moderator} 软下架插件 {PluginId},原因:{Reason}", user.Subject(), pluginId, reason.Reason);
        return Results.NoContent();
    }

    /// <summary>
    /// 恢复上架。注意它**只恢复可见性**:强制下架已经把包删了,那种情况下恢复出来的是
    /// 一个没有任何可下载版本的空页面,作者需要重新发版。
    /// </summary>
    private static async Task<IResult> RelistAsync(string pluginId, MarketDbContext db,
        ClaimsPrincipal user, ILogger<ModerationAudit> logger, CancellationToken cancellationToken)
    {
        UpdateResult result = await db.Plugins.UpdateOneAsync(p => p.Id == pluginId,
            Builders<Plugin>.Update
                            .Set(p => p.IsUnlisted, false)
                            .Set(p => p.UnlistedReason, null)
                            .Set(p => p.UnlistedAt, null),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.MatchedCount == 0)
        {
            return Results.NotFound();
        }
        logger.LogWarning("审核员 {Moderator} 恢复上架插件 {PluginId}。", user.Subject(), pluginId);
        return Results.NoContent();
    }

    /// <summary>
    /// 强制下架:恶意包、侵权包、违规包走这条路。
    ///
    /// 与软下架的本质区别是**把正式桶里的字节删掉**。只标记状态是不够的 ——
    /// 预签名 URL 在有效期内仍然指向真实对象,而"再放十分钟恶意代码"不可接受。
    ///
    /// 同时把还在隔离区/检测中的版本一并判为 Rejected:不这么做的话,
    /// 一个已被强制下架的插件仍可能被另一个审核员从队列里"放行"回来。
    /// </summary>
    private static async Task<IResult> TakedownAsync(string pluginId, [FromBody] ModerationReason reason,
        MarketDbContext db, PackageStorage storage, ClaimsPrincipal user,
        ILogger<ModerationAudit> logger, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason.Reason))
        {
            return Results.BadRequest(new { error = "必须填写强制下架原因。" });
        }
        Plugin? plugin = await db.Plugins.Find(p => p.Id == pluginId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (plugin is null)
        {
            return Results.NotFound();
        }

        List<PluginVersion> published = await db.Versions
                                                .Find(v => v.PluginId == pluginId && v.Status == PluginVersionStatus.Published)
                                                .ToListAsync(cancellationToken).ConfigureAwait(false);
        int deleted = 0;
        List<string> failures = [];
        foreach (PluginVersion version in published)
        {
            try
            {
                await storage.DeletePublicAsync(version.ObjectKey, cancellationToken).ConfigureAwait(false);
                deleted++;
            }
            catch (Exception ex)
            {
                // 某个对象删不掉不该让整次下架半途而废:剩下的版本继续删,状态照常改成
                // Withdrawn(至少不再可检索),把删不掉的键报给审核员去手工清理。
                logger.LogError(ex, "强制下架 {PluginId} 时删除对象 {Key} 失败。", pluginId, version.ObjectKey);
                failures.Add(version.ObjectKey);
            }
            await db.Versions.UpdateOneAsync(v => v.Id == version.Id,
                Builders<PluginVersion>.Update.Set(v => v.Status, PluginVersionStatus.Withdrawn),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        // 还没定论的版本(隔离中/检测中)一并封死,免得事后被误放行。
        UpdateResult blocked = await db.Versions.UpdateManyAsync(
            v => v.PluginId == pluginId
                 && (v.Status == PluginVersionStatus.Quarantined || v.Status == PluginVersionStatus.Scanning),
            Builders<PluginVersion>.Update.Set(v => v.Status, PluginVersionStatus.Rejected),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await db.Plugins.UpdateOneAsync(p => p.Id == pluginId,
            Builders<Plugin>.Update
                            .Set(p => p.IsUnlisted, true)
                            .Set(p => p.UnlistedReason, reason.Reason)
                            .Set(p => p.UnlistedAt, DateTime.UtcNow)
                            // 已发布版本一个都不剩了,展示信息必须跟着清空,
                            // 否则插件页还挂着一个下载即 404 的"最新版本"。
                            .Set(p => p.LatestVersion, null)
                            .Set(p => p.LatestApiLevel, null)
                            .Set(p => p.LatestMinHostVersion, null)
                            .Set(p => p.UpdatedAt, DateTime.UtcNow),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        logger.LogWarning("审核员 {Moderator} 强制下架插件 {PluginId}:删除 {Deleted} 个已发布版本、封停 {Blocked} 个待检版本,原因:{Reason}",
            user.Subject(), pluginId, deleted, blocked.ModifiedCount, reason.Reason);
        return Results.Ok(new { deletedVersions = deleted, blockedVersions = blocked.ModifiedCount, failedKeys = failures });
    }

    /// <summary>
    /// 清空违规描述,插件本身继续可用。
    ///
    /// 刻意**不给审核员改写描述的权力**:审核员替作者写内容,出了问题谁负责说不清楚。
    /// 这里只做"移除 + 说明原因",作者看到原因后自己重写(重写时说明自动消失)。
    /// </summary>
    private static async Task<IResult> ClearDescriptionAsync(string pluginId, [FromBody] ModerationReason reason,
        MarketDbContext db, ClaimsPrincipal user, ILogger<ModerationAudit> logger, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason.Reason))
        {
            return Results.BadRequest(new { error = "必须填写移除原因 —— 作者要据此重写。" });
        }
        UpdateResult result = await db.Plugins.UpdateOneAsync(p => p.Id == pluginId,
            Builders<Plugin>.Update
                            .Set(p => p.DescriptionMarkdown, "")
                            .Set(p => p.DescriptionRemovedReason, reason.Reason)
                            .Set(p => p.DescriptionRemovedAt, DateTime.UtcNow)
                            .Set(p => p.UpdatedAt, DateTime.UtcNow),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.MatchedCount == 0)
        {
            return Results.NotFound();
        }
        logger.LogWarning("审核员 {Moderator} 清空插件 {PluginId} 的描述,原因:{Reason}", user.Subject(), pluginId, reason.Reason);
        return Results.NoContent();
    }

    /// <summary>
    /// 设为「编辑推荐」。浏览页首屏那张双宽卡片就是它。
    ///
    /// 不要求填原因:推荐是加分动作,作者不会因为被推荐而需要一个解释 ——
    /// 「必须填原因」这条约束是给**处置**用的,套到所有动作上只会让人学会随手打个"ok"。
    /// 但仍然记日志:首屏位置是稀缺资源,事后要能回答"谁把它放上去的"。
    /// </summary>
    private static async Task<IResult> FeatureAsync(string pluginId, MarketDbContext db, ClaimsPrincipal user,
        ILogger<ModerationAudit> logger, CancellationToken cancellationToken)
    {
        UpdateResult result = await db.Plugins.UpdateOneAsync(
            p => p.Id == pluginId && !p.IsUnlisted && p.LatestVersion != null,
            Builders<Plugin>.Update.Set(p => p.IsFeatured, true).Set(p => p.FeaturedAt, DateTime.UtcNow),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.MatchedCount == 0)
        {
            // 已下架、或者一个版本都还没发布的插件推不上去:首屏点进去只会是 404 或空页。
            return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                detail: "该插件不存在、已下架,或还没有任何已发布版本。");
        }
        logger.LogInformation("审核员 {Moderator} 把 {PluginId} 设为编辑推荐。", user.Subject(), pluginId);
        return Results.NoContent();
    }

    private static async Task<IResult> UnfeatureAsync(string pluginId, MarketDbContext db, ClaimsPrincipal user,
        ILogger<ModerationAudit> logger, CancellationToken cancellationToken)
    {
        UpdateResult result = await db.Plugins.UpdateOneAsync(
            p => p.Id == pluginId,
            Builders<Plugin>.Update.Set(p => p.IsFeatured, false).Set(p => p.FeaturedAt, null),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.MatchedCount == 0)
        {
            return Results.NotFound();
        }
        logger.LogInformation("审核员 {Moderator} 取消了 {PluginId} 的编辑推荐。", user.Subject(), pluginId);
        return Results.NoContent();
    }

    // ---- 评价治理 ------------------------------------------------------------

    /// <summary>
    /// 评价治理列表。返回 <b>Markdown 原文</b>而不是渲染后的 HTML:
    /// 审核员判断的是作者写了什么,渲染这一步恰好会吃掉最该被看见的东西。
    /// 已隐藏的评价在这里**必须可见**,否则取消隐藏就无从操作。
    /// </summary>
    private static async Task<IResult> ReviewsAsync(MarketDbContext db, string? pluginId = null,
        bool? hidden = null, string? q = null, int page = 1, int size = 20)
    {
        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, MaxPageSize);
        FilterDefinitionBuilder<Review> f = Builders<Review>.Filter;
        FilterDefinition<Review> filter = f.Empty;
        if (!string.IsNullOrWhiteSpace(pluginId))
        {
            filter = f.And(filter, f.Eq(r => r.PluginId, pluginId));
        }
        if (hidden is not null)
        {
            filter = f.And(filter, f.Eq(r => r.IsHidden, hidden.Value));
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            string pattern = Regex.Escape(q.Trim());
            filter = f.And(filter, f.Or(
                f.Regex(r => r.BodyMarkdown, new(pattern, "i")),
                f.Regex(r => r.DisplayName, new(pattern, "i")),
                f.Regex(r => r.Subject, new(pattern, "i"))));
        }
        long total = await db.Reviews.CountDocumentsAsync(filter).ConfigureAwait(false);
        List<Review> items = await db.Reviews.Find(filter)
                                     .SortByDescending(r => r.UpdatedAt)
                                     .Skip((page - 1) * size).Limit(size)
                                     .ToListAsync().ConfigureAwait(false);
        return Results.Ok(new
        {
            total,
            page,
            size,
            items = items.Select(r => new
            {
                id = r.Id.ToString(),
                r.PluginId,
                r.Subject,
                r.DisplayName,
                r.Rating,
                body = r.BodyMarkdown,
                r.PluginVersion,
                r.CreatedAt,
                r.UpdatedAt,
                r.IsHidden,
                r.HiddenReason,
                r.HiddenAt,
                r.HiddenBySubject
            })
        });
    }

    /// <summary>
    /// 隐藏一条评价。**默认手段**:可撤销、留档、不计入评分均值。
    /// 内容必须从库里彻底消失时(政治敏感等)用 <see cref="PurgeReviewAsync" />。
    /// </summary>
    private static async Task<IResult> HideReviewAsync(string id, [FromBody] ModerationReason reason,
        MarketDbContext db, ClaimsPrincipal user, ILogger<ModerationAudit> logger, CancellationToken cancellationToken)
    {
        if (!MongoDB.Bson.ObjectId.TryParse(id, out MongoDB.Bson.ObjectId objectId))
        {
            return Results.BadRequest(new { error = "非法的评价 id。" });
        }
        if (string.IsNullOrWhiteSpace(reason.Reason))
        {
            return Results.BadRequest(new { error = "必须填写隐藏原因。" });
        }
        Review? review = await db.Reviews.Find(r => r.Id == objectId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (review is null)
        {
            return Results.NotFound();
        }
        await db.Reviews.UpdateOneAsync(r => r.Id == objectId,
            Builders<Review>.Update
                            .Set(r => r.IsHidden, true)
                            .Set(r => r.HiddenReason, reason.Reason)
                            .Set(r => r.HiddenAt, DateTime.UtcNow)
                            .Set(r => r.HiddenBySubject, user.Subject()),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        // 隐藏的评价不计入均值,分数必须跟着回调 —— 否则一条被隐藏的恶意 1 星会一直压着评分。
        await ReviewEndpoints.RecomputeRatingAsync(db, review.PluginId, cancellationToken).ConfigureAwait(false);
        logger.LogWarning("审核员 {Moderator} 隐藏了 {PluginId} 上 {Author} 的评价,原因:{Reason}",
            user.Subject(), review.PluginId, review.Subject, reason.Reason);
        return Results.NoContent();
    }

    private static async Task<IResult> UnhideReviewAsync(string id, MarketDbContext db,
        ClaimsPrincipal user, ILogger<ModerationAudit> logger, CancellationToken cancellationToken)
    {
        if (!MongoDB.Bson.ObjectId.TryParse(id, out MongoDB.Bson.ObjectId objectId))
        {
            return Results.BadRequest(new { error = "非法的评价 id。" });
        }
        Review? review = await db.Reviews.Find(r => r.Id == objectId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (review is null)
        {
            return Results.NotFound();
        }
        await db.Reviews.UpdateOneAsync(r => r.Id == objectId,
            Builders<Review>.Update
                            .Set(r => r.IsHidden, false)
                            .Set(r => r.HiddenReason, null)
                            .Set(r => r.HiddenAt, null)
                            .Set(r => r.HiddenBySubject, null),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await ReviewEndpoints.RecomputeRatingAsync(db, review.PluginId, cancellationToken).ConfigureAwait(false);
        logger.LogWarning("审核员 {Moderator} 取消隐藏了 {PluginId} 上 {Author} 的评价。",
            user.Subject(), review.PluginId, review.Subject);
        return Results.NoContent();
    }

    /// <summary>
    /// 彻底删除一条评价。**不可逆**,正文从数据库消失 ——
    /// 用于政治敏感等"留档本身就是负担"的内容。日常违规请用隐藏。
    ///
    /// 正文没了就没地方记原因了,所以原因只进日志。删除动作本身仍然留痕。
    /// </summary>
    private static async Task<IResult> PurgeReviewAsync(string id, [FromBody] ModerationReason reason,
        MarketDbContext db, ClaimsPrincipal user, ILogger<ModerationAudit> logger, CancellationToken cancellationToken)
    {
        if (!MongoDB.Bson.ObjectId.TryParse(id, out MongoDB.Bson.ObjectId objectId))
        {
            return Results.BadRequest(new { error = "非法的评价 id。" });
        }
        if (string.IsNullOrWhiteSpace(reason.Reason))
        {
            return Results.BadRequest(new { error = "必须填写删除原因(记入服务端日志)。" });
        }
        Review? review = await db.Reviews.Find(r => r.Id == objectId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (review is null)
        {
            return Results.NotFound();
        }
        await db.Reviews.DeleteOneAsync(r => r.Id == objectId, cancellationToken).ConfigureAwait(false);
        await ReviewEndpoints.RecomputeRatingAsync(db, review.PluginId, cancellationToken).ConfigureAwait(false);
        // 日志里不复述正文:这条路径存在的意义就是让那段文字消失。
        logger.LogWarning("审核员 {Moderator} 彻底删除了 {PluginId} 上 {Author} 的评价,原因:{Reason}",
            user.Subject(), review.PluginId, review.Subject, reason.Reason);
        return Results.NoContent();
    }
}

/// <summary>审核操作的日志类目。只为把这些动作归到一个可单独调级别的 logger 下。</summary>
public sealed class ModerationAudit;

/// <summary>审核操作的原因说明。</summary>
/// <param name="Reason">面向作者展示的原因,必填 —— 不给原因的驳回等于让人盲目重传。</param>
public sealed record ModerationReason(string Reason);

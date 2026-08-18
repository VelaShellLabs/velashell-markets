using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using VelaShell.Market.Api.Options;
using VelaShell.Market.Domain;
using VelaShell.Market.Infrastructure.Persistence;
using VelaShell.Market.Infrastructure.Scanning;
using VelaShell.Market.Infrastructure.Storage;

namespace VelaShell.Market.Api.Endpoints;

/// <summary>审核台:处理检测流水线判为"需人工复核"的包,以及下架与评价治理。</summary>
public static class ModerationEndpoints
{
    /// <summary>挂载端点。</summary>
    public static void MapModerationEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/moderation")
                                     .RequireAuthorization(MarketPolicies.Moderator)
                                     .WithTags("Moderation");

        group.MapGet("/queue", QueueAsync).WithSummary("待人工复核的版本(带完整检测报告)。");
        group.MapPost("/versions/{id}/approve", ApproveAsync).WithSummary("放行:搬进正式桶并发布。");
        group.MapPost("/versions/{id}/reject", RejectAsync).WithSummary("驳回:留在隔离区,记录原因。");
        group.MapPost("/plugins/{pluginId}/unlist", UnlistAsync).WithSummary("下架插件(不影响已安装用户)。");
    }

    private static async Task<IResult> QueueAsync(MarketDbContext db)
    {
        List<PluginVersion> pending = await db.Versions
                                              .Find(v => v.Status == PluginVersionStatus.Quarantined)
                                              .SortBy(v => v.UploadedAt)
                                              .Limit(200)
                                              .ToListAsync().ConfigureAwait(false);
        return Results.Ok(pending
                          .Where(v => v.Scan?.Verdict == ScanVerdict.NeedsReview)
                          .Select(v => new
                          {
                              id = v.Id.ToString(),
                              v.PluginId,
                              v.Version,
                              v.UploadedBySubject,
                              v.UploadedAt,
                              v.PackageSize,
                              signature = v.SignatureState,
                              findings = v.Scan!.Findings.Select(f => new { f.Code, severity = f.Severity.ToString(), f.Message, f.Path })
                          }));
    }

    private static async Task<IResult> ApproveAsync(string id, MarketDbContext db, PackageStorage storage, CancellationToken cancellationToken)
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
        report.Findings.Add(new("MANUAL_APPROVED", ScanSeverity.Info, "已由审核员人工放行。"));
        report.CompletedAt = DateTime.UtcNow;

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

    private static async Task<IResult> UnlistAsync(string pluginId, [FromBody] ModerationReason reason,
        MarketDbContext db, CancellationToken cancellationToken)
    {
        UpdateResult result = await db.Plugins.UpdateOneAsync(p => p.Id == pluginId,
            Builders<Plugin>.Update.Set(p => p.IsUnlisted, true).Set(p => p.UnlistedReason, reason.Reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.MatchedCount == 0 ? Results.NotFound() : Results.NoContent();
    }
}

/// <summary>审核操作的原因说明。</summary>
/// <param name="Reason">面向作者展示的原因,必填 —— 不给原因的驳回等于让人盲目重传。</param>
public sealed record ModerationReason(string Reason);

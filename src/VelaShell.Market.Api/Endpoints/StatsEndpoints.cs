using MongoDB.Bson;
using MongoDB.Driver;
using VelaShell.Market.Domain;
using VelaShell.Market.Infrastructure.Persistence;

namespace VelaShell.Market.Api.Endpoints;

/// <summary>
/// 站点概览数字。浏览页首屏拿它做三个数,匿名可读。
///
/// 这里刻意**不放"隔离区里还有几个包在等"这类运营内部数字** —— 它对访客没有意义,
/// 却会把审核积压情况暴露给所有人。露出去的三个都是访客真正关心的:
/// 有多少东西可以装、别人装了多少、以及有没有可疑包被放过。
/// </summary>
public static class StatsEndpoints
{
    /// <summary>挂载端点。</summary>
    public static void MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/stats", GetAsync)
           .AllowAnonymous()
           .WithTags("Stats")
           .WithSummary("站点概览:已上架插件数、已发布版本数、累计下载、被放行的可疑包数。");
    }

    private static async Task<IResult> GetAsync(MarketDbContext db, CancellationToken cancellationToken)
    {
        FilterDefinitionBuilder<Plugin> f = Builders<Plugin>.Filter;
        FilterDefinition<Plugin> listed = f.And(f.Eq(p => p.IsUnlisted, false), f.Ne(p => p.LatestVersion, null));

        long plugins = await db.Plugins.CountDocumentsAsync(listed, cancellationToken: cancellationToken).ConfigureAwait(false);
        long versions = await db.Versions
                                .CountDocumentsAsync(v => v.Status == PluginVersionStatus.Published, cancellationToken: cancellationToken)
                                .ConfigureAwait(false);

        // 累计下载在 plugins 上求和而不是在 versions 上:下架的插件不再计入对外展示的总量,
        // 而 Plugin.Downloads 本身就是各版本之和(下载端点对两处同时 $inc)。
        List<BsonDocument> sum = await db.Plugins.Aggregate<BsonDocument>(new BsonDocument[]
        {
            new("$match", new BsonDocument { { "IsUnlisted", false } }),
            new("$group", new BsonDocument { { "_id", BsonNull.Value }, { "total", new BsonDocument("$sum", "$Downloads") } })
        }, cancellationToken: cancellationToken).ToListAsync(cancellationToken).ConfigureAwait(false);
        long downloads = sum.Count == 0 ? 0 : sum[0]["total"].ToInt64();

        // 不变量:带阻断级发现的包永远不该出现在正式桶里。这个数字本来就该恒为 0,
        // 把它露在首屏是**故意**的 —— 哪天它不是 0 了,第一个看见的人就是访客。
        long blockingPublished = await db.Versions.CountDocumentsAsync(
            Builders<PluginVersion>.Filter.And(
                Builders<PluginVersion>.Filter.Eq(v => v.Status, PluginVersionStatus.Published),
                Builders<PluginVersion>.Filter.ElemMatch(v => v.Scan!.Findings,
                    Builders<ScanFinding>.Filter.Eq(finding => finding.Severity, ScanSeverity.Blocking))),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return Results.Ok(new { plugins, versions, downloads, blockingPublished });
    }
}

using MongoDB.Driver;
using MongoDB.Driver.Linq;
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
        //
        // 走 LINQ 而不是手写 BsonDocument 管道:字段名交给驱动从 Plugin 的映射翻译
        // (Downloads → downloads,库里是 camelCase)。这里原先手写成 "$Downloads" 与
        // "IsUnlisted",Mongo 不会报字段不存在 —— $match 先筛掉全部文档,累计下载于是恒为 0,
        // 接口照常 200。这类错误只有让驱动来写字段名才能在编译期挡掉。
        long downloads = await db.Plugins.AsQueryable()
                                 .Where(p => !p.IsUnlisted)
                                 .SumAsync(p => p.Downloads, cancellationToken).ConfigureAwait(false);

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

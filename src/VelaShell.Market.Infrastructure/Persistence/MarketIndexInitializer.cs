using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using VelaShell.Market.Domain;

namespace VelaShell.Market.Infrastructure.Persistence;

/// <summary>
/// 启动时建索引。刻意用**唯一索引**表达三条业务不变量,而不是靠应用层"先查再写":
/// 并发上传下那种检查必然有窗口,而这三条一旦破了,数据就没法自动修回来。
/// <list type="bullet">
///   <item>同一插件下版本号唯一 —— 否则同版本两份包,宿主装到哪一份全看运气;</item>
///   <item>每人每插件至多一条评价 —— 否则评分可以刷;</item>
///   <item>插件 id 本身就是主键(见 <see cref="Plugin.Id" />),重复上架由 <c>_id</c> 冲突挡掉。</item>
/// </list>
/// </summary>
public sealed class MarketIndexInitializer(MarketDbContext db, ILogger<MarketIndexInitializer> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.Versions.Indexes.CreateOneAsync(new CreateIndexModel<PluginVersion>(
                Builders<PluginVersion>.IndexKeys.Ascending(v => v.PluginId).Ascending(v => v.Version),
                new() { Unique = true, Name = "ux_plugin_version" }), cancellationToken: cancellationToken).ConfigureAwait(false);

            await db.Versions.Indexes.CreateOneAsync(new CreateIndexModel<PluginVersion>(
                Builders<PluginVersion>.IndexKeys.Ascending(v => v.Status).Ascending(v => v.UploadedAt),
                new() { Name = "ix_status_uploaded" }), cancellationToken: cancellationToken).ConfigureAwait(false);

            await db.Reviews.Indexes.CreateOneAsync(new CreateIndexModel<Review>(
                Builders<Review>.IndexKeys.Ascending(r => r.PluginId).Ascending(r => r.Subject),
                new() { Unique = true, Name = "ux_review_author" }), cancellationToken: cancellationToken).ConfigureAwait(false);

            await db.Plugins.Indexes.CreateOneAsync(new CreateIndexModel<Plugin>(
                Builders<Plugin>.IndexKeys.Ascending(p => p.Tags),
                new() { Name = "ix_tags" }), cancellationToken: cancellationToken).ConfigureAwait(false);

            await db.Plugins.Indexes.CreateOneAsync(new CreateIndexModel<Plugin>(
                Builders<Plugin>.IndexKeys.Ascending(p => p.OwnerSubject),
                new() { Name = "ix_owner" }), cancellationToken: cancellationToken).ConfigureAwait(false);

            // 检索:名称与简介的全文索引。中文分词 Mongo 原生支持有限,
            // 但对 id / 英文名 / 标签这类实际检索词已经够用;真要做中文检索再上 Atlas Search 或外部引擎。
            await db.Plugins.Indexes.CreateOneAsync(new CreateIndexModel<Plugin>(
                Builders<Plugin>.IndexKeys.Text(p => p.DisplayName).Text(p => p.Summary).Text(p => p.Id),
                new() { Name = "tx_search" }), cancellationToken: cancellationToken).ConfigureAwait(false);

            logger.LogInformation("Market indexes are in place.");
        }
        catch (MongoException ex)
        {
            // 建索引失败不该让服务起不来(索引可能正在后台构建,或权限受限),
            // 但必须吼出来 —— 唯一索引缺席时,上面那三条不变量就只剩应用层那层纸。
            logger.LogError(ex, "Failed to create market indexes; uniqueness invariants are NOT enforced by the database.");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

using MongoDB.Bson;
using MongoDB.Driver;
using VelaShell.Market.Api.Services;
using VelaShell.Market.Domain;
using VelaShell.Market.Infrastructure.Persistence;
using VelaShell.Market.Infrastructure.Scanning;
using VelaShell.Market.Infrastructure.Storage;

namespace VelaShell.Market.Api.Endpoints;

/// <summary>插件检索、详情与下载。全部匿名可读 —— 市场的浏览不该要求先登录。</summary>
public static class PluginEndpoints
{
    /// <summary>挂载端点。</summary>
    public static void MapPluginEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/plugins").WithTags("Plugins").AllowAnonymous();

        group.MapGet("/", SearchAsync).WithSummary("检索插件(关键词/标签/宿主兼容性,分页)。");
        group.MapGet("/{id}", DetailAsync).WithSummary("插件详情,含渲染后的 Markdown 与已发布版本列表。");
        group.MapGet("/{id}/versions/{version}/download", DownloadAsync).WithSummary("换取一个短时效下载 URL(仅正式桶)。");
        group.MapGet("/tags", TagsAsync).WithSummary("标签云。");
    }

    private static async Task<IResult> SearchAsync(
        MarketDbContext db,
        string? q,
        string? tag,
        int? apiLevel,
        string sort = "updated",
        int page = 1,
        int size = 20)
    {
        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 100);

        FilterDefinitionBuilder<Plugin> f = Builders<Plugin>.Filter;
        // 只列出确实有已发布版本的插件:一个还卡在隔离区的条目出现在检索里,点进去只会是空页。
        FilterDefinition<Plugin> filter = f.And(f.Eq(p => p.IsUnlisted, false), f.Ne(p => p.LatestVersion, null));
        if (!string.IsNullOrWhiteSpace(q))
        {
            filter = f.And(filter, f.Text(q));
        }
        if (!string.IsNullOrWhiteSpace(tag))
        {
            filter = f.And(filter, f.AnyEq(p => p.Tags, tag.ToLowerInvariant()));
        }
        if (apiLevel is { } level)
        {
            // 宿主只装 apiLevel 不高于自己的插件,检索也按同一口径过滤。
            filter = f.And(filter, f.Lte(p => p.LatestApiLevel, level));
        }

        SortDefinition<Plugin> order = sort switch
        {
            "downloads" => Builders<Plugin>.Sort.Descending(p => p.Downloads),
            "rating" => Builders<Plugin>.Sort.Descending(p => p.RatingAverage).Descending(p => p.RatingCount),
            "created" => Builders<Plugin>.Sort.Descending(p => p.CreatedAt),
            _ => Builders<Plugin>.Sort.Descending(p => p.UpdatedAt)
        };

        long total = await db.Plugins.CountDocumentsAsync(filter).ConfigureAwait(false);
        List<Plugin> items = await db.Plugins.Find(filter).Sort(order)
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
                excerpt = MarkdownRenderer.Excerpt(p.DescriptionMarkdown),
                p.Author,
                p.Tags,
                p.LatestVersion,
                p.LatestApiLevel,
                p.LatestMinHostVersion,
                p.Downloads,
                p.RatingAverage,
                p.RatingCount,
                p.UpdatedAt
            })
        });
    }

    private static async Task<IResult> DetailAsync(string id, MarketDbContext db, MarkdownRenderer markdown)
    {
        Plugin? plugin = await db.Plugins.Find(p => p.Id == id).FirstOrDefaultAsync().ConfigureAwait(false);
        if (plugin is null || plugin.IsUnlisted)
        {
            return Results.NotFound();
        }
        List<PluginVersion> versions = await db.Versions
                                               .Find(v => v.PluginId == id && v.Status == PluginVersionStatus.Published)
                                               .ToListAsync().ConfigureAwait(false);
        return Results.Ok(new
        {
            plugin.Id,
            plugin.DisplayName,
            plugin.Summary,
            descriptionHtml = markdown.ToHtml(plugin.DescriptionMarkdown),
            plugin.DescriptionMarkdown,
            plugin.Author,
            plugin.Publisher,
            plugin.Tags,
            plugin.Homepage,
            plugin.License,
            plugin.Downloads,
            plugin.RatingAverage,
            plugin.RatingCount,
            plugin.CreatedAt,
            plugin.UpdatedAt,
            versions = versions.OrderByDescending(v => v.Version, SemVerComparer.Instance).Select(v => new
            {
                v.Version,
                v.ApiLevel,
                v.MinHostVersion,
                v.HostMode,
                v.PackageSize,
                v.PayloadSha256,
                v.FileSha256,
                signature = v.SignatureState,
                releaseNotesHtml = markdown.ToHtml(v.ReleaseNotesMarkdown),
                v.PublishedAt,
                v.Downloads
            })
        });
    }

    /// <summary>
    /// 换取下载 URL。**只有 Published 的版本能走到这里** —— 隔离区里的包连预签名都签不出来
    /// (<see cref="PackageStorage.CreateDownloadUrl" /> 只认正式桶)。
    /// </summary>
    private static async Task<IResult> DownloadAsync(string id, string version, MarketDbContext db, PackageStorage storage)
    {
        PluginVersion? found = await db.Versions
                                       .Find(v => v.PluginId == id && v.Version == version && v.Status == PluginVersionStatus.Published)
                                       .FirstOrDefaultAsync().ConfigureAwait(false);
        if (found is null)
        {
            return Results.NotFound(new { error = "该版本不存在或尚未发布。" });
        }
        // 计数用原子 $inc:并发下载下"读-改-写"必然丢数。
        await db.Versions.UpdateOneAsync(v => v.Id == found.Id, Builders<PluginVersion>.Update.Inc(v => v.Downloads, 1)).ConfigureAwait(false);
        await db.Plugins.UpdateOneAsync(p => p.Id == id, Builders<Plugin>.Update.Inc(p => p.Downloads, 1)).ConfigureAwait(false);

        string url = storage.CreateDownloadUrl(found.ObjectKey, $"{id}-{version}.vpx");
        return Results.Ok(new
        {
            url,
            found.PayloadSha256,
            found.FileSha256,
            found.PackageSize,
            // 把校验和一并给出去:客户端(以及 vela-plugin)可以在装之前先核一遍。
            hint = "下载后可用 `vela-plugin verify` 核对容器完整性与签名。"
        });
    }

    /// <summary>
    /// 标签云。这里用**显式的 BsonDocument 管道**而不是强类型链式写法:
    /// unwind + group + sort 这一串在强类型 API 下的中间类型很难看懂,而管道本身只有四行,
    /// 直接写出来反而是最清楚的 —— 出问题时贴进 mongosh 就能跑。
    /// </summary>
    private static async Task<IResult> TagsAsync(MarketDbContext db)
    {
        PipelineDefinition<Plugin, BsonDocument> pipeline = new BsonDocument[]
        {
            new("$match", new BsonDocument { { "IsUnlisted", false }, { "LatestVersion", new BsonDocument("$ne", BsonNull.Value) } }),
            new("$unwind", "$Tags"),
            new("$group", new BsonDocument { { "_id", "$Tags" }, { "count", new BsonDocument("$sum", 1) } }),
            new("$sort", new BsonDocument("count", -1)),
            new("$limit", 100)
        };
        List<BsonDocument> tags = await db.Plugins.Aggregate(pipeline).ToListAsync().ConfigureAwait(false);
        return Results.Ok(tags.Select(t => new { tag = t["_id"].AsString, count = t["count"].AsInt32 }));
    }
}

using MongoDB.Driver;
using MongoDB.Driver.Linq;
using VelaShell.Market.Api.Services;
using VelaShell.Market.Domain;
using VelaShell.Market.Infrastructure.Persistence;
using VelaShell.Market.Infrastructure.Scanning;
using VelaShell.Market.Infrastructure.Storage;
using VelaShell.PluginSdk.Packaging;

namespace VelaShell.Market.Api.Endpoints;

/// <summary>插件检索、详情与下载。全部匿名可读 —— 市场的浏览不该要求先登录。</summary>
public static class PluginEndpoints
{
    /// <summary>挂载端点。</summary>
    public static void MapPluginEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/plugins").WithTags("Plugins").AllowAnonymous();

        group.MapGet("/", SearchAsync).WithSummary("检索插件(关键词/标签/宿主兼容性/是否推荐,分页)。");
        group.MapGet("/latest", LatestAsync).WithSummary("按 id 批量取每个插件的最新已发布版本(宿主检查更新用)。");
        group.MapGet("/{id}", DetailAsync).WithSummary("插件详情,含渲染后的 Markdown、已发布版本列表与公开的检测结论。");
        group.MapGet("/{id}/related", RelatedAsync).WithSummary("同一作者的其他插件与标签相近的插件。");
        group.MapGet("/{id}/versions/{version}/download", DownloadAsync).WithSummary("换取一个短时效下载 URL(仅正式桶)。");
        group.MapGet("/tags", TagsAsync).WithSummary("标签云。");
    }

    private static async Task<IResult> SearchAsync(
        MarketDbContext db,
        string? q,
        string? tag,
        int? apiLevel,
        bool? featured,
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
        if (featured is { } onlyFeatured)
        {
            filter = f.And(filter, f.Eq(p => p.IsFeatured, onlyFeatured));
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
        Dictionary<string, string> signatures = await LatestSignaturesAsync(db, items).ConfigureAwait(false);
        return Results.Ok(new
        {
            total,
            page,
            size,
            items = items.Select(p => Summarize(p, signatures))
        });
    }

    /// <summary>
    /// 列表卡片用的投影。卡片上要显示"已验签"这类结论,而签名状态挂在**版本**上,
    /// 所以走 <see cref="LatestSignaturesAsync" /> 一次批量取回,不在循环里逐个查库。
    /// </summary>
    private static object Summarize(Plugin p, IReadOnlyDictionary<string, string> signatures) => new
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
        latestSignature = p.LatestVersion is not null && signatures.TryGetValue(p.Id, out string? state) ? state : null,
        p.IsFeatured,
        p.Downloads,
        p.RatingAverage,
        p.RatingCount,
        p.UpdatedAt
    };

    /// <summary>
    /// 批量取回每个插件"最新已发布版本"的签名状态。
    ///
    /// 一次 <c>$in</c> 查完这一页的全部版本,再在内存里挑出与 <c>LatestVersion</c> 对得上的那条 ——
    /// 比在渲染循环里逐个 <c>FirstOrDefault</c> 少几十次往返,也不必为了一个展示字段
    /// 把签名状态冗余进 <see cref="Plugin" />(那样得在发布、撤回、强制下架三处同时维护)。
    /// </summary>
    private static async Task<Dictionary<string, string>> LatestSignaturesAsync(MarketDbContext db, IReadOnlyCollection<Plugin> plugins)
    {
        Dictionary<string, string> wanted = plugins.Where(p => p.LatestVersion is not null)
                                                   .ToDictionary(p => p.Id, p => p.LatestVersion!, StringComparer.Ordinal);
        if (wanted.Count == 0)
        {
            return [];
        }
        List<string> ids = [.. wanted.Keys];
        List<PluginVersion> versions = await db.Versions
                                               .Find(v => ids.Contains(v.PluginId) && v.Status == PluginVersionStatus.Published)
                                               .Project(v => new PluginVersion
                                               {
                                                   PluginId = v.PluginId,
                                                   Version = v.Version,
                                                   SignatureState = v.SignatureState,
                                                   PayloadSha256 = "",
                                                   FileSha256 = "",
                                                   ObjectKey = "",
                                                   UploadedBySubject = ""
                                               })
                                               .ToListAsync().ConfigureAwait(false);
        return versions
               .Where(v => wanted.TryGetValue(v.PluginId, out string? version) && string.Equals(version, v.Version, StringComparison.Ordinal))
               .GroupBy(v => v.PluginId, StringComparer.Ordinal)
               .ToDictionary(g => g.Key, g => g.First().SignatureState, StringComparer.Ordinal);
    }

    /// <summary>
    /// 相关插件。两路来源,前者优先:**同一作者的其他插件**,以及**标签重合最多的插件**。
    ///
    /// 这里刻意不叫"装了它的人也在用" —— 那需要安装/共现数据,而市场只看得到下载,
    /// 下载量推不出共现关系。把一个凑出来的推荐说成行为数据,是在骗读者。
    /// </summary>
    private static async Task<IResult> RelatedAsync(string id, MarketDbContext db, int size = 6)
    {
        size = Math.Clamp(size, 1, 20);
        Plugin? plugin = await db.Plugins.Find(p => p.Id == id).FirstOrDefaultAsync().ConfigureAwait(false);
        if (plugin is null)
        {
            return Results.NotFound();
        }

        FilterDefinitionBuilder<Plugin> f = Builders<Plugin>.Filter;
        FilterDefinition<Plugin> listed = f.And(
            f.Eq(p => p.IsUnlisted, false),
            f.Ne(p => p.LatestVersion, null),
            f.Ne(p => p.Id, id));

        List<Plugin> sameOwner = await db.Plugins
                                         .Find(f.And(listed, f.Eq(p => p.OwnerSubject, plugin.OwnerSubject)))
                                         .SortByDescending(p => p.Downloads)
                                         .Limit(size)
                                         .ToListAsync().ConfigureAwait(false);

        List<Plugin> sameTags = [];
        if (plugin.Tags.Count > 0 && sameOwner.Count < size)
        {
            List<string> seen = [.. sameOwner.Select(p => p.Id), id];
            sameTags = await db.Plugins
                               .Find(f.And(listed, f.AnyIn(p => p.Tags, plugin.Tags), f.Nin(p => p.Id, seen)))
                               .SortByDescending(p => p.RatingAverage)
                               .ThenByDescending(p => p.Downloads)
                               .Limit(size - sameOwner.Count)
                               .ToListAsync().ConfigureAwait(false);
        }

        List<Plugin> all = [.. sameOwner, .. sameTags];
        Dictionary<string, string> signatures = await LatestSignaturesAsync(db, all).ConfigureAwait(false);
        return Results.Ok(new
        {
            byAuthor = sameOwner.Select(p => Summarize(p, signatures)),
            byTags = sameTags.Select(p => Summarize(p, signatures))
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
            // 描述被审核员清空时,插件页要说清楚"这里为什么是空的" ——
            // 不然读者只会以为作者懒得写,而作者也不知道自己该改什么。
            plugin.DescriptionRemovedReason,
            plugin.Author,
            plugin.Publisher,
            ownerName = plugin.OwnerName,
            plugin.Tags,
            plugin.Homepage,
            plugin.License,
            plugin.IsFeatured,
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
                v.MinSdkVersion,
                v.HostMode,
                v.IdlePolicy,
                v.Entry,
                v.ActivationEvents,
                contributes = v.Contributes is null || v.Contributes.IsEmpty ? null : v.Contributes,
                v.PackageSize,
                v.PayloadSha256,
                v.FileSha256,
                signature = v.SignatureState,
                releaseNotesHtml = markdown.ToHtml(v.ReleaseNotesMarkdown),
                v.PublishedAt,
                v.Downloads,
                scan = PublicScan(v.Scan)
            })
        });
    }

    /// <summary>
    /// 已发布版本的**公开**检测结论。
    ///
    /// 这里只给结论与可复现性信息(判定、起止时间、引擎版本、条目数),
    /// **不下发 findings 原文** —— 那些是给上传者和审核员看的诊断信息,里面会带包内路径
    /// (如 <c>scripts/post-install.ps1</c>)。对已上架的包来说,公开这些等于把
    /// "这个包哪里值得注意"整理成一份现成的清单挂在详情页上。
    /// 想让访客安心的是"过了哪几关",不是"我们在它身上看见了什么"。
    /// </summary>
    private static object? PublicScan(ScanReport? scan) =>
        scan is null
            ? null
            : new
            {
                verdict = scan.Verdict.ToString(),
                scan.StartedAt,
                scan.CompletedAt,
                scan.Engines,
                scan.EntryCount,
                scan.UnpackedBytes
            };

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

    /// <summary>一次请求最多接受多少个 id。宿主装十几个插件是常态,上百个不是 —— 超了多半是脚本在刷。</summary>
    private const int MaxLatestIds = 200;

    /// <summary>
    /// 按 id 批量取「最新已发布版本」。宿主的插件管理页用它检查更新:一次带上本机装了的那几个 id,
    /// 拿回每个 id 的最新版与「装得上它需要什么」,在本地比对。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **刻意不做全量索引。** 插件多起来以后,让每个客户端为了看几个插件的版本去下整张表,
    /// 两头都在做无用功;按 id 问,请求规模只跟本机装了几个插件有关,与商店有多大无关。
    /// </para>
    /// <para>
    /// 已下架(<see cref="Plugin.IsUnlisted" />)的插件**不出现在结果里**,与详情接口 404 的口径一致:
    /// 下架的意思是"别再让人装到它",已装的用户照旧能用,但不该被推着往前升。
    /// 商店里没有的 id 同样静默略过 —— 手工旁装的、私有的插件本来就查不到,那不是错误。
    /// </para>
    /// </remarks>
    /// <param name="ids">逗号分隔的插件 id。</param>
    /// <param name="db">数据库上下文。</param>
    /// <param name="pre">是否把预发布版本也算进来(对应 <c>vela-plugin --pre</c> 与宿主的 preview 通道)。</param>
    private static async Task<IResult> LatestAsync(string? ids, MarketDbContext db, bool pre = false)
    {
        List<string> wanted = [.. (ids ?? "")
                                 .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                 .Distinct(StringComparer.Ordinal)];
        if (wanted.Count == 0)
        {
            return Results.BadRequest(new { error = "ids 不能为空:用逗号分隔要查的插件 id。" });
        }
        if (wanted.Count > MaxLatestIds)
        {
            return Results.BadRequest(new { error = $"一次最多查 {MaxLatestIds} 个 id,本次给了 {wanted.Count} 个。" });
        }

        // 两步走:先在 Plugins 里滤掉下架的,再按放行的 id 查版本 —— 下架的插件连版本都不必取。
        List<string> listed = await db.Plugins
                                      .Find(p => wanted.Contains(p.Id) && !p.IsUnlisted)
                                      .Project(p => p.Id)
                                      .ToListAsync().ConfigureAwait(false);
        if (listed.Count == 0)
        {
            return Results.Ok(new { plugins = new Dictionary<string, object>(StringComparer.Ordinal) });
        }
        List<PluginVersion> versions = await db.Versions
                                               .Find(v => listed.Contains(v.PluginId) && v.Status == PluginVersionStatus.Published)
                                               .ToListAsync().ConfigureAwait(false);
        Dictionary<string, object> plugins = versions
                                             .GroupBy(v => v.PluginId, StringComparer.Ordinal)
                                             .Select(g => SelectLatest(g, pre))
                                             .OfType<PluginVersion>()
                                             .ToDictionary(v => v.PluginId, Summarize, StringComparer.Ordinal);
        return Results.Ok(new { plugins });
    }

    /// <summary>检查更新要用到的那几个字段。故意比详情页窄:这是给机器比对的,不是给人读的。</summary>
    private static object Summarize(PluginVersion v) => new
    {
        v.Version,
        v.ApiLevel,
        v.MinHostVersion,
        v.MinSdkVersion,
        v.HostMode,
        signature = v.SignatureState,
        // 给指纹而不是公钥本身:宿主钉住的就是指纹,拿它就能在**下载之前**看出这一版是不是
        // 换了发布者。换了人的那一版不该悄悄装上去,而"先下完再拒"等于白费一次流量。
        publisherFingerprint = v.SignaturePublicKey is { Length: > 0 } key
            ? VpxContainer.PublicKeyFingerprint(key)
            : null,
        v.PayloadSha256,
        v.FileSha256,
        v.PackageSize,
        v.PublishedAt
    };

    /// <summary>
    /// 从一个插件的已发布版本里挑出「最新的那个」:默认跳过预发布(版本号带 <c>-</c> 后缀),
    /// <paramref name="includePreRelease" /> 为真时一视同仁。一个正式版都没有时退回最新的预发布。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与 <c>vela-plugin</c> 的选版规则**逐条对齐**(它那边叫 <c>SelectVersion</c>),
    /// 末尾那条"全是预发布就放宽"也与宿主自更新的稳定通道一致。三处口径不同会得到
    /// "命令行说该升、管理页说没变"这种没法向用户解释的结果。
    /// </para>
    /// <para>
    /// <see cref="Plugin.LatestVersion" /> **不能**拿来顶替这里:它是按 semver 排序取最高的那个,
    /// 预发布也算在内 —— 作者一发 beta,所有人的"最新版"就变成了那个 beta。
    /// </para>
    /// </remarks>
    /// <param name="published">同一个插件的全部已发布版本。</param>
    /// <param name="includePreRelease">是否把预发布版本也算进来。</param>
    public static PluginVersion? SelectLatest(IEnumerable<PluginVersion> published, bool includePreRelease)
    {
        PluginVersion[] ordered = [.. published.OrderByDescending(v => v.Version, SemVerComparer.Instance)];
        if (ordered.Length == 0)
        {
            return null;
        }
        return includePreRelease
            ? ordered[0]
            : ordered.FirstOrDefault(v => !v.Version.Contains('-', StringComparison.Ordinal)) ?? ordered[0];
    }

    /// <summary>
    /// 标签云。
    ///
    /// 这里原先是一条手写的 BsonDocument 管道,理由是"贴进 mongosh 就能跑"。代价是字段名
    /// 不再由驱动从映射翻译,而库里是 camelCase:管道里写着 <c>$Tags</c> / <c>IsUnlisted</c>,
    /// Mongo 不报字段不存在,标签云就一直是空列表。想看实际管道,对 queryable 调 ToString()
    /// 就能拿到 —— 用不着为此把字段名手写一遍。
    /// </summary>
    private static async Task<IResult> TagsAsync(MarketDbContext db)
    {
        var tags = await db.Plugins.AsQueryable()
                           .Where(p => !p.IsUnlisted && p.LatestVersion != null)
                           .SelectMany(p => p.Tags)
                           .GroupBy(tag => tag)
                           .Select(g => new { tag = g.Key, count = g.Count() })
                           .OrderByDescending(t => t.count)
                           .Take(100)
                           .ToListAsync().ConfigureAwait(false);
        return Results.Ok(tags);
    }
}

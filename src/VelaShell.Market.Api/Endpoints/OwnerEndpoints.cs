using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using VelaShell.Market.Api.Options;
using VelaShell.Market.Domain;
using VelaShell.Market.Infrastructure.Persistence;
using VelaShell.Market.Infrastructure.Storage;

namespace VelaShell.Market.Api.Endpoints;

/// <summary>插件页面的可编辑字段(只有拥有者能改)。</summary>
/// <param name="DescriptionMarkdown">详细描述,Markdown 原文。</param>
/// <param name="Tags">标签,逗号分隔。</param>
/// <param name="Homepage">主页地址。</param>
public sealed record PluginEditRequest(string? DescriptionMarkdown, string? Tags, string? Homepage);

/// <summary>
/// 拥有者对自己插件的操作。与审核台分开:审核员管的是"这个包该不该在市场上",
/// 拥有者管的是"我的插件页面长什么样、哪个版本还算数"。
/// </summary>
public static class OwnerEndpoints
{
    /// <summary>挂载端点。</summary>
    public static void MapOwnerEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/plugins/{id}").RequireAuthorization().WithTags("Owner");

        group.MapPut("/", EditAsync).WithSummary("修改插件页面(描述/标签/主页)。");
        group.MapPost("/versions/{version}/withdraw", WithdrawAsync)
             .WithSummary("撤回某个已发布版本:从正式桶移除,不再出现在列表与下载。");
    }

    private static async Task<IResult> EditAsync(string id, [FromBody] PluginEditRequest request,
        ClaimsPrincipal user, MarketDbContext db, CancellationToken cancellationToken)
    {
        Plugin? plugin = await db.Plugins.Find(p => p.Id == id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (plugin is null)
        {
            return Results.NotFound();
        }
        if (!string.Equals(plugin.OwnerSubject, user.Subject(), StringComparison.Ordinal))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, detail: "只有插件拥有者能修改。");
        }
        if (request.DescriptionMarkdown is { Length: > 100_000 })
        {
            return Results.BadRequest(new { error = "描述最多 100000 字符。" });
        }

        UpdateDefinition<Plugin> update = Builders<Plugin>.Update.Set(p => p.UpdatedAt, DateTime.UtcNow);
        if (request.DescriptionMarkdown is not null)
        {
            update = update.Set(p => p.DescriptionMarkdown, request.DescriptionMarkdown)
                           // 作者重写了描述,审核员那条"描述因违规已被移除"的说明就该消失 ——
                           // 留着它等于永远给这个插件挂着一块牌子,即便问题早已改掉。
                           .Set(p => p.DescriptionRemovedReason, null)
                           .Set(p => p.DescriptionRemovedAt, null);
        }
        if (request.Tags is not null)
        {
            update = update.Set(p => p.Tags, TagList.Normalize(request.Tags));
        }
        if (request.Homepage is not null)
        {
            update = update.Set(p => p.Homepage, request.Homepage);
        }
        await db.Plugins.UpdateOneAsync(p => p.Id == id, update, cancellationToken: cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    /// <summary>
    /// 撤回一个版本。**从正式桶物理删除** —— 只标记状态的话,预签名 URL 的有效期内它仍然可下载,
    /// 而撤回的典型原因(发错包、含敏感信息)恰恰不能接受"再放十分钟"。
    /// </summary>
    private static async Task<IResult> WithdrawAsync(string id, string version, ClaimsPrincipal user,
        MarketDbContext db, PackageStorage storage, IOptions<MarketAuthOptions> auth, CancellationToken cancellationToken)
    {
        Plugin? plugin = await db.Plugins.Find(p => p.Id == id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (plugin is null)
        {
            return Results.NotFound();
        }
        string subject = user.Subject();
        if (!string.Equals(plugin.OwnerSubject, subject, StringComparison.Ordinal)
            && !auth.Value.ModeratorSubjects.Contains(subject))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, detail: "只有插件拥有者或审核员能撤回版本。");
        }
        PluginVersion? found = await db.Versions
                                       .Find(v => v.PluginId == id && v.Version == version && v.Status == PluginVersionStatus.Published)
                                       .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (found is null)
        {
            return Results.NotFound(new { error = "该版本不存在或未处于已发布状态。" });
        }

        await storage.DeletePublicAsync(found.ObjectKey, cancellationToken).ConfigureAwait(false);
        await db.Versions.UpdateOneAsync(v => v.Id == found.Id,
            Builders<PluginVersion>.Update.Set(v => v.Status, PluginVersionStatus.Withdrawn),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // 撤回的可能正是"最新版本",插件条目上的展示信息必须跟着回退到仍然有效的那个。
        List<PluginVersion> remaining = await db.Versions
                                                .Find(v => v.PluginId == id && v.Status == PluginVersionStatus.Published)
                                                .ToListAsync(cancellationToken).ConfigureAwait(false);
        PluginVersion? latest = remaining.OrderByDescending(v => v.Version, Infrastructure.Scanning.SemVerComparer.Instance).FirstOrDefault();
        await db.Plugins.UpdateOneAsync(p => p.Id == id, Builders<Plugin>.Update
                                                         .Set(p => p.LatestVersion, latest?.Version)
                                                         .Set(p => p.LatestApiLevel, latest?.ApiLevel)
                                                         .Set(p => p.LatestMinHostVersion, latest?.MinHostVersion)
                                                         .Set(p => p.UpdatedAt, DateTime.UtcNow),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }
}

/// <summary>标签归一。上传与编辑两处都要用同一套规则,否则同一个插件的标签会因为改了哪一边而不同。</summary>
public static class TagList
{
    /// <summary>小写、去空白、去重、限长限量。标签是检索维度,不是自由文本。</summary>
    public static List<string> Normalize(string? tags) =>
        (tags ?? "").Split([',', ';', ' ', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(t => t.ToLowerInvariant())
                    .Where(t => t.Length <= 32)
                    .Distinct(StringComparer.Ordinal)
                    .Take(10)
                    .ToList();
}

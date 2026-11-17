using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using VelaShell.Market.Api.Options;
using VelaShell.Market.Api.Services;
using VelaShell.Market.Domain;
using VelaShell.Market.Infrastructure.Persistence;

namespace VelaShell.Market.Api.Endpoints;

/// <summary>登记可信发布者的请求。</summary>
/// <param name="PluginIds">登记到哪些插件(都必须归当前用户所有)。一个仓库发多个插件时一次登记完。</param>
/// <param name="Repository">GitHub 仓库 <c>owner/repo</c>。</param>
/// <param name="Workflow">工作流文件名,如 <c>release.yml</c>。</param>
/// <param name="Environment">可选的部署环境名。</param>
public sealed record TrustedPublisherRequest(string[]? PluginIds, string? Repository, string? Workflow, string? Environment);

/// <summary>
/// 可信发布者的登记与移除。只有插件拥有者能做 —— 登记一条等于「授权这个工作流替我发版」,
/// 这件事必须是登录的拥有者本人决定的。
/// </summary>
public static class TrustedPublisherEndpoints
{
    /// <summary>
    /// <see cref="Plugin.TrustedPublishers" /> 在库里的字段名。只有「数组第 N 个元素存不存在」这种
    /// 没法用表达式写的过滤才需要它;第一次用到时类映射早已在启动期建好。
    /// </summary>
    private static string PublishersElement =>
        MongoDB.Bson.Serialization.BsonClassMap.LookupClassMap(typeof(Plugin))
               .GetMemberMap(nameof(Plugin.TrustedPublishers)).ElementName;

    /// <summary>挂载端点。</summary>
    public static void MapTrustedPublisherEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me/trusted-publishing", Settings)
           .RequireAuthorization().WithTags("Owner")
           .WithSummary("商店的可信发布设置:是否启用、令牌受众。「我的插件」页据此决定显不显示登记入口、生成工作流示例。");

        app.MapPost("/api/me/trusted-publishers", AddAsync)
           .RequireAuthorization().WithTags("Owner")
           .WithSummary("给自己的一个或多个插件登记可信发布者(GitHub 仓库 + 工作流 + 可选环境)。");

        app.MapDelete("/api/plugins/{id}/trusted-publishers/{publisherId}", RemoveAsync)
           .RequireAuthorization().WithTags("Owner")
           .WithSummary("移除一条可信发布者。");
    }

    private static IResult Settings(IOptions<GitHubOidcOptions> options) =>
        Results.Ok(new
        {
            enabled = options.Value.Enabled,
            audience = options.Value.Audience,
            uploadPath = "/api/uploads/trusted"
        });

    private static async Task<IResult> AddAsync(
        [FromBody] TrustedPublisherRequest request,
        ClaimsPrincipal user,
        MarketDbContext db,
        GitHubRepositoryResolver resolver,
        IOptions<GitHubOidcOptions> options,
        CancellationToken cancellationToken)
    {
        GitHubOidcOptions o = options.Value;
        if (!o.Enabled)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, detail: "本商店没有启用可信发布。");
        }

        string[] pluginIds = (request.PluginIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (pluginIds.Length == 0)
        {
            return Results.BadRequest(new { error = "请至少选择一个插件。" });
        }
        string repository = request.Repository?.Trim() ?? "";
        string workflow = TrustedPublishing.NormalizeWorkflow(request.Workflow ?? "");
        string? environment = string.IsNullOrWhiteSpace(request.Environment) ? null : request.Environment.Trim();
        string? invalid = TrustedPublishing.ExplainRepository(repository)
                          ?? TrustedPublishing.ExplainWorkflow(workflow)
                          ?? TrustedPublishing.ExplainEnvironment(environment);
        if (invalid is not null)
        {
            return Results.BadRequest(new { error = invalid });
        }

        string subject = user.Subject();
        List<Plugin> plugins = await db.Plugins.Find(p => pluginIds.Contains(p.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        string[] notOwned = pluginIds.Where(id => !plugins.Any(p => p.Id == id && string.Equals(p.OwnerSubject, subject, StringComparison.Ordinal))).ToArray();
        if (notOwned.Length > 0)
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                detail: $"这些插件不存在或不归你所有:{string.Join(", ", notOwned)}。");
        }

        // 查一次就够:同一个仓库登记到几个插件上,id 是同一个。
        GitHubRepositoryLookup lookup = await resolver.ResolveAsync(repository, cancellationToken).ConfigureAwait(false);
        if (lookup.Identity is not { } repo)
        {
            return Results.BadRequest(new { error = lookup.Error });
        }

        var added = new List<object>();
        var skipped = new List<object>();
        foreach (Plugin plugin in plugins)
        {
            bool duplicate = plugin.TrustedPublishers.Any(p => p.RepositoryId == repo.RepositoryId
                                                               && p.Workflow == workflow
                                                               && string.Equals(p.Environment, environment, StringComparison.OrdinalIgnoreCase));
            if (duplicate)
            {
                skipped.Add(new { pluginId = plugin.Id, reason = "已登记过同一条" });
                continue;
            }
            var publisher = new TrustedPublisher
            {
                PublisherId = Guid.NewGuid().ToString("N"),
                Repository = repo.FullName,
                RepositoryId = repo.RepositoryId,
                RepositoryOwnerId = repo.OwnerId,
                Workflow = workflow,
                Environment = environment,
                AddedBySubject = subject
            };
            // 上限放进过滤条件里:并发登记时也不会超出(数组长度检查与 $push 在同一次原子更新里)。
            // 「第 N 个元素不存在」= 长度 < N。字段名向类映射要,不手写 —— EasilyNET 的约定会把它改成驼峰。
            UpdateResult result = await db.Plugins.UpdateOneAsync(
                Builders<Plugin>.Filter.Eq(p => p.Id, plugin.Id)
                & Builders<Plugin>.Filter.Eq(p => p.OwnerSubject, subject)
                & Builders<Plugin>.Filter.Exists($"{PublishersElement}.{o.MaxPublishersPerPlugin - 1}", exists: false),
                Builders<Plugin>.Update.Push(p => p.TrustedPublishers, publisher),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result.ModifiedCount == 0)
            {
                skipped.Add(new { pluginId = plugin.Id, reason = $"已达上限 {o.MaxPublishersPerPlugin} 条" });
                continue;
            }
            added.Add(new { pluginId = plugin.Id, publisherId = publisher.PublisherId });
        }

        return Results.Ok(new { repository = repo.FullName, workflow, environment, added, skipped });
    }

    private static async Task<IResult> RemoveAsync(string id, string publisherId, ClaimsPrincipal user, MarketDbContext db, CancellationToken cancellationToken)
    {
        Plugin? plugin = await db.Plugins.Find(p => p.Id == id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (plugin is null)
        {
            return Results.NotFound();
        }
        if (!string.Equals(plugin.OwnerSubject, user.Subject(), StringComparison.Ordinal))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, detail: "只有插件拥有者能移除可信发布者。");
        }
        UpdateResult result = await db.Plugins.UpdateOneAsync(p => p.Id == id,
            Builders<Plugin>.Update.PullFilter(p => p.TrustedPublishers, t => t.PublisherId == publisherId),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.ModifiedCount == 0 ? Results.NotFound() : Results.NoContent();
    }
}

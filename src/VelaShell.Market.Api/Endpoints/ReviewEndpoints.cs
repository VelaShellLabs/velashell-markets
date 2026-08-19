using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using VelaShell.Market.Api.Services;
using VelaShell.Market.Domain;
using VelaShell.Market.Infrastructure.Persistence;

namespace VelaShell.Market.Api.Endpoints;

/// <summary>评价请求体。</summary>
/// <param name="Rating">1–5。</param>
/// <param name="Body">Markdown 正文,可空。</param>
public sealed record ReviewRequest(int Rating, string? Body);

/// <summary>评价系统。每人每插件一条(数据库唯一索引保证),可改可删。</summary>
public static class ReviewEndpoints
{
    /// <summary>挂载端点。</summary>
    public static void MapReviewEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/plugins/{id}/reviews").WithTags("Reviews");

        group.MapGet("/", ListAsync).AllowAnonymous().WithSummary("某插件的评价列表(分页)。");
        group.MapGet("/mine", MineAsync).RequireAuthorization().WithSummary("我对该插件的评价(没有则 204)。");
        group.MapPut("/", UpsertAsync).RequireAuthorization().WithSummary("发表或修改我的评价。");
        group.MapDelete("/", DeleteAsync).RequireAuthorization().WithSummary("删除我的评价。");
    }

    /// <summary>
    /// 我对该插件的评价。单独一个端点而不是在列表里标记"这条是我的":
    /// 列表是分页的,我的那条可能在第 7 页,前端没法据此把表单预填成"修改"。
    /// </summary>
    private static async Task<IResult> MineAsync(string id, ClaimsPrincipal user, MarketDbContext db, CancellationToken cancellationToken)
    {
        string subject = user.Subject();
        Review? mine = await db.Reviews.Find(r => r.PluginId == id && r.Subject == subject)
                               .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return mine is null
            ? Results.NoContent()
            : Results.Ok(new { mine.Rating, body = mine.BodyMarkdown, mine.UpdatedAt });
    }

    private static async Task<IResult> ListAsync(string id, MarketDbContext db, MarkdownRenderer markdown, int page = 1, int size = 20)
    {
        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 100);
        FilterDefinition<Review> filter = Builders<Review>.Filter.And(
            Builders<Review>.Filter.Eq(r => r.PluginId, id),
            Builders<Review>.Filter.Eq(r => r.IsHidden, false));
        long total = await db.Reviews.CountDocumentsAsync(filter).ConfigureAwait(false);
        // 各星级的条数。前端用它画"应用商店式"的评分分布条 —— 光有均值看不出口碑是两极还是一致。
        Dictionary<int, int> distribution = (await db.Reviews.Aggregate()
                                                     .Match(filter)
                                                     .Group(r => r.Rating, g => new { Rating = g.Key, Count = g.Count() })
                                                     .ToListAsync().ConfigureAwait(false))
            .ToDictionary(g => g.Rating, g => g.Count);
        List<Review> items = await db.Reviews.Find(filter)
                                     .SortByDescending(r => r.UpdatedAt)
                                     .Skip((page - 1) * size).Limit(size)
                                     .ToListAsync().ConfigureAwait(false);
        return Results.Ok(new
        {
            total,
            page,
            size,
            distribution,
            items = items.Select(r => new
            {
                r.DisplayName,
                r.Rating,
                bodyHtml = markdown.ToHtml(r.BodyMarkdown),
                r.PluginVersion,
                r.CreatedAt,
                r.UpdatedAt
            })
        });
    }

    private static async Task<IResult> UpsertAsync(string id, [FromBody] ReviewRequest request,
        ClaimsPrincipal user, MarketDbContext db, CancellationToken cancellationToken)
    {
        if (request.Rating is < 1 or > 5)
        {
            return Results.BadRequest(new { error = "评分必须在 1–5 之间。" });
        }
        if (request.Body is { Length: > 5000 })
        {
            return Results.BadRequest(new { error = "评价正文最多 5000 字符。" });
        }
        Plugin? plugin = await db.Plugins.Find(p => p.Id == id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (plugin is null)
        {
            return Results.NotFound();
        }
        string subject = user.Subject();
        if (string.Equals(subject, plugin.OwnerSubject, StringComparison.Ordinal))
        {
            // 作者给自己打分毫无信息量,而且是最容易被滥用的一条路径。
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, detail: "不能评价自己发布的插件。");
        }

        UpdateDefinition<Review> update = Builders<Review>.Update
                                                          .SetOnInsert(r => r.PluginId, id)
                                                          .SetOnInsert(r => r.Subject, subject)
                                                          .SetOnInsert(r => r.CreatedAt, DateTime.UtcNow)
                                                          .Set(r => r.DisplayName, user.FindFirstValue("name") ?? user.FindFirstValue("preferred_username") ?? "匿名用户")
                                                          .Set(r => r.Rating, request.Rating)
                                                          .Set(r => r.BodyMarkdown, request.Body)
                                                          .Set(r => r.PluginVersion, plugin.LatestVersion)
                                                          .Set(r => r.UpdatedAt, DateTime.UtcNow);
        await db.Reviews.UpdateOneAsync(r => r.PluginId == id && r.Subject == subject, update,
            new UpdateOptions { IsUpsert = true }, cancellationToken).ConfigureAwait(false);

        await RecomputeRatingAsync(db, id, cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(string id, ClaimsPrincipal user, MarketDbContext db, CancellationToken cancellationToken)
    {
        string subject = user.Subject();
        DeleteResult result = await db.Reviews.DeleteOneAsync(r => r.PluginId == id && r.Subject == subject, cancellationToken).ConfigureAwait(false);
        if (result.DeletedCount == 0)
        {
            return Results.NotFound();
        }
        await RecomputeRatingAsync(db, id, cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    /// <summary>
    /// 重算评分。刻意**整体重算**而不是增量维护均值:评价可改可删可隐藏,
    /// 增量维护要处理的边界比一次聚合多得多,而这个量级下聚合的代价可以忽略。
    /// </summary>
    internal static async Task RecomputeRatingAsync(MarketDbContext db, string pluginId, CancellationToken cancellationToken)
    {
        List<Review> reviews = await db.Reviews.Find(r => r.PluginId == pluginId && !r.IsHidden)
                                       .ToListAsync(cancellationToken).ConfigureAwait(false);
        double average = reviews.Count == 0 ? 0 : Math.Round(reviews.Average(r => r.Rating), 2);
        await db.Plugins.UpdateOneAsync(p => p.Id == pluginId,
            Builders<Plugin>.Update.Set(p => p.RatingAverage, average).Set(p => p.RatingCount, reviews.Count),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}

using System.Security.Claims;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using VelaShell.Market.Api.Options;
using VelaShell.Market.Domain;
using VelaShell.Market.Infrastructure.Persistence;

namespace VelaShell.Market.Api.Endpoints;

/// <summary>当前用户与"我的内容"。前端靠 <c>/api/me</c> 决定导航栏显示什么、审核入口露不露。</summary>
public static class AccountEndpoints
{
    /// <summary>挂载端点。</summary>
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/me").RequireAuthorization().WithTags("Account");

        group.MapGet("/", MeAsync).WithSummary("当前登录用户,含是否为审核员。");
        group.MapGet("/plugins", MyPluginsAsync).WithSummary("我拥有的插件。");
    }

    private static IResult MeAsync(ClaimsPrincipal user, IOptions<MarketAuthOptions> auth)
    {
        string subject = user.Subject();
        return Results.Ok(new
        {
            subject,
            name = user.FindFirstValue("name") ?? user.FindFirstValue("preferred_username") ?? subject,
            email = user.FindFirstValue("email"),
            // 前端据此决定要不要显示审核台入口。真正的授权在服务端策略上,这里只是少给用户一个点了会 403 的按钮。
            isModerator = auth.Value.ModeratorSubjects.Contains(subject)
        });
    }

    private static async Task<IResult> MyPluginsAsync(ClaimsPrincipal user, MarketDbContext db, CancellationToken cancellationToken)
    {
        string subject = user.Subject();
        List<Plugin> plugins = await db.Plugins.Find(p => p.OwnerSubject == subject)
                                       .SortByDescending(p => p.UpdatedAt)
                                       .ToListAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(plugins.Select(p => new
        {
            p.Id,
            p.DisplayName,
            p.Summary,
            p.DescriptionMarkdown,
            p.Tags,
            p.Homepage,
            p.License,
            p.LatestVersion,
            p.Downloads,
            p.RatingAverage,
            p.RatingCount,
            p.IsUnlisted,
            p.UnlistedReason,
            p.UpdatedAt
        }));
    }
}

/// <summary>身份主体的取值约定。</summary>
public static class PrincipalExtensions
{
    /// <summary>
    /// 取身份主体。OIDC 的 <c>sub</c> 在 ASP.NET 里常被映射成 <see cref="ClaimTypes.NameIdentifier" />,
    /// 两个都看一遍 —— 只认一个的话,换一次 IdentityServer 的声明映射就会让全站的归属判断失效。
    /// </summary>
    public static string Subject(this ClaimsPrincipal user) =>
        user.FindFirstValue("sub")
        ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Authenticated principal has no subject claim.");
}

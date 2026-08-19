using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using VelaShell.Market.Identity.Accounts;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace VelaShell.Market.Identity.Endpoints;

/// <summary>
/// OIDC 协议端点。OpenIddict 负责协议本身(参数校验、客户端校验、PKCE、令牌签发),
/// 这里只回答它答不了的那一个问题:**这次请求背后是谁**。
/// </summary>
public static class ConnectEndpoints
{
    /// <summary>挂上授权、令牌、用户信息与退出登录四个端点。</summary>
    public static void MapConnectEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/connect").ExcludeFromDescription();

        group.MapMethods("/authorize", ["GET", "POST"], AuthorizeAsync).DisableAntiforgery();
        group.MapPost("/token", ExchangeAsync).DisableAntiforgery();
        // 强制转成 Delegate:SignOutAsync 只收一个 HttpContext,不转的话会被当成 RequestDelegate,
        // 返回的 IResult 会被直接丢掉 —— 表现为"点退出登录什么也没发生"。
        group.MapMethods("/endsession", ["GET", "POST"], (Delegate)SignOutAsync).DisableAntiforgery();
        group.MapMethods("/userinfo", ["GET", "POST"], UserInfoAsync)
             .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = OpenIddictServerAspNetCoreDefaults.AuthenticationScheme })
             .DisableAntiforgery();
    }

    /// <summary>
    /// 授权端点。没登录就把人交给登录页,登录过就直接签发授权码 ——
    /// 市场前端是第一方应用(ConsentTypes.Implicit),不再多问一次"是否授权"。
    /// </summary>
    private static async Task<IResult> AuthorizeAsync(HttpContext context, AccountStore accounts,
                                                      IOpenIddictScopeManager scopes, CancellationToken cancel)
    {
        OpenIddictRequest request = context.GetOpenIddictServerRequest()
                                    ?? throw new InvalidOperationException("当前请求不是一个 OpenID Connect 授权请求。");

        AuthenticateResult result = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        bool expired = request.MaxAge is { } maxAge && result.Properties?.IssuedUtc is { } issued &&
                       DateTimeOffset.UtcNow - issued > TimeSpan.FromSeconds(maxAge);

        if (!result.Succeeded || expired || request.HasPromptValue(PromptValues.Login))
        {
            // prompt=none 的意思是"不许打扰用户"。静默续期失败时必须按协议报错,不能弹登录页 ——
            // 那个请求是在 iframe 里发的,弹出来用户也看不见。
            if (request.HasPromptValue(PromptValues.None))
            {
                return Results.Forbid(Problem(Errors.LoginRequired, "用户尚未登录。"),
                    [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
            }

            return Results.Challenge(new AuthenticationProperties
            {
                // 原样带回来:登录成功后要接着走完这次授权请求,而不是把人丢在首页。
                RedirectUri = context.Request.PathBase + context.Request.Path +
                              QueryString.Create(context.Request.HasFormContentType
                                                     ? context.Request.Form.ToList()
                                                     : context.Request.Query.ToList())
            }, [CookieAuthenticationDefaults.AuthenticationScheme]);
        }

        string? subject = result.Principal?.GetClaim(Claims.Subject);
        MarketAccount? account = subject is null ? null : await accounts.FindByIdAsync(subject, cancel);

        // Cookie 还在但账号已经没了或被停用:清掉 cookie 再走一遍登录,别让一张过期的凭据换出新令牌。
        if (account is null || account.IsDisabled)
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Challenge(new AuthenticationProperties { RedirectUri = "/" },
                [CookieAuthenticationDefaults.AuthenticationScheme]);
        }

        ClaimsIdentity identity = CreateIdentity(account);
        identity.SetScopes(request.GetScopes());
        identity.SetResources(await scopes.ListResourcesAsync(identity.GetScopes(), cancel).ToListAsync(cancel));
        identity.SetDestinations(GetDestinations);

        return Results.SignIn(new(identity), null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>令牌端点:用授权码或刷新令牌换访问令牌。</summary>
    private static async Task<IResult> ExchangeAsync(HttpContext context, AccountStore accounts, CancellationToken cancel)
    {
        OpenIddictRequest request = context.GetOpenIddictServerRequest()
                                    ?? throw new InvalidOperationException("当前请求不是一个 OpenID Connect 令牌请求。");

        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
        {
            return Results.Forbid(Problem(Errors.UnsupportedGrantType, "只支持授权码与刷新令牌两种许可类型。"),
                [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        AuthenticateResult result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        ClaimsPrincipal? granted = result.Principal;
        string? subject = granted?.GetClaim(Claims.Subject);
        MarketAccount? account = subject is null ? null : await accounts.FindByIdAsync(subject, cancel);

        // 刷新令牌可能在账号被停用之后才来续期。每次都回查一遍账号,停用才是立即生效的。
        if (granted is null || account is null || account.IsDisabled)
        {
            return Results.Forbid(Problem(Errors.InvalidGrant, "账号已不可用,请重新登录。"),
                [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        ClaimsIdentity identity = CreateIdentity(account);
        identity.SetScopes(granted.GetScopes());
        identity.SetResources(granted.GetResources());
        identity.SetDestinations(GetDestinations);

        return Results.SignIn(new(identity), null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>退出登录:先清掉本站的会话 cookie,再由 OpenIddict 回跳客户端。</summary>
    private static async Task<IResult> SignOutAsync(HttpContext context)
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        // 回跳地址由 OpenIddict 按 post_logout_redirect_uri 白名单决定;没给就回认证服务首页。
        return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" },
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }

    /// <summary>用户信息端点。只返回访问令牌里那批声明所对应的字段,不多给。</summary>
    private static async Task<IResult> UserInfoAsync(HttpContext context, AccountStore accounts, CancellationToken cancel)
    {
        string? subject = context.User.GetClaim(Claims.Subject);
        MarketAccount? account = subject is null ? null : await accounts.FindByIdAsync(subject, cancel);
        if (account is null)
        {
            return Results.Unauthorized();
        }

        Dictionary<string, object> claims = new(StringComparer.Ordinal)
        {
            [Claims.Subject] = account.Id,
            [Claims.Name] = account.DisplayName ?? account.UserName,
            [Claims.PreferredUsername] = account.UserName
        };
        if (context.User.HasScope(Scopes.Email) && account.Email is not null)
        {
            claims[Claims.Email] = account.Email;
            claims[Claims.EmailVerified] = false;
        }
        return Results.Ok(claims);
    }

    /// <summary>把账号翻译成 OpenIddict 要的身份。声明名一律用 OIDC 标准名。</summary>
    private static ClaimsIdentity CreateIdentity(MarketAccount account)
    {
        ClaimsIdentity identity = new(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, account.Id)
                .SetClaim(Claims.Name, account.DisplayName ?? account.UserName)
                .SetClaim(Claims.PreferredUsername, account.UserName);
        if (account.Email is not null)
        {
            identity.SetClaim(Claims.Email, account.Email);
        }
        return identity;
    }

    /// <summary>
    /// 每条声明该进哪个令牌。默认只进访问令牌 —— id_token 是给客户端看的,
    /// 没申请 profile/email 就不该在里面看到姓名和邮箱。
    /// </summary>
    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        switch (claim.Type)
        {
            case Claims.Name or Claims.PreferredUsername:
                yield return Destinations.AccessToken;
                if (claim.Subject?.HasScope(Scopes.Profile) == true)
                {
                    yield return Destinations.IdentityToken;
                }
                yield break;

            case Claims.Email:
                yield return Destinations.AccessToken;
                if (claim.Subject?.HasScope(Scopes.Email) == true)
                {
                    yield return Destinations.IdentityToken;
                }
                yield break;

            default:
                yield return Destinations.AccessToken;
                yield break;
        }
    }

    /// <summary>按 OAuth 的错误格式回话 —— 客户端认的是 <c>error</c> 字段,不是 HTTP 状态码。</summary>
    private static AuthenticationProperties Problem(string error, string description) =>
        new(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
        });
}

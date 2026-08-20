using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace VelaShell.Market.Api;

/// <summary>
/// 让 API 能从**内部地址**拉认证服务的 discovery 与 JWKS, 同时仍按**对外 issuer** 校验令牌。
///
/// 问题背景: 认证服务的 issuer 是浏览器可访问的地址 (如 http://localhost:7020),
/// discovery 文档里的所有 URL —— 包括 jwks_uri —— 都基于它生成。但 API 跑在容器里,
/// localhost 指的是 API 自己, 只能通过服务名 (如 http://identity:8080) 访问认证服务。
///
/// 做法: 用自定义 <see cref="IDocumentRetriever" /> 拦下每一次文档拉取, 把 issuer 前缀换成内部地址。
/// discovery 与 JWKS 两次请求都会经过这里, 所以两处都会被正确改写。
/// 拿到配置后再把 <see cref="OpenIdConnectConfiguration.Issuer" /> 改回对外地址 ——
/// 令牌里的 <c>iss</c> 写的是那个, 不改回来签名验得过也会栽在 issuer 校验上。
///
/// 只有 issuer 与内部地址不一致时才需要它。生产环境两者通常就是同一个域名, 那时用默认行为即可。
/// </summary>
internal sealed class InternalAuthorityConfigurationManager : IConfigurationManager<OpenIdConnectConfiguration>
{
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _inner;
    private readonly string _issuer;

    /// <param name="issuer">对外 issuer, 令牌里 <c>iss</c> 的值。</param>
    /// <param name="internalAuthority">API 实际能访问到的认证服务地址。</param>
    public InternalAuthorityConfigurationManager(string issuer, string internalAuthority)
    {
        _issuer = issuer.TrimEnd('/');
        _inner = new(
            $"{internalAuthority.TrimEnd('/')}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new PatchingDocumentRetriever(_issuer, internalAuthority));
    }

    /// <inheritdoc />
    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
    {
        OpenIdConnectConfiguration config = await _inner.GetConfigurationAsync(cancel);
        config.Issuer = _issuer;
        return config;
    }

    /// <inheritdoc />
    public void RequestRefresh() => _inner.RequestRefresh();
}

/// <summary>
/// 把文档地址里的对外 issuer 前缀换成内部可达地址, 再交给真正的检索器。
/// </summary>
internal sealed class PatchingDocumentRetriever : IDocumentRetriever
{
    private readonly string _issuer;
    private readonly string _internalAuthority;
    private readonly HttpDocumentRetriever _inner;

    /// <param name="issuer">对外 issuer, 令牌里 <c>iss</c> 的值。</param>
    /// <param name="internalAuthority">API 实际能访问到的认证服务地址。</param>
    public PatchingDocumentRetriever(string issuer, string internalAuthority)
    {
        _issuer = issuer;
        _internalAuthority = internalAuthority.TrimEnd('/');

        HttpClient client = new();
        if (issuer.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            // 对外是 HTTPS 时,认证服务多半开着 Identity:RequireHttps —— 那会让 OpenIddict
            // 拒绝一切非 HTTPS 请求。而这一跳是容器网络内部的直连(http://identity:8080),
            // 不经过任何反代,自然也没有反代加的转发头,于是连 discovery 都拉不到
            //(IDX20807),表现是**所有需要登录的接口一律 500/401**。
            //
            // 这里显式补上这个头,把"这条链路对外是 HTTPS"的事实告诉认证服务。
            // 不是在放松安全:这一跳根本不出宿主机的 Docker 网络,而认证服务本来就
            // 无条件信任转发头(KnownProxies 清空),前提正是它只暴露给反代 ——
            // 也正因如此,7020 端口绝不能直接挂到公网上。
            client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        }

        // RequireHttps 按**改写之后的那个地址**来定, 不能跟着 Auth:RequireHttpsMetadata 走:
        // 改写之后是容器内的 http 地址, 硬要求 HTTPS 会让 HttpDocumentRetriever 直接抛 IDX20108。
        // 对外那条链路的 HTTPS 由启动校验"RequireHttpsMetadata=true 则 Issuer 必须是 https"保证。
        _inner = new(client)
        {
            RequireHttps = _internalAuthority.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        };
    }

    /// <inheritdoc />
    public Task<string> GetDocumentAsync(string address, CancellationToken cancel) =>
        _inner.GetDocumentAsync(address.Replace(_issuer, _internalAuthority, StringComparison.OrdinalIgnoreCase), cancel);
}

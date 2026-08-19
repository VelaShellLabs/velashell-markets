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
    /// <param name="requireHttps">是否要求 metadata 走 HTTPS。</param>
    public InternalAuthorityConfigurationManager(string issuer, string internalAuthority, bool requireHttps)
    {
        _issuer = issuer.TrimEnd('/');
        _inner = new(
            $"{internalAuthority.TrimEnd('/')}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new PatchingDocumentRetriever(_issuer, internalAuthority, requireHttps));
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
internal sealed class PatchingDocumentRetriever(string issuer, string internalAuthority, bool requireHttps) : IDocumentRetriever
{
    private readonly string _internalAuthority = internalAuthority.TrimEnd('/');
    private readonly HttpDocumentRetriever _inner = new() { RequireHttps = requireHttps };

    /// <inheritdoc />
    public Task<string> GetDocumentAsync(string address, CancellationToken cancel) =>
        _inner.GetDocumentAsync(address.Replace(issuer, _internalAuthority, StringComparison.OrdinalIgnoreCase), cancel);
}

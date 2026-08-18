using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace VelaShell.Market.Api;

/// <summary>
/// 包装 <see cref="ConfigurationManager{OpenIdConnectConfiguration}" />, 在拿到 discovery 文档后
/// 把 <see cref="OpenIdConnectConfiguration.JwksUri" /> 替换为容器内部可达的地址。
///
/// 问题背景: IdentityServer 的 issuer 是浏览器可访问的 http://localhost:7020,
/// discovery 文档里的所有 URL (包括 jwks_uri) 也都基于此地址生成。
/// 但 API 跑在 Docker 容器内, 无法解析 localhost:7020 (那是 API 自身),
/// 必须通过服务名 http://identity:8080 才能访问 IdentityServer。
///
/// 做法: 用自定义 IDocumentRetriever 拦截所有文档拉取请求,
/// 把 localhost:7020 替换为 identity:8080, 这样 OpenIdConnectConfigurationRetriever
/// 内部拉取 JWKS 时也会用内部地址。
/// </summary>
internal sealed class InternalAuthorityConfigurationManager : IConfigurationManager<OpenIdConnectConfiguration>
{
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _inner;

    public InternalAuthorityConfigurationManager(string internalAuthority)
    {
        var patchingRetriever = new PatchingDocumentRetriever(internalAuthority);
        _inner = new(
            $"{internalAuthority}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            patchingRetriever);
    }

    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
    {
        var config = await _inner.GetConfigurationAsync(cancel);

        // 确保 issuer 仍然是浏览器端的原始地址 (不是内部地址),
        // 否则 JWT 校验会因 issuer 不匹配失败。
        config.Issuer = "http://localhost:7020";

        return config;
    }

    public void RequestRefresh() => _inner.RequestRefresh();
}

/// <summary>
/// 自定义 <see cref="IDocumentRetriever"/>, 拦截所有文档拉取请求,
/// 把指向 localhost:7020 的 URL 替换为内部可达的 identity:8080。
///
/// ConfigurationManager 通过 IDocumentRetriever 拉取:
///   1. discovery document (/.well-known/openid-configuration)
///   2. JWKS (jwks_uri 指向的 URL)
/// 两个请求都经过这里, 所以两处 URL 都会被正确替换。
/// </summary>
internal sealed class PatchingDocumentRetriever : IDocumentRetriever
{
    private readonly string _internalHost; // "http://identity:8080"
    private readonly HttpDocumentRetriever _inner = new() { RequireHttps = false };

    public PatchingDocumentRetriever(string internalAuthority)
    {
        _internalHost = internalAuthority.TrimEnd('/');
    }

    public async Task<string> GetDocumentAsync(string address, CancellationToken cancel)
    {
        string patched = address.Replace("http://localhost:7020", _internalHost);
        return await _inner.GetDocumentAsync(patched, cancel);
    }
}

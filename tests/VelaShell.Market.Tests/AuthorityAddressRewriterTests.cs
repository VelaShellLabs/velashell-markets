using VelaShell.Market.Api;

namespace VelaShell.Market.Tests;

/// <summary>
/// API 拉认证服务 discovery / JWKS 时的地址改写。
///
/// 这段逻辑只在"对外 HTTPS + 容器内明文"的完整部署下才会被真正走到,而它已经坏过两次:
/// 一次是把对外的 RequireHttps 套到了内部那一跳上(IDX20108),
/// 一次是用字符串替换 issuer 前缀、而 OpenIddict 生成的 jwks_uri 里根本没有那个域名(IDX20804)。
/// 两次都要把整套 frp + 证书跑起来才发现,所以这里用纯函数把它钉死。
/// </summary>
[TestClass]
public class AuthorityAddressRewriterTests
{
    private const string Internal = "http://identity:8080";

    [TestMethod]
    public void PublicHttpsDiscoveryAddress_IsDialedBackToInternalPlainHttp()
    {
        string result = AuthorityAddressRewriter.ToInternal(
            "https://auth.easilynet.top/.well-known/openid-configuration", Internal);
        Assert.AreEqual("http://identity:8080/.well-known/openid-configuration", result);
    }

    /// <summary>
    /// 真正让线上炸掉的那一条:OpenIddict 按**请求的 scheme + Host** 生成端点 URL,
    /// 而我们给内部请求补了 X-Forwarded-Proto: https,于是 jwks_uri 长成了
    /// https://identity:8080/... —— 主机已经是内部的,只有 scheme 是错的。
    /// 前缀替换对这种地址完全无能为力,按结构重写才盖得住。
    /// </summary>
    [TestMethod]
    public void InternalHostAdvertisedOverHttps_HasSchemeCorrected()
    {
        string result = AuthorityAddressRewriter.ToInternal(
            "https://identity:8080/.well-known/jwks", Internal);
        Assert.AreEqual("http://identity:8080/.well-known/jwks", result);
    }

    [TestMethod]
    public void PathAndQuery_ArePreserved()
    {
        string result = AuthorityAddressRewriter.ToInternal(
            "https://auth.easilynet.top/.well-known/jwks?v=2", Internal);
        Assert.AreEqual("http://identity:8080/.well-known/jwks?v=2", result);
    }

    /// <summary>内部地址用默认端口时不该在 URL 里留一个多余的 :80。</summary>
    [TestMethod]
    public void DefaultPortAuthority_DoesNotEmitExplicitPort()
    {
        string result = AuthorityAddressRewriter.ToInternal(
            "https://auth.easilynet.top/.well-known/jwks", "http://identity");
        Assert.AreEqual("http://identity/.well-known/jwks", result);
    }

    /// <summary>内部地址本身是 HTTPS 时照样能用(比如认证服务自己带证书的部署)。</summary>
    [TestMethod]
    public void HttpsInternalAuthority_IsHonoured()
    {
        string result = AuthorityAddressRewriter.ToInternal(
            "https://auth.easilynet.top/.well-known/jwks", "https://identity:8443");
        Assert.AreEqual("https://identity:8443/.well-known/jwks", result);
    }

    [TestMethod]
    public void NonAbsoluteAddress_IsReturnedUnchanged()
    {
        Assert.AreEqual("/.well-known/jwks", AuthorityAddressRewriter.ToInternal("/.well-known/jwks", Internal));
    }

    [TestMethod]
    public void UnparsableInternalAuthority_LeavesAddressAlone()
    {
        const string address = "https://auth.easilynet.top/.well-known/jwks";
        Assert.AreEqual(address, AuthorityAddressRewriter.ToInternal(address, "identity:8080"));
    }
}

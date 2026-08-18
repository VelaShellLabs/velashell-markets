namespace VelaShell.Market.Api.Options;

/// <summary>授权策略名。</summary>
public static class MarketPolicies
{
    /// <summary>审核员:能看隔离区、放行/驳回待复核的包、下架插件与隐藏评价。</summary>
    public const string Moderator = "market:moderator";
}

/// <summary>
/// 市场对接 EasilyNET.IdentityServer 的配置。市场是**资源服务器**:不发令牌,只验令牌。
/// </summary>
public sealed class MarketAuthOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Auth";

    /// <summary>IdentityServer 的 issuer,如 <c>https://localhost:7020</c>。discovery 与 JWKS 都从它推导。</summary>
    public string Authority { get; set; } = "https://localhost:7020";

    /// <summary>本 API 的受众标识(IdentityServer 里注册的 API 资源名)。留空则不校验 aud。</summary>
    public string Audience { get; set; } = "velashell-market";

    /// <summary>是否要求 metadata 走 HTTPS。**生产环境必须为 true**;开发环境对自签证书可临时关掉。</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// 审核员的身份主体(<c>sub</c>)列表。市场的管理员是市场自己的概念,
    /// 不要求对方 IdentityServer 为我们维护角色声明。
    /// </summary>
    public string[] ModeratorSubjects { get; set; } = [];

    /// <summary>允许跨域访问的前端来源。</summary>
    public string[] AllowedOrigins { get; set; } = ["http://localhost:8000"];
}

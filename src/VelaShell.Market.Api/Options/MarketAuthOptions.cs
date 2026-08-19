namespace VelaShell.Market.Api.Options;

/// <summary>授权策略名。</summary>
public static class MarketPolicies
{
    /// <summary>审核员:能看隔离区、放行/驳回待复核的包、下架插件与隐藏评价。</summary>
    public const string Moderator = "market:moderator";
}

/// <summary>
/// 市场对接统一认证服务的配置。市场是**资源服务器**:不发令牌,只验令牌。
/// </summary>
public sealed class MarketAuthOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Auth";

    /// <summary>
    /// 令牌里 <c>iss</c> 的值,也就是认证服务对外宣称的身份。
    /// 它必须与认证服务的 <c>Identity:Issuer</c> 一模一样,差一个斜杠都会让所有请求变成 401。
    /// </summary>
    public string Issuer { get; set; } = "http://localhost:7020";

    /// <summary>
    /// 拉 discovery 与 JWKS 用的地址。留空表示与 <see cref="Issuer" /> 相同。
    ///
    /// 只有在"浏览器看到的地址"与"API 能访问到的地址"不是同一个时才需要单独设 ——
    /// compose 里就是这种情况:浏览器走 <c>http://localhost:7020</c>,而 API 在容器内
    /// 只能走服务名 <c>http://identity:8080</c>。两者都指同一个服务,签发者仍以 Issuer 为准。
    /// </summary>
    public string Authority { get; set; } = "";

    /// <summary>本 API 的受众标识(认证服务里那个 scope 的资源名)。留空则不校验 aud。</summary>
    public string Audience { get; set; } = "velashell-market";

    /// <summary>是否要求 metadata 走 HTTPS。**生产环境必须为 true**;开发环境对自签证书可临时关掉。</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// 审核员的身份主体(<c>sub</c>)列表。市场的管理员是市场自己的概念,
    /// 不要求认证服务为我们维护一套角色声明。
    /// </summary>
    public string[] ModeratorSubjects { get; set; } = [];

    /// <summary>允许跨域访问的前端来源。</summary>
    public string[] AllowedOrigins { get; set; } = ["http://localhost:8000"];
}

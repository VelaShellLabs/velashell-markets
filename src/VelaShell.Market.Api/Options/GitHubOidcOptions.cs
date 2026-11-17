namespace VelaShell.Market.Api.Options;

/// <summary>
/// 可信发布(GitHub Actions OIDC)的配置。默认关闭:不开时 <c>/api/uploads/trusted</c> 不挂载,
/// 「我的插件」页也不提供登记入口 —— 一个没在用的认证方案不该留在线上。
/// </summary>
public sealed class GitHubOidcOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "GitHubOidc";

    /// <summary>认证方案名。与统一认证服务那套方案严格分开,只挂在可信上传一个端点上。</summary>
    public const string Scheme = "GitHubActions";

    /// <summary>是否启用可信发布。</summary>
    public bool Enabled { get; set; }

    /// <summary>GitHub Actions 的 OIDC 签发者。除非用 GHES,否则不要改。</summary>
    public string Issuer { get; set; } = "https://token.actions.githubusercontent.com";

    /// <summary>
    /// 令牌的受众(<c>aud</c>)。CI 申请令牌时传的 audience 必须与它一致 ——
    /// 这样别的服务要来的 GitHub 令牌拿到这里也用不了,反之亦然。
    /// </summary>
    public string Audience { get; set; } = "velashell-market";

    /// <summary>登记时查仓库 id 用的 GitHub REST API 根地址。GHES 时改成 <c>https://主机/api/v3</c>。</summary>
    public string ApiBaseUrl { get; set; } = "https://api.github.com";

    /// <summary>
    /// 可选:查仓库用的 GitHub 令牌(只读即可)。不配时只能登记**公开**仓库,
    /// 且受匿名 API 每小时 60 次的限额 —— 登记是低频操作,通常够用。
    /// </summary>
    public string? ApiToken { get; set; }

    /// <summary>每个插件最多登记多少条可信发布者。</summary>
    public int MaxPublishersPerPlugin { get; set; } = 10;
}

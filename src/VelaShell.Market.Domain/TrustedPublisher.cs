using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;

namespace VelaShell.Market.Domain;

/// <summary>
/// 可信发布者:插件拥有者登记的一条「这个 CI 工作流可以替我发版」的授权。
///
/// 机制与 PyPI 的 Trusted Publishing 相同 —— CI 不持有任何长期密钥,而是拿 GitHub Actions
/// 当场签发的 OIDC 令牌来上传;商店核对令牌里的仓库、工作流与环境是否与这里登记的一致。
/// 令牌只活几分钟、只对这一次运行有效,泄露的代价远小于一把存在仓库机密里的长期令牌。
///
/// **按数字 id 匹配,不按仓库名**:仓库改名、转移,或被删除后有人用同名重建,名字都会对上,
/// 数字 id 不会。登记时由商店向 GitHub 查出 id 存下来,名字只用于展示与报错。
/// </summary>
public sealed class TrustedPublisher
{
    /// <summary>登记条目的 id(删除时用)。</summary>
    /// <remarks>
    /// **成员不能叫 <c>Id</c>**:EasilyNET 的约定会把子对象里叫 <c>Id</c> 的 string 一律按 ObjectId 存,
    /// 写库即崩(来龙去脉见 <see cref="ContributedCommand" />)。改名后用特性把库里与 API 上的字段名钉回 <c>id</c>。
    /// </remarks>
    [BsonElement("id")]
    [JsonPropertyName("id")]
    public required string PublisherId { get; set; }

    /// <summary>提供方。目前只有 <c>github</c>。</summary>
    public string Provider { get; set; } = "github";

    /// <summary>仓库全名 <c>owner/repo</c>,登记时 GitHub 返回的规范写法。仅用于展示。</summary>
    public required string Repository { get; set; }

    /// <summary>仓库的数字 id(令牌里的 <c>repository_id</c>)。匹配以它为准。</summary>
    public long RepositoryId { get; set; }

    /// <summary>仓库所有者的数字 id(令牌里的 <c>repository_owner_id</c>)。</summary>
    public long RepositoryOwnerId { get; set; }

    /// <summary>工作流文件名,如 <c>release.yml</c>(只是文件名,不含 <c>.github/workflows/</c>)。</summary>
    public required string Workflow { get; set; }

    /// <summary>
    /// 可选的 GitHub 部署环境名。填了就要求令牌来自该环境 —— 环境可以在 GitHub 上配审批人与
    /// 分支 / 标签限制,这是挡住「有仓库写权限的人推个分支改工作流就能发版」的那道闸。
    /// </summary>
    public string? Environment { get; set; }

    /// <summary>登记时间(UTC)。</summary>
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    /// <summary>登记人的身份主体。</summary>
    public string? AddedBySubject { get; set; }
}

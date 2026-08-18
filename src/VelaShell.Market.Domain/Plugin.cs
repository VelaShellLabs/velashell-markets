using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace VelaShell.Market.Domain;

/// <summary>
/// 一个插件条目。文档 id **就是插件 id**(<c>acme.snippets</c>)而不是自增/ObjectId ——
/// 插件 id 在 VelaShell 里全局唯一且发布后不可改(它同时是命令前缀与插件私有数据的命名空间),
/// 拿它当主键,"同 id 重复上架"这件事就由数据库本身挡掉,不依赖应用层的检查顺序。
/// </summary>
public sealed class Plugin
{
    /// <summary>插件 id,同时是主键(小写 <c>[a-z0-9.-]</c>)。</summary>
    [BsonId]
    public required string Id { get; set; }

    /// <summary>拥有者的身份主体(IdentityServer 的 <c>sub</c>)。只有它能发新版本。</summary>
    public required string OwnerSubject { get; set; }

    /// <summary>拥有者展示名(登录时的 name/preferred_username,仅用于展示)。</summary>
    public string? OwnerName { get; set; }

    /// <summary>展示名称(取自最新版本的清单)。</summary>
    public required string DisplayName { get; set; }

    /// <summary>一句话简介(取自清单)。</summary>
    public string? Summary { get; set; }

    /// <summary>详细描述,**Markdown 原文**。渲染在读取时做,存储永远只存原文。</summary>
    public string DescriptionMarkdown { get; set; } = "";

    /// <summary>作者(清单的 author,缺省时回落 publisher)。</summary>
    public string? Author { get; set; }

    /// <summary>发布者标识(清单的 publisher)。</summary>
    public string? Publisher { get; set; }

    /// <summary>标签,小写去重(检索与分类用)。</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>主页 / 仓库地址。</summary>
    public string? Homepage { get; set; }

    /// <summary>许可证标识(如 MIT)。</summary>
    public string? License { get; set; }

    /// <summary>当前已发布的最新版本号;一个版本都没通过审核时为空。</summary>
    public string? LatestVersion { get; set; }

    /// <summary>最新已发布版本的 apiLevel(列表页按宿主兼容性过滤要用)。</summary>
    public int? LatestApiLevel { get; set; }

    /// <summary>最新已发布版本要求的最低宿主版本。</summary>
    public string? LatestMinHostVersion { get; set; }

    /// <summary>累计下载次数(按版本下载求和,写入走原子 $inc)。</summary>
    public long Downloads { get; set; }

    /// <summary>评分均值(1–5,无评价时为 0)。</summary>
    public double RatingAverage { get; set; }

    /// <summary>评价条数。</summary>
    public int RatingCount { get; set; }

    /// <summary>是否被管理员下架(下架后不出现在检索里,已装用户不受影响)。</summary>
    public bool IsUnlisted { get; set; }

    /// <summary>下架原因(展示给拥有者)。</summary>
    public string? UnlistedReason { get; set; }

    /// <summary>创建时间(UTC)。</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>最后一次有版本发布的时间(UTC),排序用。</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>一个插件版本的生命周期状态。上传后必须逐级前进,不允许跳步。</summary>
public enum PluginVersionStatus
{
    /// <summary>已落隔离桶,等待检测。**任何情况下都不可下载**。</summary>
    Quarantined,

    /// <summary>检测进行中。</summary>
    Scanning,

    /// <summary>检测未通过,留在隔离桶里等保留期到期;原因见扫描报告。</summary>
    Rejected,

    /// <summary>检测通过并已搬进正式桶,可下载。</summary>
    Published,

    /// <summary>发布后被撤回(作者或管理员),不再出现在列表与下载。</summary>
    Withdrawn
}

/// <summary>
/// 一个插件版本。**对象存储里的位置由状态决定**:隔离期在隔离桶,通过检测后才搬进正式桶
/// —— 这条不变量是整个上传管线的核心,别为了"省一次拷贝"把它优化掉。
/// </summary>
public sealed class PluginVersion
{
    /// <summary>主键。</summary>
    [BsonId]
    public ObjectId Id { get; set; }

    /// <summary>所属插件 id。</summary>
    public required string PluginId { get; set; }

    /// <summary>版本号(semver,来自清单)。同一插件下唯一。</summary>
    public required string Version { get; set; }

    /// <summary>当前状态。</summary>
    public PluginVersionStatus Status { get; set; } = PluginVersionStatus.Quarantined;

    /// <summary>清单里的 apiLevel。</summary>
    public int ApiLevel { get; set; }

    /// <summary>清单里的 minHostVersion(可空)。</summary>
    public string? MinHostVersion { get; set; }

    /// <summary>宿主模式:inProcess / isolated。</summary>
    public string HostMode { get; set; } = "inProcess";

    /// <summary>入口程序集相对路径。</summary>
    public string Entry { get; set; } = "";

    /// <summary>版本说明,Markdown 原文。</summary>
    public string ReleaseNotesMarkdown { get; set; } = "";

    /// <summary>包字节数(容器整体,不是载荷)。</summary>
    public long PackageSize { get; set; }

    /// <summary>容器头里的载荷 SHA-256(小写十六进制)。下载校验与去重都用它。</summary>
    public required string PayloadSha256 { get; set; }

    /// <summary>整包文件的 SHA-256(含头与签名尾)。</summary>
    public required string FileSha256 { get; set; }

    /// <summary>签名状态:Unsigned / Trusted / Untrusted / Invalid(Invalid 不可能走到这里)。</summary>
    public string SignatureState { get; set; } = "Unsigned";

    /// <summary>签名公钥(Base64 SPKI),未签名时为空。用于"同 id 升级必须同公钥"的密钥连续性检查。</summary>
    public string? SignaturePublicKey { get; set; }

    /// <summary>对象存储里的键(隔离期与发布后是**不同桶里的同一个键**)。</summary>
    public required string ObjectKey { get; set; }

    /// <summary>上传者身份主体。</summary>
    public required string UploadedBySubject { get; set; }

    /// <summary>上传时间(UTC)。</summary>
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    /// <summary>通过检测并发布的时间(UTC)。</summary>
    public DateTime? PublishedAt { get; set; }

    /// <summary>该版本的下载次数。</summary>
    public long Downloads { get; set; }

    /// <summary>最近一次扫描报告(内嵌:它与版本一一对应,单独建集合只会多一次往返)。</summary>
    public ScanReport? Scan { get; set; }
}

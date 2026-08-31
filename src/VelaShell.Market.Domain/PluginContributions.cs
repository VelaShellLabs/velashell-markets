using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;

namespace VelaShell.Market.Domain;

/// <summary>
/// 一条命令贡献(<c>plugin.json</c> 的 <c>contributes.commands</c>)。
/// </summary>
/// <param name="CommandId">命令 id,以 <c>&lt;插件id&gt;.</c> 为前缀。</param>
/// <param name="Title">面向用户的显示名称。</param>
/// <param name="Category">分组标签,可空。</param>
/// <remarks>
/// <b>这个成员不能叫 <c>Id</c>。</b>
///
/// EasilyNET 注册的 <c>StringToObjectIdIdGeneratorConvention</c> 会**递归**走进每一层子对象,
/// 把凡是叫 <c>Id</c> / <c>ID</c> 的 string 成员一律按 ObjectId 表示存储。于是写库时
/// <c>velashell.redis.discover</c> 这样的命令 id 被拿去 <c>ObjectId.Parse</c>,
/// 整个上传请求 500(<c>'…' is not a valid 24 digit hex string</c>)。
///
/// 试过但**无效**的两条路,别再走一遍:
/// <list type="bullet">
///   <item>
///     <description><c>[BsonNoId]</c> —— 只能让它不当主键,拦不住这条约定改写序列化器。</description>
///   </item>
///   <item>
///     <description>
///     <c>[BsonRepresentation(BsonType.String)]</c> —— 该约定是在子类型自己的 Attribute 约定
///     跑完之后才递归回来改写的,显式特性反而先被盖掉。
///     </description>
///   </item>
/// </list>
///
/// 该约定只认**成员名**(精确匹配 <c>Id</c> / <c>ID</c>,不匹配 <c>XxxId</c> 后缀),不看元素名,
/// 所以改名即可绕开;下面两个特性再把库里与 API 上的字段名钉回 <c>id</c>,对外没有任何变化。
///
/// 另一条路是 <c>ConfigureMongoConventions</c> + <c>ObjectIdToStringTypes</c>,但调用它会
/// **丢掉 EasilyNET 的全部内置默认约定**:字段名从 <c>pluginId</c> 变成 <c>PluginId</c>,
/// 枚举从 <c>"Quarantined"</c> 变成 <c>0</c>,库里既有文档集体读不出来。代价远大于改个成员名。
/// </remarks>
public sealed record ContributedCommand(
    [property: BsonElement("id"), JsonPropertyName("id")]
    string CommandId,
    string Title,
    string? Category);

/// <summary>
/// 一条连接类型贡献(协议或工作台)。两者在清单里的形状一致,存储也就不必拆成两个类型。
/// </summary>
/// <param name="ConnectionId">协议 / 连接类型 id。</param>
/// <param name="DisplayName">连接配置页上的名称。</param>
/// <param name="DefaultPort">新建配置时的默认端口。</param>
/// <remarks>同 <see cref="ContributedCommand" />:成员名不能是 <c>Id</c>,原因见那边的说明。</remarks>
public sealed record ContributedConnection(
    [property: BsonElement("id"), JsonPropertyName("id")]
    string ConnectionId,
    string DisplayName,
    int DefaultPort);

/// <summary>
/// 版本清单里的声明式贡献点快照。
///
/// 存下来是为了在插件详情页回答一个具体问题:**装上它之后,宿主里会多出什么?**
/// 这些是发现期就生效的静态声明(命令面板里的条目、连接配置页上的页签),
/// 不装载插件程序集就能知道,所以完全可以在上架前如实告诉用户。
///
/// 注意它**不是权限清单**:VelaShell 的清单格式目前没有能力/权限声明字段,
/// 页面上不要把它说成"该插件申请的权限"。
/// </summary>
public sealed class PluginContributionSummary
{
    /// <summary>命令贡献。</summary>
    public List<ContributedCommand> Commands { get; set; } = [];

    /// <summary>远程文件协议贡献。</summary>
    public List<ContributedConnection> Protocols { get; set; } = [];

    /// <summary>工作台连接类型贡献。</summary>
    public List<ContributedConnection> Workspaces { get; set; } = [];

    /// <summary>三类贡献都为空(大量插件确实什么都不声明,页面据此整块不渲染)。</summary>
    public bool IsEmpty => Commands.Count == 0 && Protocols.Count == 0 && Workspaces.Count == 0;
}

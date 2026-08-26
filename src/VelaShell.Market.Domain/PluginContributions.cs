namespace VelaShell.Market.Domain;

/// <summary>
/// 一条命令贡献(<c>plugin.json</c> 的 <c>contributes.commands</c>)。
/// </summary>
/// <param name="Id">命令 id,以 <c>&lt;插件id&gt;.</c> 为前缀。</param>
/// <param name="Title">面向用户的显示名称。</param>
/// <param name="Category">分组标签,可空。</param>
public sealed record ContributedCommand(string Id, string Title, string? Category);

/// <summary>
/// 一条连接类型贡献(协议或工作台)。两者在清单里的形状一致,存储也就不必拆成两个类型。
/// </summary>
/// <param name="Id">协议 / 连接类型 id。</param>
/// <param name="DisplayName">连接配置页上的名称。</param>
/// <param name="DefaultPort">新建配置时的默认端口。</param>
public sealed record ContributedConnection(string Id, string DisplayName, int DefaultPort);

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

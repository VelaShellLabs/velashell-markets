/**
 * 「装上之后宿主里会多出什么」。
 *
 * 数据来自包内 `plugin.json` 的 `contributes` —— 这些是**发现期就生效的静态声明**
 * (命令面板里的条目、连接配置页上的页签),不装载插件程序集就能知道,
 * 所以完全可以在上架前如实告诉用户。
 *
 * 一句要说在前面的话:VelaShell 的清单格式目前**没有能力/权限声明字段**,
 * 所以这一块绝不能写成"该插件申请的权限"。写成那样就是在编一个不存在的保证。
 */
export default function ContributesBox({ contributes, hostMode, idlePolicy }: { contributes?: MarketAPI.Contributes | null; hostMode?: string; idlePolicy?: string }) {
  const commands = contributes?.commands ?? [];
  const protocols = contributes?.protocols ?? [];
  const workspaces = contributes?.workspaces ?? [];
  if (commands.length === 0 && protocols.length === 0 && workspaces.length === 0) {
    return null;
  }

  return (
    <div className="contributes-box">
      <h4>装上之后,宿主里会多出这些</h4>

      {commands.map((command) => (
        <div className="contributes-row" key={command.id}>
          <b>{command.id}</b>
          <span>
            命令 · {command.title}
            {command.category ? ` · ${command.category}` : ''}
          </span>
        </div>
      ))}

      {protocols.map((protocol) => (
        <div className="contributes-row" key={protocol.id}>
          <b>{protocol.id}</b>
          <span>
            连接配置页上多一个「{protocol.displayName}」协议页签 · 默认端口 {protocol.defaultPort}
          </span>
        </div>
      ))}

      {workspaces.map((workspace) => (
        <div className="contributes-row" key={workspace.id}>
          <b>{workspace.id}</b>
          <span>
            连接配置页上多一个「{workspace.displayName}」工作台类型 · 默认端口 {workspace.defaultPort}
          </span>
        </div>
      ))}

      <p className="rail-note" style={{ marginTop: 12 }}>
        以上取自包内 <code className="mono">plugin.json</code> 的 <code className="mono">contributes</code>,是发现期的静态声明。
        运行方式:<b>{hostMode === 'Isolated' ? '隔离进程(崩溃不影响宿主)' : '进程内'}</b>
        {idlePolicy === 'Recyclable' ? ',空闲时可被回收' : ''}。
      </p>
    </div>
  );
}

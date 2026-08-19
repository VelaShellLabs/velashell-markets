# 架构与数据模型

## 分层

```
VelaShell.Market.Identity       统一认证(OpenIddict + MongoDB):签令牌、账号与注册
        ╎ 只经 OIDC 协议往来:签名的 JWT + 一份 JWKS,没有任何代码依赖
VelaShell.Market.Api            最小 API、OIDC 资源服务器、Markdown 渲染、审核台
        │
VelaShell.Market.Infrastructure Mongo 上下文与索引、S3 存储、ClamAV、检测流水线
        │
VelaShell.Market.Domain         实体与状态机(不依赖任何基础设施)
        │
VelaShell.PluginSdk             .vpx 容器与 plugin.json —— 与宿主同一份实现
```

认证服务与市场之间那条虚线是刻意的:两边**没有项目引用**,只共享一个 `sub` 字符串和
一份公钥。市场至今没有一行代码知道口令长什么样,认证服务也不知道插件是什么。
换掉任意一边(比如接企业既有的 IdP)只需要改配置里的 issuer。

## 集合

| 集合 | 主键 | 关键索引 |
| --- | --- | --- |
| `plugins` | **插件 id 本身** | `Tags`、`OwnerSubject`、`DisplayName+Summary+Id` 文本索引 |
| `plugin.versions` | ObjectId | **唯一** `PluginId+Version`、`Status+UploadedAt` |
| `plugin.reviews` | ObjectId | **唯一** `PluginId+Subject` |

三条不变量刻意交给**唯一索引**而不是应用层"先查再写":并发下那种检查必然有窗口,
而一旦破了(同版本两份包、同一人多条评价、同 id 两个插件),数据没法自动修回来。

`plugins._id` 用插件 id 而不是 ObjectId,是因为插件 id 在 VelaShell 里本就全局唯一、
发布后不可改(它同时是命令前缀与插件私有数据的命名空间)—— 拿它当主键,
"同 id 重复上架"由数据库直接挡掉。

## 版本状态机

```
Quarantined ──检测──▶ Scanning ──┬─ 通过 ─────▶ Published ──撤回──▶ Withdrawn
     ▲                            ├─ 需复核 ──▶ Quarantined(等审核台)
     └──── 引擎故障重排队 ─────────┴─ 拒收 ────▶ Rejected
```

包在对象存储里的位置**由状态决定**:非 `Published` 一律在隔离桶。详见
[安全检测流水线](security-pipeline.md)。

## 评分

`Plugin.RatingAverage` / `RatingCount` 在每次评价增删改后**整体重算**,不做增量维护:
评价可改可删可隐藏,增量要处理的边界比一次聚合多得多,而这个量级下聚合的代价可以忽略。

## Markdown

只存原文,渲染在读取时做(`MarkdownRenderer`)。存 HTML 等于把一次转义失误永久固化进数据库,
而且以后想换渲染器都换不动。安全上两道:Markdig 关掉 HTML 直通,再对产物做白名单清洗
(`javascript:` / `data:` 之类的 scheme 与内联事件处理器)。

## 已实现的运维动作

- **保留期清扫**(`QuarantineJanitor`):被拒的包在隔离桶里留够保留期(默认 30 天)后删除对象,
  数据库记录与检测报告**保留** —— 作者仍然看得见当初为什么被拒,而占空间的二进制不必永远留着。
- **撤回版本**:拥有者或审核员可撤回已发布版本,**从正式桶物理删除**。只标状态的话,
  预签名 URL 的有效期内它仍然可下载,而撤回的典型原因(发错包、含敏感信息)恰恰不能接受"再放十分钟"。

## 尚未做的

- 插件源索引(供 VelaShell 宿主直接"浏览并安装"),即 VelaShell 蓝图 10 §3 的 registry;
- 下载统计的去重(当前每次签 URL 即 +1);
- 评价的举报与折叠、作者回复;
- 待人工复核的包没有超时策略与通知;
- 依赖成分分析与动态沙箱(见安全文档末尾)。

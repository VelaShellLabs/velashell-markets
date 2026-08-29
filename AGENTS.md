# AGENTS.md

> 给 AI 代理与新加入者的操作约定。**动手之前先读完本文件,以及它指向的文档。**

## 一、开工前必读:velashell-docs

VelaShell 生态的**全部文档**集中在一个仓库:
**[VelaShellLabs/velashell-docs](https://github.com/VelaShellLabs/velashell-docs)**。
本仓库**不放** `docs/`、`docs-en/` —— 设计手册、开发规范与开发文档都在那边。

**在动任何代码之前**,先把下表中与你要改的部分相关的几篇读掉。跳过这一步直接改,
结果通常是两种:与既有设计冲突,或者重复实现一个已经存在的能力。

| 位置 | 内容 |
| --- | --- |
| [`zh/host/`](https://github.com/VelaShellLabs/velashell-docs/tree/main/zh/host) | 宿主分层架构与依赖方向、工程化重构蓝图、交互与界面规格、快捷键参考、设置项审计,以及 SFTP / FTP / Telnet / 串口 / Redis / S3 / 系统密钥链等可行性调研 |
| [`zh/plugins/`](https://github.com/VelaShellLabs/velashell-docs/tree/main/zh/plugins) | 插件系统设计蓝图 01–15(进程模型、IPC 协议、权限系统、UI 扩展、威胁模型、路线图)与[进度总览 STATUS](https://github.com/VelaShellLabs/velashell-docs/blob/main/zh/plugins/STATUS.md) |
| [`zh/sdk/`](https://github.com/VelaShellLabs/velashell-docs/tree/main/zh/sdk) | 插件契约 SDK 参考、SDK 仓库的发版流程 |
| [`zh/cli/`](https://github.com/VelaShellLabs/velashell-docs/tree/main/zh/cli) | `vela-plugin` 命令行手册、CLI 仓库的发版流程 |
| [`zh/templates/`](https://github.com/VelaShellLabs/velashell-docs/tree/main/zh/templates) | 插件开发指南、打包与发布、模板仓库的发版流程 |

英文镜像在 [`en/`](https://github.com/VelaShellLabs/velashell-docs/tree/main/en),与 `zh/` 同构。
[仓库首页](https://github.com/VelaShellLabs/velashell-docs)有按「我想做什么」组织的快速入口表。

## 二、涉及文档的改动一律同步到 velashell-docs

**这是本文件最重要的一条。**

- 本仓库里**不新建** `docs/`、`docs-en/` 或任何成体系的文档目录。要写文档,去 velashell-docs 开 PR。
- 改了代码,而**行为、接口、配置项、命令行、构建流程或版本纪律**与现有文档对不上时,
  必须**同时**在 velashell-docs 提一个 PR 把文档改过来。两个 PR 在正文里互相引用,一起合。
  只改代码不改文档,等于让文档开始骗人 —— 而文档是别人照抄的。
- velashell-docs 的 `zh/` 与 `en/` 是**互为镜像**的两棵树,文件一一对应。改了中文就要改英文,
  反之亦然。漏一边,两棵树就开始漂。
- velashell-docs 内部的互相引用**一律走相对路径**(如 `../templates/dev-guide.md`),
  不要写回 GitHub 绝对 URL —— 文档集中到一个仓库,消掉的正是那种一改路径就断的跨仓库链接。
- **例外**:留在代码仓库里的少数几份文件不适用上述规则,因为它们服务的是「在这个仓库里写代码」
  这件事,搬走只会离使用场景更远。各仓库的例外清单见下面第三节。

## 三、本仓库:velashell-markets(插件商店)

插件的上传、审核、检索与分发。含统一认证服务(OIDC / OpenIddict + MongoDB)。
上传的 `.vpx` 先进**隔离区**,过容器校验、结构检查与 ClamAV 病毒扫描后才发布。

### 跑起来

```powershell
cp .env.example .env      # 至少改掉 MONGO_ROOT_PASSWORD
$env:SEED_DEMO_DATA='true'
$env:ASPNETCORE_ENVIRONMENT='Development'
docker compose up -d
dotnet run --project src/VelaShell.Market.Api
```

```bash
dotnet build VelaShell.Market.slnx
dotnet test  VelaShell.Market.slnx
```

### 安全流水线是本仓库的核心约束

隔离桶 `vpx-quarantine` **永不对外可读**。任何改动都不许让未过检的包出现在
`vpx-public`,也不许在失败路径上把包"顺手放行"。改流水线前先读
`docs/security-pipeline.md` 与 `docs/architecture.md`。

### ⚠️ 本仓库的 docs/ 尚未并入 velashell-docs

其余仓库的文档已于 2026-08-30 集中到 velashell-docs,**本仓库的 `docs/` 是唯一的例外**,
六篇(`api.md`、`architecture.md`、`deployment.md`、`deployment-nas-frp.md`、
`identity-integration.md`、`security-pipeline.md`)仍在本地。

在它们迁走之前:
- 改本仓库的行为,**照旧改本地 `docs/`**;
- 但**不要在本仓库新建**其他文档目录,新写的成体系文档直接去 velashell-docs;
- 涉及插件契约、`.vpx` 容器格式、签名与信任模型的说明,**以 velashell-docs 为准**,
  本仓库只描述商店侧的实现,不重复定义契约。

商店的对外口径(`vela-plugin install` 怎么用、`--source` 怎么指自建商店)在
[`zh/cli/cli.md`](https://github.com/VelaShellLabs/velashell-docs/blob/main/zh/cli/cli.md),
发布流程在 [`zh/templates/publishing.md`](https://github.com/VelaShellLabs/velashell-docs/blob/main/zh/templates/publishing.md)
—— **改了商店的接口或审核规则,要同步改那两篇。**

### 留在本仓库的文档

`README.md`、`LICENSE.txt`,以及上述待迁移的 `docs/`。

# VelaShell 插件市场(velashell-markets)

[VelaShell](../VelaShell) 插件的上传、审核、检索与分发。用户经
[EasilyNET.IdentityServer](../EasilyNET.IdentityServer) 统一登录,上传的 `.vpx` 先进**隔离区**,
通过容器校验、结构检查与病毒扫描后才会发布。

```
上传 ──▶ 隔离桶(vpx-quarantine)──▶ 静态检查 ──▶ ClamAV ──┬─ 通过 ──▶ 正式桶(vpx-public)──▶ 可下载
          ↑ 永不对外可读                                    ├─ 可疑 ──▶ 留隔离区,转人工复核
          └────────────────────────────────────────────────┴─ 有害 ──▶ 留隔离区,拒收并给出原因
```

## 快速开始

```bash
# 一次性:把 VelaShell 的插件 SDK 打进本地包源(SDK 发到 nuget.org 后此步可省)
pwsh ./build/Sync-VelaShellSdk.ps1

# 起全套:MongoDB + MinIO + ClamAV + API + 前端
docker compose up -d

# 需要连同 IdentityServer 一起起(从同级仓库构建):
docker compose --profile identity up -d
```

| 服务 | 地址 | 说明 |
| --- | --- | --- |
| 前端 | http://localhost:8000 | React + Umi + antd |
| API | http://localhost:8080 | Swagger 在 Development 下于 `/swagger` |
| MinIO 控制台 | http://localhost:9001 | 默认 `minioadmin` / `minioadmin` |
| MongoDB | localhost:27017 | 库名 `velashell-market` |
| clamd | localhost:3310 | 首次启动要拉病毒库,约几分钟 |

> ClamAV 病毒库没就绪时 clamd 不接受连接。这时上传的包会**停在隔离区等重试**,
> 而不会被当成"干净"放行 —— 引擎不可用绝不等于通过。

## 本机开发

```bash
dotnet run --project src/VelaShell.Market.Api      # API
cd src/VelaShell.Market.Web && npm install && npm run dev   # 前端(代理到 8080)
```

同级目录下存在 `VelaShell` 仓库时,`.vpx` 解析会**直接引用宿主那份 SDK 工程**
(见根 `Directory.Build.props` 的 `UseLocalVelaShellSdk`),改一处两边同步;
容器构建看不到同级仓库,自动回退到 NuGet 包。

## 结构

```
src/
├── VelaShell.Market.Domain/          插件 / 版本 / 评价 / 扫描报告与状态机
├── VelaShell.Market.Infrastructure/  Mongo 上下文与索引、S3 存储、ClamAV、检测流水线
├── VelaShell.Market.Api/             HTTP API(最小 API)、OIDC 资源服务器、Markdown 渲染
└── VelaShell.Market.Web/             前端骨架(React + Umi + antd)
tests/VelaShell.Market.Tests/         静态检测器的地面真值用例
build/                                Dockerfile 与 SDK 同步脚本
docs/                                 架构、安全流水线、身份对接
```

## 技术选型

| 关注点 | 选择 | 理由 |
| --- | --- | --- |
| 业务数据 | MongoDB(EasilyNET.Mongo 的 `MongoContext`) | 插件元数据是文档形态;三条唯一性不变量由**唯一索引**而非应用层保证 |
| 包存储 | S3 协议(部署用 MinIO) | 换 AWS S3 / OSS / COS 只改配置;**隔离与正式物理分桶** |
| 身份 | EasilyNET.IdentityServer(OIDC) | 市场只做资源服务器,验 JWT / JWKS,不碰对方内部实现 |
| `.vpx` 解析 | `VelaShell.PluginSdk` | 与宿主**同一份实现**,杜绝"市场收得下、宿主装不上" |
| 病毒扫描 | ClamAV(clamd INSTREAM) | 独立容器,可换;连不上按"检测未完成"处理 |
| Markdown | Markdig(关 HTML 直通 + 白名单清洗) | 只存原文,渲染在读取时做 |

## 文档

- [架构与数据模型](docs/architecture.md)
- [安全检测流水线](docs/security-pipeline.md)
- [对接 EasilyNET.IdentityServer](docs/identity-integration.md)

# VelaShell 插件市场(velashell-markets)

[VelaShell](../VelaShell) 插件的上传、审核、检索与分发。用户经仓库内的**统一认证服务**
(OIDC / OpenIddict + MongoDB)登录,上传的 `.vpx` 先进**隔离区**,
通过容器校验、结构检查与病毒扫描后才会发布。

```
上传 ──▶ 隔离桶(vpx-quarantine)──▶ 静态检查 ──▶ ClamAV ──┬─ 通过 ──▶ 正式桶(vpx-public)──▶ 可下载
          ↑ 永不对外可读                                    ├─ 可疑 ──▶ 留隔离区,转人工复核
          └────────────────────────────────────────────────┴─ 有害 ──▶ 留隔离区,拒收并给出原因
```

## 快速开始

```powershell
cp .env.example .env      # 至少改掉 MONGO_ROOT_PASSWORD

# 起全套,并播三个演示插件 —— 它们会**真的走一遍检测流水线**后才出现在首页
$env:SEED_DEMO_DATA='true'
$env:ASPNETCORE_ENVIRONMENT='Development'
docker compose up -d --build
```

浏览、搜索、看详情不需要登录;上传、评价、审核要登录。第一次进来先去
<http://localhost:7020/account/register> 注册一个账号,登录后那一页会显示你的 `sub` ——
把它填进 `.env` 的 `MODERATOR_SUBJECT` 就能进审核台。
完整部署说明(含生产要改的项与常见故障)见 [docs/deployment.md](docs/deployment.md)。

| 服务 | 地址 | 说明 |
| --- | --- | --- |
| 前端 | http://localhost:8000 | React + Umi + antd |
| 统一认证 | http://localhost:7020 | OIDC(OpenIddict);登录、注册、改口令 |
| API | http://localhost:8080 | Swagger 在 Development 下于 `/swagger` |
| MinIO 控制台 | http://localhost:9001 | 默认 `minioadmin` / `minioadmin` |
| MongoDB | localhost:27017 | 库名 `velashell-market` / `velashell-identity`,**已启用鉴权** |
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
├── VelaShell.Market.Identity/        统一认证(OpenIddict):授权码 + PKCE、账号与注册
└── VelaShell.Market.Web/             前端(React + Umi + antd)
    ├── layouts/                   全站骨架:顶栏、主题令牌、Markdown 排版
    ├── components/                评价区、检测报告
    └── pages/                     浏览 / 详情 / 发布 / 我的上传 / 我的插件 / 审核台
tests/VelaShell.Market.Tests/         静态检测器的地面真值用例
build/                                Dockerfile 与 SDK 同步脚本
docs/                                 架构、安全流水线、身份对接
```

## 技术选型

| 关注点 | 选择 | 理由 |
| --- | --- | --- |
| 业务数据 | MongoDB(EasilyNET.Mongo 的 `MongoContext`) | 插件元数据是文档形态;三条唯一性不变量由**唯一索引**而非应用层保证 |
| 包存储 | S3 协议(部署用 MinIO) | 换 AWS S3 / OSS / COS 只改配置;**隔离与正式物理分桶** |
| 身份 | 自建 OIDC(OpenIddict + MongoDB) | 授权码 + 强制 PKCE;市场 API 只做资源服务器,验 JWT / JWKS,不碰账号与口令 |
| `.vpx` 解析 | `VelaShell.PluginSdk` | 与宿主**同一份实现**,杜绝"市场收得下、宿主装不上" |
| 病毒扫描 | ClamAV(clamd INSTREAM) | 独立容器,可换;连不上按"检测未完成"处理 |
| Markdown | Markdig(关 HTML 直通 + 白名单清洗) | 只存原文,渲染在读取时做 |

## 文档

- [部署](docs/deployment.md)
- [架构与数据模型](docs/architecture.md)
- [安全检测流水线](docs/security-pipeline.md)
- [API 一览](docs/api.md)
- [统一认证(OIDC)](docs/identity-integration.md)

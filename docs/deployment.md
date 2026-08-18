# 部署

分三种场景:本机看效果、单机生产、以及"我已经有 MongoDB / MinIO / IdentityServer"。

## 一、本机看效果(最短路径)

```powershell
# 1) 一次性:把 VelaShell 的插件 SDK 打进本地包源
#    容器构建看不到同级的 VelaShell 仓库,只能走 NuGet;SDK 发到 nuget.org 之后这步可省。
pwsh ./build/Sync-VelaShellSdk.ps1

# 2) 起全套,并**播三个演示插件**(它们会真的走一遍检测流水线)
$env:SEED_DEMO_DATA='true'
$env:ASPNETCORE_ENVIRONMENT='Development'
docker compose up -d --build
```

打开 <http://localhost:8000>。

| 想看什么 | 去哪 |
| --- | --- |
| 插件列表、详情、Markdown 说明、版本表、校验和 | <http://localhost:8000> |
| API 与 Swagger(仅 Development) | <http://localhost:8080/swagger> |
| 隔离桶与正式桶里到底有什么 | MinIO 控制台 <http://localhost:9001>(`minioadmin`/`minioadmin`) |
| 检测流水线的日志 | `docker compose logs -f api` |

**演示数据不是假记录**:播种器会真的打出 `.vpx`、真的落隔离桶、真的排队送检,
所以你在首页看到的插件是走完整条流水线之后才出现的,下载下来也是能被 VelaShell 装上的真包
(只是入口程序集是空壳,装上没有实际功能)。

### 第一次起有两件事要等

1. **ClamAV 拉病毒库**:大约几分钟、约 300MB。这期间 clamd 不接受连接,
   演示插件会停在隔离区并按 30s、60s、90s… 退避重试(最多六次,累计约 10 分钟),
   而**不会**被误判为有害。等它就绪后自动发布。
   进度看 `docker compose logs -f clamav`。
2. **API 镜像首次构建**要拉 .NET 11 preview 的 SDK 镜像(约 1GB)。

想跳过等待,可以先关掉病毒扫描看界面(检测报告里会明确记一条"引擎被关闭"的告警):

```powershell
docker compose stop clamav
# 在 docker-compose.yml 的 api 环境里加 ClamAv__Enabled=false 后重建 api
```

### 登录与上传要额外一步

浏览、搜索、看详情**不需要登录**。上传、评价、审核需要 EasilyNET.IdentityServer:

```powershell
docker compose --profile identity up -d      # 从同级仓库构建并跑起来
$env:AUTH_AUTHORITY='http://identity:8080'   # 让 API 指向它
docker compose up -d api
```

然后按 [docs/identity-integration.md](identity-integration.md) 注册一个前端 client。
`redirect_uri` 填 `http://localhost:8000/callback`。

## 二、单机生产

```bash
cp .env.example .env      # 改掉 MinIO 凭据、authority、审核员 sub
docker compose up -d --build
```

上线前**必须**改的几项:

| 配置 | 为什么 |
| --- | --- |
| `MINIO_ROOT_USER` / `MINIO_ROOT_PASSWORD` | 默认凭据等于把两个桶公开 |
| `AUTH_AUTHORITY` | 指向真实的 IdentityServer |
| `AUTH_REQUIRE_HTTPS=true` | 关掉它等于允许中间人替换 discovery 与 JWKS |
| `MODERATOR_SUBJECT` | 留空则审核台无人可进,待复核的包会一直停在隔离区 |
| `SEED_DEMO_DATA` | 保持 `false` |

还需要在 compose 之外补三件事:

1. **反向代理与 TLS**:`web` 容器只监听 80。前面套一层 Caddy / nginx / Traefik 终止 TLS,
   并把 `client_max_body_size` 放到 512MB 以上 —— 插件包最大就是这个量级。
2. **备份**:`mongo-data`(业务数据)与 `minio-data`(**正式桶里的包**)。
   隔离桶可以不备份 —— 里面的东西按定义还没被证明无害。
3. **病毒库更新**:clamav 镜像自带 freshclam,保持容器常驻即可;别用 `--rm` 跑它,
   否则每次重启都要重拉一遍库。

## 三、复用已有的基础设施

三个依赖都是可换的,把对应服务从 compose 里删掉,改环境变量指过去即可:

| 依赖 | 环境变量 | 说明 |
| --- | --- | --- |
| MongoDB | `ConnectionStrings__Mongo` | 副本集/Atlas 都行,连接串照写 |
| 对象存储 | `ObjectStorage__Endpoint` / `AccessKey` / `SecretKey` / `Region` | 走 S3 协议,AWS S3、阿里云 OSS、腾讯云 COS 都可以。**非 MinIO 时注意 `ForcePathStyle`**:代码里固定开着(MinIO 必需),AWS S3 也接受 path-style |
| 病毒扫描 | `ClamAv__Host` / `Port`,或 `ClamAv__Enabled=false` | 关掉后检测报告里会留一条告警,不会静默略过 |

两个桶名可以改(`ObjectStorage__QuarantineBucket` / `PublicBucket`),但**必须是两个不同的桶** ——
"没通过检测的包绝不进正式桶"这条不变量就是靠物理分桶保证的,
指向同一个桶等于把整条安全流水线架空。

## 四、常见故障

| 现象 | 多半是 |
| --- | --- |
| API 启动即退出,日志里 `Object storage is unavailable` | MinIO 没起来或凭据不对。桶建不出来就没有隔离区,这时故意让服务起不来 |
| 上传一直停在"隔离中" | ClamAV 还没就绪。看 `docker compose logs clamav`,等库拉完 |
| 上传后报 `CLAMAV_UNAVAILABLE` | 退避重试六次仍连不上。这不是包的问题,修好引擎后重传即可 |
| 登录后调 API 一律 401 | issuer 与 `Auth__Authority` 对不上。IdentityServer 的 `options.Issuer` 是写死的,换地址时那一处也要改 |
| 前端能开但列表空 | 正常 —— 市场里还没有已发布的插件。想看效果就打开 `SEED_DEMO_DATA` |
| `docker compose build api` 报找不到 `VelaShell.PluginSdk` | 没跑 `build/Sync-VelaShellSdk.ps1`,`./packages` 是空的 |

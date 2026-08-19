# 部署

分三种场景:本机看效果、单机生产、以及"我已经有 MongoDB / MinIO / 病毒引擎"。

## 一、本机看效果(最短路径)

```powershell
# 1) 一次性:把 VelaShell 的插件 SDK 打进本地包源
#    容器构建看不到同级的 VelaShell 仓库,只能走 NuGet;SDK 发到 nuget.org 之后这步可省。
pwsh ./build/Sync-VelaShellSdk.ps1

# 2) 一次性:复制环境变量样例,至少把 MONGO_ROOT_PASSWORD 改掉
cp .env.example .env

# 3) 起全套,并**播三个演示插件**(它们会真的走一遍检测流水线)
$env:SEED_DEMO_DATA='true'
$env:ASPNETCORE_ENVIRONMENT='Development'
docker compose up -d --build
```

打开 <http://localhost:8000>。

| 想看什么 | 去哪 |
| --- | --- |
| 插件列表、详情、Markdown 说明、版本表、校验和 | <http://localhost:8000> |
| 注册账号 / 登录 / 查自己的 `sub` | <http://localhost:7020> |
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

### 登录、注册与审核台

浏览、搜索、看详情**不需要登录**。上传、评价、审核要登录 —— 统一认证服务
(`identity`,<http://localhost:7020>)跟着 compose 一起起来,不需要额外步骤。

第一次进来一个账号都没有,去 <http://localhost:7020/account/register> 注册一个即可;
或者在 `.env` 里填 `BOOTSTRAP_USER` / `BOOTSTRAP_PASSWORD`,启动时自动建好。

**开审核台**:登录认证服务后首页会直接显示你的 `sub`,把它填进 `.env` 的
`MODERATOR_SUBJECT`,再 `docker compose up -d api`。留空则审核台无人可进。

细节(协议选型、issuer 怎么配、密钥怎么存)见 [docs/identity-integration.md](identity-integration.md)。

## 二、单机生产

```bash
cp .env.example .env      # 改掉 Mongo / MinIO 凭据、issuer、审核员 sub
docker compose up -d --build
```

上线前**必须**改的几项:

| 配置 | 为什么 |
| --- | --- |
| `MONGO_ROOT_USER` / `MONGO_ROOT_PASSWORD` | 默认口令等于没有口令。业务数据和账号都在这个库里 |
| `MINIO_ROOT_USER` / `MINIO_ROOT_PASSWORD` | 默认凭据等于把两个桶公开 |
| `IDENTITY_ISSUER` | 指向认证服务的真实对外地址(如 `https://auth.example.com`) |
| `IDENTITY_REQUIRE_HTTPS=true` | 关掉它等于允许令牌在明文里裸奔 |
| `AUTH_REQUIRE_HTTPS=true` | 关掉它等于允许中间人替换 discovery 与 JWKS |
| `WEB_ORIGIN` | 前端的真实来源;它同时是 CORS 白名单与登录回跳白名单 |
| `MODERATOR_SUBJECT` | 留空则审核台无人可进,待复核的包会一直停在隔离区 |
| `BOOTSTRAP_PASSWORD` | 建完首个账号后清空,并到页面上改掉初始口令 |
| `SEED_DEMO_DATA` | 保持 `false` |

域名统一到 HTTPS 之后,compose 里 api 服务的 `Auth__Authority` 可以直接删掉 ——
它存在的唯一理由是"浏览器看到的地址与容器内能访问的地址不是同一个",见
[identity-integration.md 第三节](identity-integration.md)。

还需要在 compose 之外补三件事:

1. **反向代理与 TLS**:`web` 容器只监听 80,`identity` 只监听 8080(映射到 7020)。
   前面套一层 Caddy / nginx / Traefik 终止 TLS,并把 `client_max_body_size` 放到 512MB
   以上 —— 插件包最大就是这个量级。
2. **备份**:`mongo-data`(业务数据 **与账号**)、`minio-data`(**正式桶里的包**)、
   `identity-keys`(**令牌签名密钥**)。隔离桶可以不备份 —— 里面的东西按定义还没被证明无害。
   密钥丢了不会丢数据,但所有人会被登出,而且这事没有补救办法,只能重新登录。
3. **病毒库更新**:clamav 镜像自带 freshclam,保持容器常驻即可;别用 `--rm` 跑它,
   否则每次重启都要重拉一遍库。

## 二之二、给已有的 MongoDB 补上账号密码

`MONGO_INITDB_ROOT_USERNAME` / `PASSWORD` **只在数据目录为空时生效**。
如果 `mongo-data` 卷里已经有数据(在这次改动之前跑过),照上面配好之后 mongo 仍然是免密的,
而带凭据的连接串反而会连不上。两条路:

**A. 数据可以丢**(本机看效果的环境):

```powershell
docker compose down -v      # 注意:连同插件数据与对象存储一起清掉
docker compose up -d --build
```

**B. 数据要留**:先在免密状态下手工建 root 账号,再启用鉴权。

```powershell
# 1) 用当前(免密)配置起 mongo
docker compose up -d mongo

# 2) 建账号
docker compose exec mongo mongosh --quiet --eval @'
db.getSiblingDB("admin").createUser({
  user: "market",
  pwd: "换成你的口令",
  roles: [ { role: "root", db: "admin" } ]
})
'@

# 3) 把 .env 里的 MONGO_ROOT_USER / MONGO_ROOT_PASSWORD 填成同样的值,再整体重启
docker compose up -d --force-recreate
```

口令里带 `: @ / ? #` 这类字符时,连接串里要写成百分号转义(`@` → `%40`),
否则连接串会被解析成别的东西 —— 表现为一个看不懂的"认证失败"。

## 三、复用已有的基础设施

三个依赖都是可换的,把对应服务从 compose 里删掉,改环境变量指过去即可:

| 依赖 | 环境变量 | 说明 |
| --- | --- | --- |
| MongoDB | `ConnectionStrings__Mongo`(api 与 identity 各一份) | 副本集/Atlas 都行,连接串照写。两个服务用**同一个实例的不同库**(`velashell-market` / `velashell-identity`),分开也行 |
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
| 登录后调 API 一律 401 | 令牌里的 `iss` 与 API 的 `Auth__Issuer` 对不上。改了 `IDENTITY_ISSUER` 就要让 api 一起重启 |
| 点登录跳过去报"这次授权请求没能通过" | `redirect_uri` 不在白名单。`WEB_ORIGIN` 要和浏览器地址栏里的来源一模一样(端口、协议都算) |
| 所有服务卡在 `waiting for mongo to be healthy` | Mongo 开了鉴权但健康检查或连接串的凭据不对;老卷没有 root 账号,见上面第二之二节 |
| 重启 identity 后所有人被登出 | `identity-keys` 卷没挂上,密钥每次重新生成 |
| 前端能开但列表空 | 正常 —— 市场里还没有已发布的插件。想看效果就打开 `SEED_DEMO_DATA` |
| `docker compose build api` 报找不到 `VelaShell.PluginSdk` | 没跑 `build/Sync-VelaShellSdk.ps1`,`./packages` 是空的 |

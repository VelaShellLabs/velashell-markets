# NAS + frp 外网部署(mk.oneblogs.cn)

把整套市场跑在家里的 NAS 上,通过 frp 穿到公网 VPS,用 `mk.oneblogs.cn` 对外提供服务。

本文只讲这一种拓扑。本机开发、MongoDB 副本集运维、复用已有基础设施等,见 [deployment.md](deployment.md)。

---

## 一、先想清楚:三个地址,而不是一个

这套系统对外要暴露的不是"一个网站",而是三类东西。它们在配置里是三个独立的值,**填错任何一个,症状都不是报错,而是"能打开但登不上"或"能下单却下不到包"**:

| 用途 | 谁在访问 | 配置项 |
|---|---|---|
| 商店页面与 `/api` | 用户浏览器 | `WEB_ORIGIN` |
| OIDC 认证服务 | 用户浏览器(整页跳转)+ API(验签) | `IDENTITY_ISSUER` |
| 插件包下载 | 用户浏览器(预签名 URL) | `OBJECT_STORAGE_PUBLIC_ENDPOINT` |

推荐的域名规划(本文后面都按这个来):

```
https://mk.oneblogs.cn           商店 + /api + 插件包下载
https://auth.mk.oneblogs.cn      统一认证服务
```

**为什么认证服务要单独一个名字?** 它是一个完整的 Web 应用(登录页、注册页、授权端点),不是几个 API。挂在子路径上需要给它配 `PathBase`、还要让反代把前缀原样透传,任何一处没对齐,登录页的样式和跳转就会散架。换个二级域名是零代码成本的做法,而 `oneblogs.cn` 是你自己的区,加一条记录就有了。

> 如果你确实只想要一个域名,可以做成 `mk.oneblogs.cn/auth`,但需要给认证服务加 `UsePathBase` 支持 —— 目前代码里没有,要改。

**插件包下载不需要额外域名。** 下载走的是 API 签发的**预签名 URL**,路径形如 `/vpx-public/<插件id>/<版本>/xxx.vpx`。前端 nginx 里已经有一条把这个路径直通 MinIO 的 `location`,所以 MinIO 既不用穿透、也不用占域名。

---

## 二、请求怎么流动

```
                    ┌─────────────────────── 公网 VPS ───────────────────────┐
用户浏览器 ──HTTPS──▶│ nginx(TLS 终止 + 按域名分流)                          │
                    │   mk.oneblogs.cn        → 127.0.0.1:18000              │
                    │   auth.mk.oneblogs.cn   → 127.0.0.1:17020              │
                    │ frps :7000                                             │
                    └───────────────────────┬────────────────────────────────┘
                                            │ frp 隧道(单条 TCP,加密)
                    ┌───────────────────────┴──────────── NAS ───────────────┐
                    │ frpc → 127.0.0.1:8000(web) / 127.0.0.1:7020(identity) │
                    │                                                        │
                    │  ┌── web(nginx)──────────────────────────────────┐    │
                    │  │  /              → dist/(SPA)                   │    │
                    │  │  /api/          → api:8080                     │    │
                    │  │  /vpx-public/   → minio:9000  ← 预签名下载      │    │
                    │  └────────────────────────────────────────────────┘    │
                    │  identity:8080   api:8080   minio   mongo rs0   clamav │
                    └────────────────────────────────────────────────────────┘
```

要点:**VPS 上只开两个隧道**。MinIO、Mongo、clamd、api 的 8080 一个都不穿 —— 它们只在 NAS 的 Docker 网络里被访问。

---

## 三、准备工作

### 3.1 DNS

在 `oneblogs.cn` 的解析里加两条 A 记录,都指向 VPS 的公网 IP:

```
mk         A   <VPS_IP>
auth.mk    A   <VPS_IP>
```

等 `dig +short mk.oneblogs.cn` 能返回 VPS IP 再往下走。证书签发要靠它。

### 3.2 VPS 放行端口

```
80/tcp     Let's Encrypt HTTP-01 验证 + http→https 跳转
443/tcp    对外服务
7000/tcp   frps 的接入端口(只给你的 NAS 用,建议按来源 IP 限制)
```

### 3.3 NAS 侧要求

- Docker 与 Docker Compose v2
- 内存:**建议 6GB 以上**。ClamAV 常驻约 1.5–2GB(病毒库全部载入内存),Mongo 三个节点 + MinIO + 两个 .NET 服务加起来也不轻
- 磁盘:插件包上限 512MB/个,按你预期的插件数量留余量

---

## 四、VPS:frps + nginx + 证书

### 4.1 frps

装 frp(下面按 v0.52+ 的 TOML 配置;老版本是 INI,键名不同):

```bash
# /etc/frp/frps.toml
bindPort = 7000

# 认证令牌:NAS 那边必须填一模一样的值。别用示例里的字符串。
auth.method = "token"
auth.token = "换成一串足够长的随机值"

# 只允许下面这些端口被穿透,防止隧道被拿来开别的口子
allowPorts = [{ start = 17000, end = 19000 }]

log.to = "/var/log/frps.log"
log.level = "info"
```

```bash
# systemd 单元 /etc/systemd/system/frps.service
[Unit]
Description=frp server
After=network.target

[Service]
Type=simple
ExecStart=/usr/local/bin/frps -c /etc/frp/frps.toml
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
```

```bash
systemctl daemon-reload && systemctl enable --now frps
systemctl status frps
```

### 4.2 证书

```bash
apt install -y certbot python3-certbot-nginx
certbot --nginx -d mk.oneblogs.cn -d auth.mk.oneblogs.cn
```

一张证书带两个名字,续期由 certbot 的定时任务负责。

### 4.3 nginx

```nginx
# /etc/nginx/conf.d/velashell-market.conf

# ---------- 商店 ----------
server {
    listen 443 ssl http2;
    server_name mk.oneblogs.cn;

    ssl_certificate     /etc/letsencrypt/live/mk.oneblogs.cn/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/mk.oneblogs.cn/privkey.pem;

    # 插件包上限 512MB。这一层不放开,大包会在 VPS 上就被 413 掉,
    # NAS 那边的日志里什么都看不到。
    client_max_body_size 512m;
    # 上传不缓冲:否则 VPS 要先把整个包落到自己磁盘再转发。
    proxy_request_buffering off;
    # 下载不缓冲,同理。
    proxy_buffering off;

    location / {
        proxy_pass http://127.0.0.1:18000;

        # $http_host 而不是 $host:插件包的下载地址是预签名 URL,
        # 签名按 Host 头计算,改写它会让 MinIO 直接回 403。
        proxy_set_header Host              $http_host;
        proxy_set_header X-Real-IP         $remote_addr;
        proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
        # 这一行是 HTTPS 部署的关键:TLS 在这里就终止了,后面全是明文 HTTP。
        # 不把真实协议传下去,后端会以为整条链路都是明文。
        proxy_set_header X-Forwarded-Proto $scheme;

        # 大包上传/下载给足时间
        proxy_read_timeout  300s;
        proxy_send_timeout  300s;
    }
}

# ---------- 统一认证 ----------
server {
    listen 443 ssl http2;
    server_name auth.mk.oneblogs.cn;

    ssl_certificate     /etc/letsencrypt/live/mk.oneblogs.cn/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/mk.oneblogs.cn/privkey.pem;

    location / {
        proxy_pass http://127.0.0.1:17020;
        proxy_set_header Host              $http_host;
        proxy_set_header X-Real-IP         $remote_addr;
        proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
        # 少了这行,认证服务在 RequireHttps=true 下会把每个请求都当成不安全传输拒掉,
        # 表现是登录页直接打不开。
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}

# ---------- http 一律跳 https ----------
server {
    listen 80;
    server_name mk.oneblogs.cn auth.mk.oneblogs.cn;
    return 301 https://$host$request_uri;
}
```

```bash
nginx -t && systemctl reload nginx
```

---

## 五、NAS:frpc

```toml
# /etc/frp/frpc.toml
serverAddr = "<VPS_IP>"
serverPort = 7000

auth.method = "token"
auth.token = "与 frps 完全相同的那串"

# 商店(含 /api 与 /vpx-public 下载)
[[proxies]]
name = "market-web"
type = "tcp"
localIP = "127.0.0.1"
localPort = 8000
remotePort = 18000

# 统一认证
[[proxies]]
name = "market-identity"
type = "tcp"
localIP = "127.0.0.1"
localPort = 7020
remotePort = 17020
```

**不要**给 minio(9000/9001)、mongo(27017-27019)、clamd(3310)、api(8080)加隧道。它们没有任何需要从公网直接访问的理由,穿出去只是白送攻击面。

启动后在 NAS 上确认隧道已建立:

```bash
frpc verify -c /etc/frp/frpc.toml     # 先校验配置
systemctl enable --now frpc
journalctl -u frpc -n 30              # 看到 "start proxy success" 才算成功
```

---

## 六、NAS:`.env`

从样例复制,然后按下表逐项改。**这张表是整份文档最重要的部分** —— 外网部署与本机跑的差别几乎全在这里。

```bash
cp .env.example .env
```

| 变量 | 填什么 | 填错的后果 |
|---|---|---|
| `MONGO_ROOT_PASSWORD` | 换成强口令 | 用默认值等于没设密码 |
| `MONGO_RS_KEY` | 换成随机串(三节点共用) | 同上 |
| `MINIO_ROOT_USER` / `MINIO_ROOT_PASSWORD` | 换掉 minioadmin | 同上 |
| `IDENTITY_ISSUER` | `https://auth.mk.oneblogs.cn` | 登录跳不过去,或跳过去后令牌 `iss` 对不上被 API 拒绝 |
| `IDENTITY_REQUIRE_HTTPS` | `true` | 留 false 等于允许令牌在明文里传输 |
| `AUTH_REQUIRE_HTTPS` | `true` | 留 false 等于允许中间人替换 discovery 与 JWKS |
| `WEB_ORIGIN` | `https://mk.oneblogs.cn` | 登录后回跳被拒(不在白名单);API 的 CORS 也据此推导 |
| `OBJECT_STORAGE_PUBLIC_ENDPOINT` | `https://mk.oneblogs.cn` | **下载地址会指向内网**,用户点下载什么都拿不到 |
| `PUBLIC_BUCKET` | `vpx-public`(保持默认即可) | 改了但只改一处,下载会落到 SPA 的 404 上 |
| `SEED_DEMO_DATA` | `false` | 空库启动时会自动播三个假插件 |
| `ALLOW_SELF_REGISTRATION` | 想让人自助注册就 `true` | — |
| `BOOTSTRAP_USER` / `BOOTSTRAP_PASSWORD` | 首个管理员账号 | 留空则只能靠自助注册 |
| `MODERATOR_SUBJECT` | **先留空**,第一次登录后再填(见 §8) | 留空时审核台无人可进,待复核的包停在隔离区 |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Development 会暴露 Swagger 与详细错误页 |

注意 `IDENTITY_ISSUER` 末尾**不要带斜杠**,它会被直接拼进 discovery 文档里的每个端点 URL。

---

## 七、NAS:起服务

```bash
# SDK 还没发到 nuget.org 之前:先把本地包打进 ./packages
pwsh ./build/Sync-VelaShellSdk.ps1

# 前端在本机打包(镜像只装 dist/,见 build/web.Dockerfile)
cd src/VelaShell.Market.Web && bun install && bun run build && cd -

docker compose build
docker compose up -d
docker compose ps
```

第一次启动有两件事要等:

1. **ClamAV 拉病毒库**,约 300MB、几分钟。库没就绪时 clamd 不接受连接,这期间上传的包会**停在隔离区等重试**,而不会被当成干净放行。用 `docker compose logs -f clamav` 看进度。
2. **Mongo 副本集选主**,几十秒。`docker compose ps` 里 mongo-primary 变 healthy 即可。

---

## 八、首次配置:管理员与审核员

1. 打开 `https://auth.mk.oneblogs.cn/account/register` 注册账号(或用 `BOOTSTRAP_USER` 登录)
2. 登录后**认证服务首页会直接显示你的 sub**,是一串十六进制
3. 把它填进 `.env` 的 `MODERATOR_SUBJECT`
4. `docker compose up -d api` 重建 api 让它生效

没有这一步,标记为"需人工复核"的插件包会永远停在隔离区 —— 这是刻意设计的安全默认,不是故障。

---

## 九、上线前的验收清单

逐条跑,**全绿再对外公布地址**。

```bash
# 1) 商店首页
curl -s -o /dev/null -w "%{http_code}\n" https://mk.oneblogs.cn/
# 期望 200

# 2) API 活着
curl -s https://mk.oneblogs.cn/api/plugins
# 期望 {"total":0,...}

# 3) 认证服务的 discovery —— 这条最能说明问题
curl -s https://auth.mk.oneblogs.cn/.well-known/openid-configuration | head -c 400
# 期望里面所有端点都是 https://auth.mk.oneblogs.cn/... 开头。
# 如果看到 http:// 或 localhost,说明 IDENTITY_ISSUER 没改对。

# 4) 页面里注入的认证地址
curl -s https://mk.oneblogs.cn/ | grep -o '__MARKET_AUTHORITY__="[^"]*"'
# 期望 __MARKET_AUTHORITY__="https://auth.mk.oneblogs.cn"

# 5) 完整走一遍登录:浏览器打开 https://mk.oneblogs.cn,点登录,
#    确认能跳到 auth 域名、登录后能跳回商店且右上角显示用户名。

# 6) 传一个插件包,等它走完检测流水线,然后**点下载**。
#    这一步验证的是预签名 URL 那条链路,前面五条全过它也可能不过。
```

第 6 步如果失败,先看下载地址长什么样(浏览器开发者工具的网络面板,`/download` 那个请求的响应体):

- host 是 `minio:9000` 或 `host.docker.internal` → `OBJECT_STORAGE_PUBLIC_ENDPOINT` 没改
- host 对了但 403 `SignatureDoesNotMatch` → 反代改写了 Host 头,见 §11

---

## 十、安全与备份

### 必须做的

- **`identity-keys` 卷要备份。** 里面是令牌的签名与加密密钥。丢了等于换了个签发者:所有已发出的令牌一起失效,所有人被登出。

  ```bash
  docker run --rm -v dockercompose_identity-keys:/keys -v $(pwd):/backup alpine \
      tar czf /backup/identity-keys-$(date +%F).tgz -C /keys .
  ```

  卷名以 `docker volume ls | grep identity-keys` 的实际输出为准。

- **Mongo 与 MinIO 的数据卷要有定期备份。** 前者是全部元数据(插件、版本、评价、账号),后者是插件包本体。

- **NAS 上的端口不要暴露到 LAN 之外。** compose 把 mongo/minio/clamd/api 都发布到了宿主机端口,那是为了本机调试。NAS 上如果有公网 IP 或端口转发,务必确认这些端口没被转出去。

  > 想把它们收到 `127.0.0.1` 上的话,**mongo 的三个端口不能这么改** —— 容器是通过 `host.docker.internal`(即宿主机网关 IP)回连副本集的,绑到回环会让它们互相失联。

### 建议做的

- frps 的 7000 端口按来源 IP 限制,只放行你 NAS 的出口 IP
- `ALLOW_SELF_REGISTRATION=false`,账号由你手工建 —— 一个自用市场没必要开放注册
- VPS nginx 加个基础的限流,防止有人对着上传接口刷

---

## 十一、常见故障

**登录页打不开,认证服务日志里全是 400。**
`X-Forwarded-Proto` 没传到。`IDENTITY_REQUIRE_HTTPS=true` 时,认证服务会拒绝一切它认为是明文的请求;TLS 在 VPS 就终止了,不显式告诉它真实协议,它看到的永远是 http。检查 VPS nginx 的 auth 那个 server 块里有没有 `proxy_set_header X-Forwarded-Proto $scheme;`。

**能登录,但跳回商店时报 `invalid_request` 或白屏。**
`WEB_ORIGIN` 与浏览器地址栏不一致。回跳白名单是 `${WEB_ORIGIN}/callback`,少个 `s`、多个斜杠、或者用了 IP 而不是域名,都会被拒 —— 这是防钓鱼的第一道闸,它拒绝得很严格。

**列表能看,点下载 404。**
预签名 URL 的路径是 `/{正式桶名}/...`。前端 nginx 那条 location 用的是 `MARKET_PUBLIC_BUCKET`,API 签名用的是 `ObjectStorage__PublicBucket`,两者由同一个 `PUBLIC_BUCKET` 推导。如果你只改了一处,请求就会落到 SPA 的 `try_files` 上,返回 index.html 或 404。

**点下载返回 403 `SignatureDoesNotMatch`。**
反代改写了 Host 头。S3 的签名覆盖 **Host 头与路径**(不含协议),任何一处被改都会对不上:

- nginx 里必须是 `proxy_set_header Host $http_host;`。用 `$host` 会**丢掉端口** —— 标准 443 端口下两者恰好相同,所以这个坑往往在你改用非标端口的那天才冒出来
- `proxy_pass` 后面不能带路径,否则 nginx 会重写 URI
- 不要在中间再套一层会改写路径的反代

**上传大包时 413,或传到一半断。**
`client_max_body_size 512m` 三层都要有:VPS nginx、NAS 的 web nginx(已内置)、api 的 Kestrel(已内置)。缺任何一层都会在那一层被截断。另外 `proxy_read_timeout` 给足,家宽上行慢,512MB 的包传很久。

**上传后一直卡在"检测中"。**
ClamAV 病毒库还没拉完,或者内存不够被 OOM 杀了。`docker compose logs clamav` 看。引擎不可用时流水线会重试而不是放行 —— 包停在隔离区是**正确行为**。

**下载很慢。**
所有流量都要经过 VPS 中转,瓶颈通常是家宽上行和 VPS 带宽中的较小者。真要提速只能把包放到对象存储/CDN 上,那是另一套方案。

---

## 十二、日常运维

```bash
# 看状态
docker compose ps
docker compose logs -f api

# 更新代码后重新部署(前端要先在本机打包)
git pull
cd src/VelaShell.Market.Web && bun install && bun run build && cd -
docker compose build
docker compose up -d

# 只更新前端
cd src/VelaShell.Market.Web && bun run build && cd -
docker compose build web && docker compose up -d web
```

改了 `.env` 里的地址类变量(`IDENTITY_ISSUER` / `WEB_ORIGIN` / `OBJECT_STORAGE_PUBLIC_ENDPOINT`)之后,**要重建 api、identity、web 三个容器**,它们各自持有其中一部分:

```bash
docker compose up -d api identity web
```

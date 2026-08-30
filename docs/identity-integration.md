# 统一认证(OIDC)

> **2026-08-30:认证服务已拆到独立仓库 [velashell-identity](https://github.com/joesdu/velashell-identity)。**
> 它不再只服务插件市场(资讯服务等也在用),而一个信任根不该跟着某个业务仓库的发版节奏走。
> 本文保留的是**市场这一侧怎么接入**;认证服务自身的运维、加新客户端、以及那三个
> "改了就出事"的常量,见那个仓库的 README。

市场自己不发令牌。发令牌的是独立仓库里的 **velashell-identity** ——
一个基于 [OpenIddict](https://documentation.openiddict.com/) 的 OIDC 授权服务器,账号与协议数据都存在 MongoDB 里。

```
浏览器                     统一认证 (7020)              市场 API (8080)
  │  点"登录"                    │                            │
  ├──── /connect/authorize ─────▶│                            │
  │                              │ 没登录 → 登录页/注册页        │
  │◀──── 302 回 /callback?code ──┤                            │
  ├──── /connect/token ─────────▶│                            │
  │◀──── access_token(JWT) ─────┤                            │
  ├──── Authorization: Bearer ───┼───────────────────────────▶│
  │                              │◀── 拉 JWKS 验签(仅第一次)──┤
```

三方各自的角色:认证服务**签**令牌,市场 API **验**令牌,前端只是**拿着**令牌。
市场 API 至今没有任何一行代码知道口令长什么样。

## 一、跑起来

认证服务在**另一个仓库**,先起它:

```powershell
# 在 velashell-identity 仓库里(它自带 MongoDB,能独立跑)
docker compose up -d

# 回到本仓库
docker compose up -d
```

| 地址 | 是什么 |
| --- | --- |
| <http://localhost:7020> | 统一认证。登录后首页会显示你的 `sub` |
| <http://localhost:7020/account/register> | 注册 |
| <http://localhost:7020/.well-known/openid-configuration> | discovery |
| <http://localhost:8000> | 插件市场,右上角"登录" |

第一次进来没有任何账号,两条路选一条:

1. 打开注册页自助注册;
2. 在 `.env` 里填 `BOOTSTRAP_USER` / `BOOTSTRAP_PASSWORD`,启动时会建好第一个账号,
   它的 `sub` 会打在 `docker compose logs identity` 里。

**想开审核台**,把 `sub` 填进 `.env` 的 `MODERATOR_SUBJECT` 再重启 api。
审核员依旧是市场自己的概念:认证服务不维护角色,市场也不指望它维护。

## 二、协议这边定了什么

| 项 | 值 | 为什么 |
| --- | --- | --- |
| 流程 | 授权码 + **强制 PKCE** | 浏览器里的公开客户端没地方藏密钥,PKCE 是它唯一能证明"换码的人就是发起授权的人"的手段 |
| 关掉的流程 | 隐式、口令 | 隐式流会把令牌塞进地址栏;口令流让第三方页面直接碰到用户口令 |
| 客户端 | `velashell-market-web`,公开,无 secret | 同上 |
| 同意页 | 不弹(`ConsentTypes.Implicit`) | 第一方应用。用户点了"登录"就是同意,再问一遍只是噪音 |
| 访问令牌 | **不加密**的 JWT,1 小时 | 市场 API 是独立的资源服务器,要靠 JWKS 自行验签解析。OpenIddict 默认会加密访问令牌,代码里显式关掉了 |
| 授权码/刷新令牌 | 加密,14 天 | 它们是纯内部凭据,外面没有任何人需要读得懂 |
| scope | `openid profile email velashell-market offline_access` | `velashell-market` 决定令牌里的 `aud`;`offline_access` 换来刷新令牌 |
| 令牌存哪 | 浏览器 `sessionStorage` | 关掉标签页即失效,比 `localStorage` 少一类被顺走的场景 |

客户端与 scope **不需要手工注册**:认证服务每次启动都按配置把它们写进 MongoDB(存在就覆盖)。
于是"改配置 → 重启"是唯一的客户端管理方式,不会出现配置与库不一致的第三种状态。
配置见 `docker-compose.yml` 里 identity 服务的 `Identity__Clients__0__*`。

## 三、issuer:唯一一个容易配错的地方

`Identity:Issuer` 同时是三样东西:

- 令牌里 `iss` 的值;
- discovery 文档里所有端点 URL 的前缀(**包括 `jwks_uri`**);
- 前端点"登录"时跳过去的地址。

所以它必须是**浏览器打得开**的地址。compose 里是 `http://localhost:7020`。

麻烦在于市场 API 跑在容器内,那里的 `localhost` 指的是 API 自己。于是 API 侧分成两个配置:

| 配置 | compose 里的值 | 含义 |
| --- | --- | --- |
| `Auth:Issuer` | `http://localhost:7020` | 令牌里 `iss` 应该长什么样。校验用 |
| `Auth:Authority` | `http://identity:8080` | API 实际能访问到的地址。拉 discovery 与 JWKS 用 |

两者不同时,`InternalAuthorityConfigurationManager` 会把每次文档拉取的地址前缀从前者换成后者,
拿到配置后再把 issuer 改回前者。两者相同(生产上通常是同一个域名)时它根本不会被启用。

> 令牌里的 `iss` 一定带结尾斜杠(`http://localhost:7020/`),因为 OpenIddict 用 `Uri` 表示 issuer。
> 配置里几乎没人会带。API 两种写法都认 —— 只认一种的话,这个差别会精确地表现成
> "登录成功,但调任何接口都是 401"。

## 四、账号

账号存在 MongoDB 的 `accounts` 集合,口令用框架自带的 `PasswordHasher<T>`(PBKDF2)散列。
没有引入 ASP.NET Core Identity 那一整套 —— 它的价值在角色、双因素、外部登录等一批这里用不到的能力,
代价是一层要专门适配 MongoDB 的抽象。

- **`_id` 就是 `sub`**,一经签发不可更改:市场那边的 `Plugin.OwnerSubject`、`Review.Subject` 存的都是它,
  换了 `sub` 等于换了个人,历史插件与评价会集体失去归属。用户名和邮箱都可以改,`sub` 不行。
- 用户名与邮箱的唯一性由 **唯一索引** 保证,不靠应用层"先查再插"。
- 连续失败 8 次锁定 15 分钟(`Accounts:MaxFailedAttempts` / `LockoutDuration`)。
- 改口令会换掉安全戳,已发出的刷新令牌在下次续期时失效。
- 停用账号立即生效:授权端点与令牌端点每次都回查一遍账号,不等令牌自然过期。
- 自助注册可以用 `ALLOW_SELF_REGISTRATION=false` 关掉,那时只能靠 `BOOTSTRAP_*` 或直接改库建账号。

## 五、签名密钥

签名与加密各一把 RSA 2048,首次启动生成后落在 `Identity:KeyDirectory`(compose 里挂了 `identity-keys` 卷)。

**这个目录必须持久化。** 丢了密钥等于换了签发者:所有已签发的令牌一起失效,所有人被登出。
备份时把它和 `mongodb_master_data` 一起备。

## 六、上生产要改的

| 配置 | 改成什么 | 不改的后果 |
| --- | --- | --- |
| `IDENTITY_ISSUER` | 真实域名,如 `https://auth.example.com` | 令牌里的 `iss` 指着 localhost,别的机器一律验不过 |
| `IDENTITY_REQUIRE_HTTPS` | `true` | 令牌在明文里裸奔 |
| `AUTH_REQUIRE_HTTPS` | `true` | 允许中间人替换 discovery 与 JWKS |
| `MONGO_ROOT_PASSWORD` | 换掉 | 默认口令等于没有口令 |
| `WEB_ORIGIN` | 前端真实来源 | 回跳白名单不匹配,登录完跳不回来 |
| `BOOTSTRAP_PASSWORD` | 建完账号后清空,并在页面上改掉初始口令 | 初始口令留在 `.env` 里 |

域名统一之后,`Auth__Authority` 可以直接删掉(留空即等于 `Auth__Issuer`),
上面第三节那套地址改写也就不需要了。

## 七、接第二个客户端

**这件事现在在 [velashell-identity](https://github.com/joesdu/velashell-identity) 仓库做**
(它的 README 有一节专讲"接一个新服务进来")。下面留作参考 ——
在那个仓库的 `docker-compose.yml` 里给 identity 服务加一组环境变量:

```yaml
- Identity__Clients__1__ClientId=velashell-host
- Identity__Clients__1__DisplayName=VelaShell 宿主
- Identity__Clients__1__RedirectUris__0=http://localhost:5173/callback
- Identity__Clients__1__PostLogoutRedirectUris__0=http://localhost:5173
- Identity__Clients__1__Scopes__0=velashell-market
```

重启 identity 即可。要做机密客户端(服务端到服务端)就再加 `Identity__Clients__1__ClientSecret=…`;
**浏览器里的应用一律别填** —— 前端没有能保密的地方,填了也只是把密钥公开发布一遍。

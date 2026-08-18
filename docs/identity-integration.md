# 对接 EasilyNET.IdentityServer

市场是**资源服务器**:自己不发令牌,只验对方签发的 JWT。这样这边不需要知道
IdentityServer 的任何内部实现,对方换存储(内存 / EF Core / MongoDB)、换部署形态都影响不到我们。

## 一、API 侧(已实现)

`Program.cs` 里就一段:

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
       .AddJwtBearer(options =>
       {
           options.Authority = auth.Authority;      // 如 https://localhost:7020
           options.Audience = auth.Audience;        // velashell-market
           options.RequireHttpsMetadata = auth.RequireHttpsMetadata;
       });
```

`Authority` 一给,运行时自动去 `/.well-known/openid-configuration` 拉 discovery 与 JWKS,
签名密钥轮换也自动跟上。配置项在 `appsettings.json` 的 `Auth` 节,容器里用
`Auth__Authority` 等环境变量覆盖。

**审核员是市场自己的概念**:`Auth:ModeratorSubjects` 列出允许进审核台的 `sub`。
刻意不依赖对方 IdentityServer 里的角色声明 —— 那要求对方为我们维护一套角色,耦合毫无必要。
留空时审核台没人进得去,`NeedsReview` 的包会一直停在隔离区,这是安全的默认。

## 二、给市场注册一个 client

对方 Host 的示例客户端(`mvc` / `spa` / `console` …)是**硬编码在
`Stores/InMemoryStores.cs` 里的内存 store**,所以不要去改那个仓库。
它已经实现了 RFC 7591 动态客户端注册,用它注册即可:

```bash
curl -X POST https://localhost:7020/connect/register \
  -H 'Content-Type: application/json' \
  -d '{
    "client_name": "VelaShell Market Web",
    "redirect_uris": ["http://localhost:8000/callback"],
    "post_logout_redirect_uris": ["http://localhost:8000"],
    "grant_types": ["authorization_code", "refresh_token"],
    "response_types": ["code"],
    "token_endpoint_auth_method": "none",
    "scope": "openid profile velashell-market"
  }'
```

拿到的 `client_id` 填给前端(`src/auth.ts` 的 `clientId`,或在页面上注入
`window.__MARKET_CLIENT_ID__`)。

前端是浏览器里的**公开客户端**,所以:

- 走授权码 + PKCE(对方强制 PKCE,也不实现隐式模式);
- 不放任何 client secret —— 浏览器里没有可保密的地方;
- 令牌只进 `sessionStorage`,关掉标签页即失效。

## 三、`velashell-market` 这个 scope

市场用它作为 audience。如果对方的资源(`IResourceStore`)里还没有这个 API 资源,
两条路选一条:

1. 在对方的资源 store 里加一个名为 `velashell-market` 的 API 资源(需要改他们的仓库);
2. **把市场的 `Auth:Audience` 留空**,只校验 issuer 与签名 —— 适合内部部署,
   代价是任何该 issuer 签发的令牌都能访问市场 API。

生产环境建议第 1 条。

## 四、开发环境的两个坑

- **自签证书**:IdentityServer 默认跑在 `https://localhost:7020`。容器里的 API 拉 discovery
  会因为证书不受信而失败,开发时把 `Auth__RequireHttpsMetadata=false` 并让 authority 指向
  `http://identity:8080`(用 compose 的 `identity` profile 时就是这个地址)。**生产必须为 true。**
- **issuer 必须与 authority 完全一致**:对方 `Program.cs` 里 `options.Issuer` 写死为
  `https://localhost:7020`,容器里换成别的地址时那一处也要跟着改,否则令牌里的 `iss`
  与我们校验的 authority 对不上,表现为"登录成功但调 API 一律 401"。

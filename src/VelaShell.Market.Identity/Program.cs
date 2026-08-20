using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.WebEncoders;
using MongoDB.Driver;
using Serilog;
using static OpenIddict.Abstractions.OpenIddictConstants;
using VelaShell.Market.Identity.Accounts;
using VelaShell.Market.Identity.Endpoints;
using VelaShell.Market.Identity.Options;
using VelaShell.Market.Identity.Security;
using VelaShell.Market.Identity.Seeding;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) => config.ReadFrom.Configuration(context.Configuration).WriteTo.Console());

// ---- 配置 -------------------------------------------------------------------
builder.Services.Configure<IdentityServerOptions>(builder.Configuration.GetSection(IdentityServerOptions.SectionName));
// 只认 Proto 与 For 两个转发头;Host 不认,因为 issuer 是显式配置的,不该随请求头飘。
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.Configure<AccountOptions>(builder.Configuration.GetSection(AccountOptions.SectionName));
IdentityServerOptions server = builder.Configuration.GetSection(IdentityServerOptions.SectionName)
                                      .Get<IdentityServerOptions>() ?? new();

// issuer 是令牌里的 iss,也是 discovery 里所有端点的前缀。RequireHttps=true 却给一个 http 的 issuer,
// 结果是 OpenIddict 拒掉每一个请求(登录页都打不开),而报错信息跟真正的原因隔着好几层。
// 启动就失败,把话说清楚。
if (server.RequireHttps && !server.Issuer.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        $"Identity:RequireHttps 为 true,但 Identity:Issuer 是 “{server.Issuer}”。" +
        "两者必须一致 —— 请把 IDENTITY_ISSUER 换成 https 地址," +
        "或在还没上 TLS 之前把 IDENTITY_REQUIRE_HTTPS 保持为 false。");
}

// ---- 数据 --------------------------------------------------------------------
// 账号与 OpenIddict 自己的四个集合(applications / scopes / authorizations / tokens)同库。
string connectionString = builder.Configuration.GetConnectionString("Mongo")
                          ?? "mongodb://localhost:27017/velashell-identity";
MongoUrl mongoUrl = MongoUrl.Create(connectionString);
string databaseName = string.IsNullOrEmpty(mongoUrl.DatabaseName) ? "velashell-identity" : mongoUrl.DatabaseName;
builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoUrl));
builder.Services.AddSingleton(provider => provider.GetRequiredService<IMongoClient>().GetDatabase(databaseName));
builder.Services.AddScoped<AccountStore>();

// ---- 签名与加密密钥 ----------------------------------------------------------
// 在容器建好之前就要准备好:OpenIddict 的服务端配置需要密钥实例。
// 用一个临时的日志工厂,因为此刻 Serilog 还没接管。
TokenKeyProvider keys;
using (ILoggerFactory bootstrap = LoggerFactory.Create(logging => logging.AddSimpleConsole()))
{
    keys = new(server.KeyDirectory, bootstrap.CreateLogger<TokenKeyProvider>());
}
builder.Services.AddSingleton(keys);

// 数据保护密钥(会话 cookie 与登录表单的防伪令牌都靠它加密)也放进同一个目录。
// 默认位置在容器内,容器一重建就换一套:表现为所有人被登出,而且**正停在登录页的人
// 会拿到一个看不懂的防伪校验失败** —— 那张表单是用上一套密钥签的。
// SetApplicationName 固定下来,免得换个部署路径又变出一套隔离的密钥环。
builder.Services.AddDataProtection()
       .PersistKeysToFileSystem(new(Path.Combine(Path.GetFullPath(server.KeyDirectory), "dataprotection")))
       .SetApplicationName("velashell-market-identity");

// ---- 会话:登录页与授权端点之间靠它串起来 --------------------------------------
// 这个 cookie 只属于认证服务自己(它与市场前端不同源),回答的是"这台浏览器上是谁登录着",
// 授权端点据此决定要不要弹登录页。它不是访问市场 API 的凭据 —— 那是访问令牌的事。
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
       .AddCookie(options =>
       {
           options.LoginPath = "/account/login";
           options.LogoutPath = "/connect/endsession";
           options.AccessDeniedPath = "/account/login";
           options.ExpireTimeSpan = TimeSpan.FromDays(14);
           options.SlidingExpiration = true;
           options.Cookie.Name = "velashell.identity";
           options.Cookie.HttpOnly = true;
           options.Cookie.SameSite = SameSiteMode.Lax;
           // 生产是 HTTPS,cookie 就该只走 HTTPS;本机跑 http://localhost 时 Always 会让 cookie 根本发不出去。
           options.Cookie.SecurePolicy = server.RequireHttps ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;

           // 每次请求都拿 cookie 里的安全戳跟库里的对一遍。对不上就当场登出 ——
           // 改口令、停用账号才能**立刻**把别的设备踢下线,而不是等 14 天 cookie 自然过期。
           //
           // 这里不像 ASP.NET Core Identity 那样加 ValidationInterval 节流:这个 cookie 只在
           // 认证服务自己的几个页面(登录页、首页、授权端点)上用得到,请求量本来就很小,
           // 而节流的代价是"改了口令但对方还能再用 30 分钟",不值当。
           options.Events.OnValidatePrincipal = async context =>
           {
               string? subject = context.Principal?.FindFirst(Claims.Subject)?.Value;
               string? stamp = context.Principal?.FindFirst(MarketClaims.SecurityStamp)?.Value;
               AccountStore accounts = context.HttpContext.RequestServices.GetRequiredService<AccountStore>();
               MarketAccount? account = subject is null ? null : await accounts.FindByIdAsync(subject, context.HttpContext.RequestAborted);

               // stamp 为空的是**升级本版本之前**签发的老 cookie。一律当作无效:
               // 放行它们等于给"改口令踢不掉旧会话"留一个无限期的后门。代价是升级后所有人重登一次。
               if (account is null || account.IsDisabled || stamp is null
                   || !string.Equals(stamp, account.SecurityStamp, StringComparison.Ordinal))
               {
                   context.RejectPrincipal();
                   await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
               }
           };
       });
builder.Services.AddAuthorization();

// ---- OpenIddict --------------------------------------------------------------
builder.Services.AddOpenIddict()
       .AddCore(options => options.UseMongoDb())
       .AddServer(options =>
       {
           // issuer 决定 discovery 文档里所有端点的 URL,也决定令牌里的 iss。
           // 它必须是**浏览器与资源服务器都能访问到**的地址,详见 docs/identity-integration.md。
           options.SetIssuer(new Uri(server.Issuer, UriKind.Absolute));

           options.SetAuthorizationEndpointUris("connect/authorize")
                  .SetTokenEndpointUris("connect/token")
                  .SetUserInfoEndpointUris("connect/userinfo")
                  .SetEndSessionEndpointUris("connect/endsession");

           // 只开授权码 + 刷新令牌。隐式流早就不该再用;口令流会让第三方页面直接碰到用户口令。
           options.AllowAuthorizationCodeFlow()
                  .AllowRefreshTokenFlow()
                  .RequireProofKeyForCodeExchange();

           // 标准 scope 也要登记:OpenIddict 会逐个校验请求里的 scope 是否被认得,
           // 只有 openid 与 offline_access 是协议内置的。少登记 profile/email 的话,
           // 前端一发起登录就会被回一个 invalid_scope。
           options.RegisterScopes([
               Scopes.Profile,
               Scopes.Email,
               .. server.Scopes.Select(static s => s.Name).Where(static n => !string.IsNullOrWhiteSpace(n))
           ]);

           options.SetAccessTokenLifetime(server.AccessTokenLifetime)
                  .SetIdentityTokenLifetime(server.IdentityTokenLifetime)
                  .SetRefreshTokenLifetime(server.RefreshTokenLifetime);

           options.AddSigningKey(keys.SigningKey)
                  .AddEncryptionKey(keys.EncryptionKey);

           // OpenIddict 默认把访问令牌也加密,那样只有它自己读得懂。市场 API 是独立的资源服务器,
           // 要靠 JWKS 验签自行解析令牌,所以这里必须关掉加密 —— 授权码与刷新令牌照旧加密。
           options.DisableAccessTokenEncryption();

           options.UseAspNetCore()
                  .EnableAuthorizationEndpointPassthrough()
                  .EnableTokenEndpointPassthrough()
                  .EnableUserInfoEndpointPassthrough()
                  .EnableEndSessionEndpointPassthrough()
                  .EnableStatusCodePagesIntegration();

           if (!server.RequireHttps)
           {
               // 只为本机 http://localhost:7020 开路。生产环境放开它等于允许令牌在明文里裸奔。
               options.UseAspNetCore().DisableTransportSecurityRequirement();
           }
       });

builder.Services.AddHostedService<OpenIddictSeeder>();
builder.Services.AddHostedService<TokenPruningWorker>();

// ---- CORS ---------------------------------------------------------------
// 授权端点是整页跳转,不需要 CORS;但 discovery、JWKS 与令牌端点都是前端用 fetch 调的,
// 少了这一段,登录能跳过去、跳回来,却在换令牌那一步静静地失败在浏览器里。
//
// 允许的来源直接从客户端的回跳白名单推导:能发起登录的来源,恰好就是会来换令牌的来源。
// 这样只有一处需要维护,不会出现"加了客户端却忘了加 CORS"。
string[] browserOrigins = [.. server.Clients
                              .SelectMany(client => client.RedirectUris.Concat(client.PostLogoutRedirectUris))
                              .Select(static uri => Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed)
                                                        ? parsed.GetLeftPart(UriPartial.Authority)
                                                        : null)
                              .OfType<string>()
                              .Distinct(StringComparer.OrdinalIgnoreCase)];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(browserOrigins).AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddRazorPages();
builder.Services.AddProblemDetails();
// 默认的 HTML 编码器会把所有非 ASCII 字符转成 &#x...; 实体。页面照样能看,但源码里全是转义,
// 查问题时基本没法读。放开全部 Unicode 区段,让中文就是中文。
builder.Services.Configure<WebEncoderOptions>(options =>
    options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

WebApplication app = builder.Build();

app.UseSerilogRequestLogging();
// 反代在最外层卸载 TLS(frp -> nginx),容器收到的是明文 HTTP。不把 X-Forwarded-Proto
// 还原成请求的 scheme,OpenIddict 在 RequireHttps=true 下会把每个请求都当成不安全传输拒掉,
// 登录页根本打不开。KnownProxies/KnownNetworks 清空 = 无条件信任转发头,
// 前提是这个容器只暴露给反代,绝不能直接挂到公网上(见 docs/deployment-nas-frp.md)。
app.UseForwardedHeaders();
// 协议层的失败(比如 redirect_uri 不在白名单)由 OpenIddict 归到状态码上,
// 再由这里重放到 /error 渲染成一句人话,而不是一个空白的 400。
app.UseStatusCodePagesWithReExecute("/error", "?code={0}");
app.UseStaticFiles();
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapConnectEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

await app.RunAsync();

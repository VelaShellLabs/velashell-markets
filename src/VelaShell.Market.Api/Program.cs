using Amazon.Runtime;
using Amazon.S3;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using VelaShell.Market.Api.Endpoints;
using VelaShell.Market.Api.Options;
using VelaShell.Market.Api.Services;
using VelaShell.Market.Infrastructure.Persistence;
using VelaShell.Market.Infrastructure.Scanning;
using VelaShell.Market.Infrastructure.Storage;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) => config.ReadFrom.Configuration(context.Configuration).WriteTo.Console());

// ---- 配置 -------------------------------------------------------------------
builder.Services.Configure<ObjectStorageOptions>(builder.Configuration.GetSection(ObjectStorageOptions.SectionName));
// 反代卸载 TLS 后容器只看得到明文 HTTP。还原 X-Forwarded-Proto/For 才能拿到真实的
// 协议与客户端 IP —— 日志里的来源地址、以及任何按 scheme 生成的链接都靠它。
// 清空 KnownProxies/KnownNetworks = 无条件信任转发头,前提是本容器只暴露给反代。
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.Configure<ClamAvOptions>(builder.Configuration.GetSection(ClamAvOptions.SectionName));
builder.Services.Configure<MarketAuthOptions>(builder.Configuration.GetSection(MarketAuthOptions.SectionName));
builder.Services.Configure<GitHubOidcOptions>(builder.Configuration.GetSection(GitHubOidcOptions.SectionName));

// ---- 身份认证:资源服务器姿态 ------------------------------------------------
// 市场自己不发令牌,只验统一认证服务(独立仓库 velashell-identity)签发的 JWT:
// 经 discovery 拿到 JWKS,再逐条校验签名、issuer、audience 与有效期。
MarketAuthOptions auth = builder.Configuration.GetSection(MarketAuthOptions.SectionName).Get<MarketAuthOptions>() ?? new();
// 浏览器看到的地址(issuer)与 API 能访问到的地址不一定是同一个:compose 里前者是
// http://localhost:7020,后者是容器网络里的 http://identity:8080。两者不同时才需要改写文档地址。
string internalAuthority = string.IsNullOrWhiteSpace(auth.Authority) ? auth.Issuer : auth.Authority;
bool authorityDiffersFromIssuer = !string.Equals(internalAuthority.TrimEnd('/'), auth.Issuer.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

// "要求 HTTPS"这件事约束的是**对外**那条链路 —— discovery 与 JWKS 的地址都从 issuer 派生。
// issuer 还是 http 却把这个开关打开,是自相矛盾的配置:开关看着开了,实际什么也没保护。
// 与其让它静默地毫无作用,不如启动就失败,把话说清楚。
// (容器网络内部那一跳走 http 是正常的,由 PatchingDocumentRetriever 单独判断。)
if (auth.RequireHttpsMetadata && !auth.Issuer.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        $"Auth:RequireHttpsMetadata 为 true,但 Auth:Issuer 是 “{auth.Issuer}”。" +
        "要求 HTTPS 元数据时 issuer 必须是 https —— 请把 IDENTITY_ISSUER 换成 https 地址," +
        "或在还没上 TLS 之前把 AUTH_REQUIRE_HTTPS 保持为 false。");
}
// 漏写协议的 "identity:8080" 是个合法的绝对 URI(scheme=identity),不会在解析这一步失败,
// 只会让后面的地址改写拼出连不上的东西 —— 那时报出来的是一串 TLS/连接错误,离真因很远。
if (!Uri.TryCreate(internalAuthority, UriKind.Absolute, out Uri? parsedAuthority)
    || (parsedAuthority.Scheme != Uri.UriSchemeHttp && parsedAuthority.Scheme != Uri.UriSchemeHttps))
{
    throw new InvalidOperationException(
        $"Auth:Authority 是 “{internalAuthority}”,不是一个 http/https 的绝对地址。" +
        "它应该形如 http://identity:8080(API 在容器网络里访问认证服务的地址)。");
}

GitHubOidcOptions gitHubOidc = builder.Configuration.GetSection(GitHubOidcOptions.SectionName).Get<GitHubOidcOptions>() ?? new();
if (gitHubOidc.Enabled && string.IsNullOrWhiteSpace(gitHubOidc.Audience))
{
    // 不校验受众的话,任何仓库为任何服务要来的 GitHub 令牌都能拿来这里上传。
    throw new InvalidOperationException("GitHubOidc:Enabled 为 true 时必须配置 GitHubOidc:Audience。");
}

AuthenticationBuilder authentication = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);
authentication.AddJwtBearer(options =>
       {
           options.Authority = auth.Issuer;
           options.Audience = auth.Audience;
           // 关掉它等于允许中间人替换 discovery 与 JWKS。生产必须为 true。
           options.RequireHttpsMetadata = auth.RequireHttpsMetadata;
           if (authorityDiffersFromIssuer)
           {
               // discovery 文档里的 jwks_uri 是按 issuer 生成的,容器内不可达;
               // 这个 ConfigurationManager 负责把它改写到内部地址上,详见该类的注释。
               options.ConfigurationManager = new VelaShell.Market.Api.InternalAuthorityConfigurationManager(
                   auth.Issuer, internalAuthority);
           }
           options.TokenValidationParameters = new TokenValidationParameters
           {
               ValidateIssuer = true,
               // 带不带结尾的斜杠都认。OpenIddict 用 Uri 表示 issuer,于是令牌里的 iss 一定带斜杠,
               // 而人写配置时几乎不会带 —— 只认一种写法的话,这个差别会表现成"登录成功但一律 401"。
               ValidIssuers = [auth.Issuer.TrimEnd('/'), $"{auth.Issuer.TrimEnd('/')}/"],
               ValidateAudience = !string.IsNullOrEmpty(auth.Audience),
               ValidAudience = auth.Audience,
               ValidateLifetime = true,
               ClockSkew = TimeSpan.FromSeconds(30),
               NameClaimType = "name",
               RoleClaimType = "role"
           };
       });

// ---- 可信发布:GitHub Actions 的 OIDC 令牌 ------------------------------------
// 第二套方案,**不是默认方案**:默认策略(RequireAuthorization())只认上面那套统一认证服务的令牌,
// 这一套只挂在 /api/uploads/trusted 一个端点上(MarketPolicies.TrustedPublisher)。
// 令牌由 GitHub 为一次工作流运行签发、几分钟即过期;仓库、工作流、环境等声明决定它能替谁发版,
// 匹配规则见 TrustedPublishing.Match。
if (gitHubOidc.Enabled)
{
    authentication.AddJwtBearer(GitHubOidcOptions.Scheme, options =>
    {
        options.Authority = gitHubOidc.Issuer;
        options.Audience = gitHubOidc.Audience;
        options.RequireHttpsMetadata = true;
        // 保留原始声明名:repository / job_workflow_ref 这些不在映射表里,但 sub 会被改写,统一不改省得两套口径。
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = gitHubOidc.Issuer,
            ValidateAudience = true,
            ValidAudience = gitHubOidc.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
}

builder.Services.AddHttpClient<GitHubRepositoryResolver>(GitHubRepositoryResolver.ClientName, client =>
{
    // GitHub API 拒绝没有 User-Agent 的请求。
    client.DefaultRequestHeaders.UserAgent.ParseAdd("VelaShell-Market");
    client.Timeout = TimeSpan.FromSeconds(15);
});

AuthorizationBuilder authorization = builder.Services.AddAuthorizationBuilder();
if (gitHubOidc.Enabled)
{
    authorization.AddPolicy(MarketPolicies.TrustedPublisher, policy =>
        policy.AddAuthenticationSchemes(GitHubOidcOptions.Scheme).RequireAuthenticatedUser());
}
authorization
    .AddPolicy(MarketPolicies.Moderator, policy =>
        policy.RequireAssertion(context =>
            // 主体的取法必须与 PrincipalExtensions.Subject() 完全一致:两处分叉的话,
            // 换一次声明映射就会出现"归属判断认得出你、审核策略认不出你"。
            context.User.Identity?.IsAuthenticated == true
            && auth.ModeratorSubjects.Contains(
                context.User.FindFirst("sub")?.Value
                ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? "")));

// ---- 数据与存储 --------------------------------------------------------------
builder.Services.AddMongoContext<MarketDbContext>(builder.Configuration.GetConnectionString("Mongo") ?? "mongodb://localhost:27017/velashell-market");
static AmazonS3Client BuildS3Client(ObjectStorageOptions storage, string serviceUrl) =>
    new(new BasicAWSCredentials(storage.AccessKey, storage.SecretKey), new AmazonS3Config
    {
        ServiceURL = serviceUrl,
        // MinIO 不支持虚拟主机风格的桶寻址,必须走 path-style。
        ForcePathStyle = true,
        AuthenticationRegion = storage.Region
    });

// 服务端自用:上传落桶、检测通过后复制、删除,全部走这个内网端点。
builder.Services.AddSingleton<IAmazonS3>(provider =>
{
    ObjectStorageOptions storage = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ObjectStorageOptions>>().Value;
    return BuildS3Client(storage, storage.Endpoint);
});
// 只用来签下载 URL:ServiceURL 换成**浏览器**能访问的对外地址。
// 签名是纯计算,这个客户端一次请求都不会发,所以指向一个从服务端根本连不通的地址也无妨 ——
// 反过来,如果拿上面那个内网客户端去签,签出来的 URL 里会是 http://minio:9000,外网点不开。
builder.Services.AddKeyedSingleton<IAmazonS3>(ObjectStorageOptions.PresignClientKey, (provider, _) =>
{
    ObjectStorageOptions storage = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ObjectStorageOptions>>().Value;
    return BuildS3Client(storage, storage.EffectivePublicEndpoint);
});
builder.Services.AddSingleton<PackageStorage>();
builder.Services.AddSingleton<ClamAvScanner>();
builder.Services.AddSingleton<ScanQueue>();
builder.Services.AddSingleton<MarkdownRenderer>();
builder.Services.AddHostedService<MarketIndexInitializer>();
builder.Services.AddHostedService<PackageReviewWorker>();
builder.Services.AddHostedService<QuarantineJanitor>();
builder.Services.AddHostedService<StorageInitializer>();
// 播种排在存储初始化之后:它要往隔离桶里写东西,桶得先存在。默认关闭,见 Market:SeedDemoData。
builder.Services.AddHostedService<DemoDataSeeder>();

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(auth.AllowedOrigins).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();

// 上传体积上限在两处都要设:Kestrel 一处、表单一处。少设一处就会在 500MB 的包上
// 收到一个没有上下文的 413 或 BadHttpRequestException。
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
    options.MultipartBodyLengthLimit = 512L * 1024 * 1024);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 512L * 1024 * 1024);

WebApplication app = builder.Build();

app.UseForwardedHeaders();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapAccountEndpoints();
app.MapStatsEndpoints();
app.MapPluginEndpoints();
app.MapOwnerEndpoints();
app.MapUploadEndpoints();
app.MapTrustedPublisherEndpoints();
if (gitHubOidc.Enabled)
{
    app.MapTrustedUploadEndpoint();
}
app.MapReviewEndpoints();
app.MapModerationEndpoints();

await app.RunAsync();
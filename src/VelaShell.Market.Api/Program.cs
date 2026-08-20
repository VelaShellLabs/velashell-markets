using Amazon.Runtime;
using Amazon.S3;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
builder.Services.Configure<ClamAvOptions>(builder.Configuration.GetSection(ClamAvOptions.SectionName));
builder.Services.Configure<MarketAuthOptions>(builder.Configuration.GetSection(MarketAuthOptions.SectionName));

// ---- 身份认证:资源服务器姿态 ------------------------------------------------
// 市场自己不发令牌,只验统一认证服务(src/VelaShell.Market.Identity)签发的 JWT:
// 经 discovery 拿到 JWKS,再逐条校验签名、issuer、audience 与有效期。
MarketAuthOptions auth = builder.Configuration.GetSection(MarketAuthOptions.SectionName).Get<MarketAuthOptions>() ?? new();
// 浏览器看到的地址(issuer)与 API 能访问到的地址不一定是同一个:compose 里前者是
// http://localhost:7020,后者是容器网络里的 http://identity:8080。两者不同时才需要改写文档地址。
string internalAuthority = string.IsNullOrWhiteSpace(auth.Authority) ? auth.Issuer : auth.Authority;
bool authorityDiffersFromIssuer = !string.Equals(internalAuthority.TrimEnd('/'), auth.Issuer.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
       .AddJwtBearer(options =>
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
                   auth.Issuer, internalAuthority, auth.RequireHttpsMetadata);
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
builder.Services.AddAuthorizationBuilder()
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
builder.Services.AddSingleton<IAmazonS3>(provider =>
{
    ObjectStorageOptions storage = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ObjectStorageOptions>>().Value;
    return new AmazonS3Client(new BasicAWSCredentials(storage.AccessKey, storage.SecretKey), new AmazonS3Config
    {
        ServiceURL = storage.Endpoint,
        // MinIO 不支持虚拟主机风格的桶寻址,必须走 path-style。
        ForcePathStyle = true,
        AuthenticationRegion = storage.Region
    });
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
app.MapPluginEndpoints();
app.MapOwnerEndpoints();
app.MapUploadEndpoints();
app.MapReviewEndpoints();
app.MapModerationEndpoints();

await app.RunAsync();
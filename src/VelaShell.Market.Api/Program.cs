using Amazon.Runtime;
using Amazon.S3;
using EasilyNET.Mongo.AspNetCore;
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
// 市场自己不发令牌,只验 EasilyNET.IdentityServer 签发的 JWT(经 Authority 拉 discovery 与 JWKS)。
// 这样这边不需要知道对方任何内部实现,对方换存储/换部署形态也影响不到我们。
MarketAuthOptions auth = builder.Configuration.GetSection(MarketAuthOptions.SectionName).Get<MarketAuthOptions>() ?? new();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
       .AddJwtBearer(options =>
       {
           options.Authority = auth.Authority;
           options.Audience = auth.Audience;
           // 开发环境里 IdentityServer 常跑在自签证书上,允许显式放开;生产必须为 false。
           options.RequireHttpsMetadata = auth.RequireHttpsMetadata;
           options.TokenValidationParameters = new TokenValidationParameters
           {
               ValidateIssuer = true,
               ValidIssuer = auth.Authority,
               ValidateAudience = !string.IsNullOrEmpty(auth.Audience),
               ValidateLifetime = true,
               ClockSkew = TimeSpan.FromSeconds(30),
               NameClaimType = "name",
               RoleClaimType = "role"
           };
       });
builder.Services.AddAuthorization(options =>
    // 审核台:只有配置里列出的主体能进。刻意不依赖对方 IdentityServer 里的角色声明 ——
    // 市场的管理员是市场自己的概念,不该要求对方为我们维护一套角色。
    options.AddPolicy(MarketPolicies.Moderator, policy =>
        policy.RequireAssertion(context =>
            // 主体的取法必须与 PrincipalExtensions.Subject() 完全一致:两处分叉的话,
            // 换一次声明映射就会出现"归属判断认得出你、审核策略认不出你"。
            context.User.Identity?.IsAuthenticated == true
            && auth.ModeratorSubjects.Contains(
                context.User.FindFirst("sub")?.Value
                ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? ""))));

// ---- 数据与存储 --------------------------------------------------------------
builder.Services.AddMongoContext<MarketDbContext>(builder.Configuration.GetConnectionString("Mongo")
                                                  ?? "mongodb://localhost:27017/velashell-market");
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

/// <summary>供集成测试引用的入口标记。</summary>
public partial class Program;

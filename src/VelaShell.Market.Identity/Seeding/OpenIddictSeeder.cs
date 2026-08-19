using Microsoft.Extensions.Options;
using MongoDB.Driver;
using OpenIddict.Abstractions;
using OpenIddict.MongoDb;
using OpenIddict.MongoDb.Models;
using VelaShell.Market.Identity.Accounts;
using VelaShell.Market.Identity.Options;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace VelaShell.Market.Identity.Seeding;

/// <summary>
/// 启动时把配置里的客户端与 scope 落到 MongoDB,并按需建首个账号。
///
/// 幂等:每次启动都按配置**覆盖**已有的客户端与 scope。于是"改配置 → 重启"就是唯一的
/// 客户端管理方式,不需要再去数据库里手工改一遍,也不会出现配置与库不一致的第三种状态。
/// </summary>
public sealed class OpenIddictSeeder(
    IServiceProvider services,
    IOptions<IdentityServerOptions> server,
    IOptions<AccountOptions> accounts,
    ILogger<OpenIddictSeeder> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancel)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        await CreateIndexesAsync(scope.ServiceProvider, cancel);
        await SeedScopesAsync(scope.ServiceProvider, cancel);
        await SeedClientsAsync(scope.ServiceProvider, cancel);
        await SeedBootstrapAccountAsync(scope.ServiceProvider, cancel);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancel) => Task.CompletedTask;

    /// <summary>
    /// OpenIddict 的集合索引要自己建。少了它们,每次令牌校验都会退化成全表扫描 ——
    /// tokens 集合是所有集合里长得最快的那个。
    /// </summary>
    private static async Task CreateIndexesAsync(IServiceProvider provider, CancellationToken cancel)
    {
        IOpenIddictMongoDbContext context = provider.GetRequiredService<IOpenIddictMongoDbContext>();
        OpenIddictMongoDbOptions options = provider.GetRequiredService<IOptionsMonitor<OpenIddictMongoDbOptions>>().CurrentValue;
        IMongoDatabase database = await context.GetDatabaseAsync(cancel);

        await database.GetCollection<OpenIddictMongoDbApplication>(options.ApplicationsCollectionName)
                      .Indexes.CreateOneAsync(new CreateIndexModel<OpenIddictMongoDbApplication>(
                          Builders<OpenIddictMongoDbApplication>.IndexKeys.Ascending(a => a.ClientId),
                          new CreateIndexOptions { Name = "ux_application_client", Unique = true }), cancellationToken: cancel);

        await database.GetCollection<OpenIddictMongoDbScope>(options.ScopesCollectionName)
                      .Indexes.CreateOneAsync(new CreateIndexModel<OpenIddictMongoDbScope>(
                          Builders<OpenIddictMongoDbScope>.IndexKeys.Ascending(s => s.Name),
                          new CreateIndexOptions { Name = "ux_scope_name", Unique = true }), cancellationToken: cancel);

        await database.GetCollection<OpenIddictMongoDbAuthorization>(options.AuthorizationsCollectionName)
                      .Indexes.CreateOneAsync(new CreateIndexModel<OpenIddictMongoDbAuthorization>(
                          Builders<OpenIddictMongoDbAuthorization>.IndexKeys
                              .Ascending(a => a.ApplicationId).Ascending(a => a.Status)
                              .Ascending(a => a.Subject).Ascending(a => a.Type),
                          new CreateIndexOptions { Name = "ix_authorization_lookup" }), cancellationToken: cancel);

        IMongoCollection<OpenIddictMongoDbToken> tokens =
            database.GetCollection<OpenIddictMongoDbToken>(options.TokensCollectionName);
        await tokens.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<OpenIddictMongoDbToken>(
                Builders<OpenIddictMongoDbToken>.IndexKeys.Ascending(t => t.ReferenceId),
                // 只有引用令牌才有 reference_id,所以这条唯一索引必须稀疏。
                new CreateIndexOptions { Name = "ux_token_reference", Unique = true, Sparse = true }),
            new CreateIndexModel<OpenIddictMongoDbToken>(
                Builders<OpenIddictMongoDbToken>.IndexKeys
                    .Ascending(t => t.ApplicationId).Ascending(t => t.Status)
                    .Ascending(t => t.Subject).Ascending(t => t.Type),
                new CreateIndexOptions { Name = "ix_token_lookup" })
        ], cancel);
    }

    private async Task SeedScopesAsync(IServiceProvider provider, CancellationToken cancel)
    {
        IOpenIddictScopeManager manager = provider.GetRequiredService<IOpenIddictScopeManager>();

        foreach (ApiScopeOptions declared in server.Value.Scopes)
        {
            if (string.IsNullOrWhiteSpace(declared.Name))
            {
                continue;
            }

            OpenIddictScopeDescriptor descriptor = new()
            {
                Name = declared.Name,
                DisplayName = string.IsNullOrWhiteSpace(declared.DisplayName) ? declared.Name : declared.DisplayName
            };
            foreach (string resource in declared.Resources)
            {
                descriptor.Resources.Add(resource);
            }

            object? existing = await manager.FindByNameAsync(declared.Name, cancel);
            if (existing is null)
            {
                await manager.CreateAsync(descriptor, cancel);
                logger.LogInformation("已注册 scope {Scope}(受众 {Resources})。", declared.Name, string.Join(", ", declared.Resources));
            }
            else
            {
                await manager.UpdateAsync(existing, descriptor, cancel);
            }
        }
    }

    private async Task SeedClientsAsync(IServiceProvider provider, CancellationToken cancel)
    {
        IOpenIddictApplicationManager manager = provider.GetRequiredService<IOpenIddictApplicationManager>();

        foreach (ClientOptions declared in server.Value.Clients)
        {
            if (string.IsNullOrWhiteSpace(declared.ClientId))
            {
                continue;
            }

            bool confidential = !string.IsNullOrEmpty(declared.ClientSecret);
            OpenIddictApplicationDescriptor descriptor = new()
            {
                ClientId = declared.ClientId,
                ClientSecret = declared.ClientSecret,
                DisplayName = string.IsNullOrWhiteSpace(declared.DisplayName) ? declared.ClientId : declared.DisplayName,
                ApplicationType = ApplicationTypes.Web,
                ClientType = confidential ? ClientTypes.Confidential : ClientTypes.Public,
                // 第一方应用:不弹"是否授权"的确认页。用户点了登录就是同意,再问一遍只是噪音。
                ConsentType = ConsentTypes.Implicit,
                Permissions =
                {
                    Permissions.Endpoints.Authorization,
                    Permissions.Endpoints.Token,
                    Permissions.Endpoints.EndSession,
                    Permissions.GrantTypes.AuthorizationCode,
                    Permissions.GrantTypes.RefreshToken,
                    Permissions.ResponseTypes.Code,
                    Permissions.Scopes.Profile,
                    Permissions.Scopes.Email
                },
                Requirements =
                {
                    // 公开客户端没地方藏密钥,PKCE 是它唯一能证明"换码的人就是发起授权的人"的手段。
                    Requirements.Features.ProofKeyForCodeExchange
                }
            };

            foreach (string uri in declared.RedirectUris)
            {
                descriptor.RedirectUris.Add(new(uri, UriKind.Absolute));
            }
            foreach (string uri in declared.PostLogoutRedirectUris)
            {
                descriptor.PostLogoutRedirectUris.Add(new(uri, UriKind.Absolute));
            }
            foreach (string scopeName in declared.Scopes)
            {
                descriptor.Permissions.Add(Permissions.Prefixes.Scope + scopeName);
            }

            object? existing = await manager.FindByClientIdAsync(declared.ClientId, cancel);
            if (existing is null)
            {
                await manager.CreateAsync(descriptor, cancel);
                logger.LogInformation("已注册客户端 {ClientId},回跳白名单:{Uris}。",
                    declared.ClientId, string.Join(", ", declared.RedirectUris));
            }
            else
            {
                await manager.UpdateAsync(existing, descriptor, cancel);
                logger.LogInformation("已更新客户端 {ClientId} 的注册信息。", declared.ClientId);
            }
        }
    }

    private async Task SeedBootstrapAccountAsync(IServiceProvider provider, CancellationToken cancel)
    {
        AccountStore store = provider.GetRequiredService<AccountStore>();
        await store.EnsureIndexesAsync(cancel);

        if (!await store.IsEmptyAsync(cancel))
        {
            return;
        }

        BootstrapAccountOptions? bootstrap = accounts.Value.Bootstrap;
        if (bootstrap is null || string.IsNullOrWhiteSpace(bootstrap.UserName) || string.IsNullOrWhiteSpace(bootstrap.Password))
        {
            logger.LogInformation("还没有任何账号。到 {Issuer}/account/register 注册第一个,或配置 Accounts:Bootstrap 由启动时创建。",
                server.Value.Issuer);
            return;
        }

        RegistrationResult result = await store.CreateAsync(bootstrap.UserName, bootstrap.Password,
            bootstrap.Email, bootstrap.DisplayName ?? bootstrap.UserName, cancel);

        if (!result.Succeeded)
        {
            logger.LogWarning("首个账号 {UserName} 没建成:{Error}", bootstrap.UserName, result.Error);
            return;
        }

        // sub 就是市场那边 Auth:ModeratorSubjects 要填的值,这里直接打出来省得再去库里翻。
        logger.LogWarning("已创建首个账号 {UserName},其 sub 为 {Subject}。" +
                          "把它填进市场 API 的 Auth__ModeratorSubjects__0 即可开通审核台;并尽快改掉初始口令。",
            bootstrap.UserName, result.Account!.Id);
    }
}

/// <summary>
/// 定期清掉过期或已作废的令牌与授权记录。
///
/// 不清的话 tokens 集合会一直涨:每次登录、每次刷新都留一条。这不是"优化",
/// 是让一个长期运行的认证服务不至于把磁盘写满。
/// </summary>
public sealed class TokenPruningWorker(IServiceProvider services, ILogger<TokenPruningWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    /// <summary>作废满 14 天才清:留一段窗口,方便出事时还能查到痕迹。</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromDays(14);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken cancel)
    {
        using PeriodicTimer timer = new(Interval);
        do
        {
            try
            {
                await using AsyncServiceScope scope = services.CreateAsyncScope();
                DateTimeOffset threshold = DateTimeOffset.UtcNow - Retention;

                await scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>().PruneAsync(threshold, cancel);
                await scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>().PruneAsync(threshold, cancel);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                // 清理失败不该拖垮认证服务本身:记一笔,下个周期再来。
                logger.LogWarning(e, "清理过期令牌时出错,{Interval} 后重试。", Interval);
            }
        } while (await timer.WaitForNextTickAsync(cancel));
    }
}

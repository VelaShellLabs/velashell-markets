using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using VelaShell.Market.Identity.Options;

namespace VelaShell.Market.Identity.Accounts;

/// <summary>登录的结论。把"密码错"与"账号不存在"合并成同一种失败,避免枚举用户名。</summary>
public enum SignInStatus
{
    /// <summary>通过。</summary>
    Success,

    /// <summary>用户名或口令不对。</summary>
    InvalidCredentials,

    /// <summary>连续失败过多,已被临时锁定。</summary>
    LockedOut,

    /// <summary>账号被停用。</summary>
    Disabled
}

/// <summary>注册的结论。</summary>
/// <param name="Account">成功时的账号。</param>
/// <param name="Error">失败时给用户看的原因。</param>
public readonly record struct RegistrationResult(MarketAccount? Account, string? Error)
{
    /// <summary>是否成功。</summary>
    public bool Succeeded => Account is not null;
}

/// <summary>
/// 账号的读写与口令校验。
///
/// 这里不引入 ASP.NET Core Identity 的整套 UserManager/Store 体系:那套东西的价值在于
/// 角色、双因素、外部登录、令牌提供程序等一大批我们用不到的能力,代价是一层需要专门适配
/// MongoDB 的抽象。市场需要的只有"建账号、验口令、防爆破"三件事,直接落在集合上更清楚。
/// 唯独口令散列复用框架的 <see cref="PasswordHasher{TUser}" /> —— 自己写散列是最不该做的事。
/// </summary>
public sealed partial class AccountStore(IMongoDatabase database, IOptions<AccountOptions> options)
{
    private readonly IMongoCollection<MarketAccount> _accounts = database.GetCollection<MarketAccount>("accounts");
    private readonly PasswordHasher<MarketAccount> _hasher = new();

    /// <summary>用户名允许的字符:字母、数字、下划线、点、连字符,3~32 位。</summary>
    [GeneratedRegex(@"^[a-zA-Z0-9_.\-]{3,32}$")]
    private static partial Regex UserNamePattern { get; }

    /// <summary>建立唯一索引。用户名与邮箱的唯一性由**数据库**保证,不靠应用层的"先查再插"。</summary>
    public async Task EnsureIndexesAsync(CancellationToken cancel = default)
    {
        await _accounts.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<MarketAccount>(
                Builders<MarketAccount>.IndexKeys.Ascending(a => a.NormalizedUserName),
                new CreateIndexOptions { Name = "ux_account_username", Unique = true }),
            // 邮箱可以不填,所以这条索引必须是稀疏的 —— 否则第二个不填邮箱的账号会撞 null 的唯一约束。
            new CreateIndexModel<MarketAccount>(
                Builders<MarketAccount>.IndexKeys.Ascending(a => a.NormalizedEmail),
                new CreateIndexOptions { Name = "ux_account_email", Unique = true, Sparse = true })
        ], cancel);
    }

    /// <summary>按 <c>sub</c> 取账号。</summary>
    public async Task<MarketAccount?> FindByIdAsync(string id, CancellationToken cancel = default) =>
        await _accounts.Find(a => a.Id == id).FirstOrDefaultAsync(cancel);

    /// <summary>按用户名或邮箱取账号 —— 登录框里两种都收。</summary>
    public async Task<MarketAccount?> FindByLoginAsync(string login, CancellationToken cancel = default)
    {
        string normalized = Normalize(login);
        return await _accounts.Find(a => a.NormalizedUserName == normalized || a.NormalizedEmail == normalized)
                              .FirstOrDefaultAsync(cancel);
    }

    /// <summary>集合里一个账号都没有?播种首个管理账号时用它判断。</summary>
    public async Task<bool> IsEmptyAsync(CancellationToken cancel = default) =>
        await _accounts.CountDocumentsAsync(FilterDefinition<MarketAccount>.Empty,
            new CountOptions { Limit = 1 }, cancel) == 0;

    /// <summary>注册一个账号。用户名/邮箱重复由唯一索引挡下,这里把写冲突翻译成可读的提示。</summary>
    public async Task<RegistrationResult> CreateAsync(string userName, string password, string? email,
                                                      string? displayName, CancellationToken cancel = default)
    {
        userName = userName.Trim();
        email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        displayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();

        if (!UserNamePattern.IsMatch(userName))
        {
            return new(null, "用户名只能用字母、数字、下划线、点或连字符,长度 3~32 位。");
        }
        if (string.IsNullOrEmpty(password) || password.Length < options.Value.MinimumPasswordLength)
        {
            return new(null, $"口令至少 {options.Value.MinimumPasswordLength} 位。");
        }
        if (email is not null && (!email.Contains('@') || email.Length < 5))
        {
            return new(null, "邮箱格式不对。");
        }

        MarketAccount account = new()
        {
            UserName = userName,
            NormalizedUserName = Normalize(userName),
            Email = email,
            NormalizedEmail = email is null ? null : Normalize(email),
            DisplayName = displayName,
            PasswordHash = ""
        };
        account.PasswordHash = _hasher.HashPassword(account, password);

        try
        {
            await _accounts.InsertOneAsync(account, cancellationToken: cancel);
            return new(account, null);
        }
        catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // 唯一索引名直接告诉我们撞的是哪一条,不需要再回查一次。
            return new(null, e.WriteError.Message.Contains("ux_account_email", StringComparison.Ordinal)
                                 ? "这个邮箱已经注册过了。"
                                 : "这个用户名已经被占用了。");
        }
    }

    /// <summary>
    /// 校验口令。失败会累加计数并在超过阈值时锁定;成功则清零计数、记录登录时间,
    /// 并在散列参数过期时顺手升级散列。
    /// </summary>
    public async Task<SignInStatus> CheckPasswordAsync(MarketAccount account, string password,
                                                       CancellationToken cancel = default)
    {
        if (account.IsDisabled)
        {
            return SignInStatus.Disabled;
        }
        if (account.LockoutEndsAt is { } until && until > DateTime.UtcNow)
        {
            return SignInStatus.LockedOut;
        }

        PasswordVerificationResult verification = _hasher.VerifyHashedPassword(account, account.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            await RegisterFailureAsync(account, cancel);
            return account.LockoutEndsAt is { } end && end > DateTime.UtcNow
                       ? SignInStatus.LockedOut
                       : SignInStatus.InvalidCredentials;
        }

        UpdateDefinition<MarketAccount> update = Builders<MarketAccount>.Update
            .Set(a => a.AccessFailedCount, 0)
            .Set(a => a.LockoutEndsAt, null)
            .Set(a => a.LastLoginAt, DateTime.UtcNow);

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            update = update.Set(a => a.PasswordHash, _hasher.HashPassword(account, password));
        }

        await _accounts.UpdateOneAsync(a => a.Id == account.Id, update, cancellationToken: cancel);
        return SignInStatus.Success;
    }

    /// <summary>改口令。同时换掉安全戳,让已经发出去的刷新令牌在下次续期时失效。</summary>
    public async Task ChangePasswordAsync(MarketAccount account, string password, CancellationToken cancel = default)
    {
        await _accounts.UpdateOneAsync(a => a.Id == account.Id,
            Builders<MarketAccount>.Update
                                   .Set(a => a.PasswordHash, _hasher.HashPassword(account, password))
                                   .Set(a => a.SecurityStamp, Guid.NewGuid().ToString("N"))
                                   .Set(a => a.AccessFailedCount, 0)
                                   .Set(a => a.LockoutEndsAt, null),
            cancellationToken: cancel);
    }

    private async Task RegisterFailureAsync(MarketAccount account, CancellationToken cancel)
    {
        int failures = account.AccessFailedCount + 1;
        bool lockout = options.Value.MaxFailedAttempts > 0 && failures >= options.Value.MaxFailedAttempts;

        await _accounts.UpdateOneAsync(a => a.Id == account.Id,
            Builders<MarketAccount>.Update
                                   .Set(a => a.AccessFailedCount, lockout ? 0 : failures)
                                   .Set(a => a.LockoutEndsAt, lockout ? DateTime.UtcNow + options.Value.LockoutDuration : null),
            cancellationToken: cancel);

        if (lockout)
        {
            account.LockoutEndsAt = DateTime.UtcNow + options.Value.LockoutDuration;
        }
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}

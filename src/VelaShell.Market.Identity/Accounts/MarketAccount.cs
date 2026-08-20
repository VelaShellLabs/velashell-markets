using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace VelaShell.Market.Identity.Accounts;

/// <summary>本服务自用的声明名(标准 OIDC 声明一律用 <c>OpenIddictConstants.Claims</c>)。</summary>
public static class MarketClaims
{
    /// <summary>
    /// 安全戳在会话 cookie 与刷新令牌里的声明名。
    ///
    /// 刻意**不给它任何 Destination**:它既不该出现在访问令牌里(市场 API 用不着,
    /// 而访问令牌是能被解开看的),也不该进 id_token。没有 destination 的声明仍然会被
    /// OpenIddict 存进授权码与刷新令牌 —— 那正是续期时要比对的地方。
    /// </summary>
    public const string SecurityStamp = "velashell:stamp";
}

/// <summary>
/// 一个可登录的账号。
///
/// 文档 <c>_id</c> 就是令牌里的 <c>sub</c>,也是市场那边 <c>Plugin.OwnerSubject</c> /
/// <c>Review.Subject</c> 存的值。因此**它一旦签发就不能改** —— 换了 sub 等于换了个人,
/// 历史插件与评价会集体失去归属。用户名和邮箱都允许改,sub 不允许。
/// </summary>
public sealed class MarketAccount
{
    /// <summary>账号标识,同时是 OIDC 的 <c>sub</c>。</summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>登录用的用户名。</summary>
    public required string UserName { get; set; }

    /// <summary>用户名的规范化形式(小写)。唯一索引建在它上面,于是"Alice"与"alice"不能同时存在。</summary>
    public required string NormalizedUserName { get; set; }

    /// <summary>邮箱,可空。填了就能用邮箱登录。</summary>
    public string? Email { get; set; }

    /// <summary>邮箱的规范化形式(小写)。</summary>
    public string? NormalizedEmail { get; set; }

    /// <summary>显示名,进入令牌的 <c>name</c> 声明。留空时退回用户名。</summary>
    public string? DisplayName { get; set; }

    /// <summary>口令散列(PBKDF2,由 <see cref="Microsoft.AspNetCore.Identity.PasswordHasher{TUser}" /> 产生)。</summary>
    public required string PasswordHash { get; set; }

    /// <summary>
    /// 安全戳。改口令或禁用账号时换一个新值 —— 已签发的刷新令牌会在下次续期时对不上而失效。
    /// </summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>创建时间(UTC)。</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>最近一次登录成功的时间(UTC)。</summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>是否已停用。停用后既登不进来,已有的刷新令牌也换不出新的访问令牌。</summary>
    public bool IsDisabled { get; set; }

    /// <summary>连续登录失败次数,成功一次即清零。</summary>
    public int AccessFailedCount { get; set; }

    /// <summary>锁定到期时间(UTC)。为空表示未锁定。</summary>
    public DateTime? LockoutEndsAt { get; set; }
}

using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace VelaShell.Market.Identity.Security;

/// <summary>
/// 令牌签名与加密用的 RSA 密钥,首次启动生成、之后从磁盘复用。
///
/// 为什么不用 OpenIddict 的 <c>AddDevelopmentSigningCertificate()</c>:那玩意儿把证书写进
/// 当前用户的 X.509 存储。容器里跑的进程通常没有可写的用户配置目录,而且它明确只适合开发。
/// 为什么不用证书文件:证书在这里唯一的作用就是装一对 RSA 密钥,还要额外操心 PFX 口令与
/// 各平台不一样的 <c>X509KeyStorageFlags</c>。直接存裸密钥,少一层没有收益的包装。
///
/// **密钥目录必须持久化**(compose 里挂了 <c>identity-keys</c> 卷)。丢了密钥等于换了签发者:
/// 所有已签发的令牌一起失效,所有人被登出。
/// </summary>
public sealed class TokenKeyProvider
{
    private const int KeySize = 2048;

    private readonly string _directory;
    private readonly ILogger<TokenKeyProvider> _logger;

    /// <summary>按配置的目录准备好两把密钥。</summary>
    public TokenKeyProvider(string directory, ILogger<TokenKeyProvider> logger)
    {
        _directory = Path.GetFullPath(directory);
        _logger = logger;
        Directory.CreateDirectory(_directory);

        SigningKey = Load("signing.key");
        EncryptionKey = Load("encryption.key");
    }

    /// <summary>签名密钥。公钥经 JWKS 端点对外发布,资源服务器用它验签。</summary>
    public RsaSecurityKey SigningKey { get; }

    /// <summary>
    /// 加密密钥。授权码与刷新令牌由 OpenIddict 加密后才交给客户端 ——
    /// 它们是纯粹的内部凭据,外面没有任何人需要读得懂。访问令牌不加密(见 Program.cs)。
    /// </summary>
    public RsaSecurityKey EncryptionKey { get; }

    private RsaSecurityKey Load(string fileName)
    {
        string path = Path.Combine(_directory, fileName);
        RSA rsa = RSA.Create(KeySize);

        if (File.Exists(path))
        {
            rsa.ImportRSAPrivateKey(File.ReadAllBytes(path), out _);
            return new(rsa);
        }

        File.WriteAllBytes(path, rsa.ExportRSAPrivateKey());
        // 密钥只该被本进程读到。Windows 上 ACL 由目录继承,这里的调用是空操作。
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        _logger.LogInformation("已生成新的 {File},存于 {Directory}。请确保该目录被持久化,否则重启会让所有令牌失效。",
            fileName, _directory);
        return new(rsa);
    }
}

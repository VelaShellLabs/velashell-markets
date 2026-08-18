namespace VelaShell.Market.Infrastructure.Storage;

/// <summary>
/// 对象存储配置(S3 协议;MinIO 是默认部署形态,换成 AWS S3 / OSS / COS 只改这里)。
/// </summary>
public sealed class ObjectStorageOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "ObjectStorage";

    /// <summary>服务端点,如 <c>http://minio:9000</c>。</summary>
    public string Endpoint { get; set; } = "http://localhost:9000";

    /// <summary>访问密钥。</summary>
    public string AccessKey { get; set; } = "";

    /// <summary>密钥。</summary>
    public string SecretKey { get; set; } = "";

    /// <summary>区域(MinIO 随便填,但 SigV4 需要一个值)。</summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>
    /// 隔离桶:上传先落这里。**永不对外开放读取**,连预签名下载都不给 ——
    /// 隔离区里的东西按定义就是"还没证明无害"的。
    /// </summary>
    public string QuarantineBucket { get; set; } = "vpx-quarantine";

    /// <summary>正式桶:通过检测后搬过来,下载一律走这里的预签名 URL。</summary>
    public string PublicBucket { get; set; } = "vpx-public";

    /// <summary>下载用预签名 URL 的有效期。</summary>
    public TimeSpan DownloadUrlLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>单个包的最大字节数(与 VelaShell 容器的 512MB 载荷上限对齐,略留余量给头与签名尾)。</summary>
    public long MaxPackageBytes { get; set; } = 512L * 1024 * 1024;

    /// <summary>被拒绝的包在隔离桶里的保留天数(留给作者申诉与我们复盘,过期由清理任务删除)。</summary>
    public int RejectedRetentionDays { get; set; } = 30;
}

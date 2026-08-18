using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VelaShell.Market.Infrastructure.Storage;

/// <summary>
/// 插件包的对象存储。**两段式**是这里唯一重要的设计:
/// 上传只写隔离桶,检测通过后才服务端复制进正式桶,下载只从正式桶签 URL。
/// <para>
/// 为什么不"先传正式桶再打个未发布标记":那样一旦有人猜到键名(或某天某个改动放宽了桶策略),
/// 未检测的包就直接可下载了。物理分桶让"没过检测的东西能被下载"这件事需要**两处**同时出错才会发生。
/// </para>
/// </summary>
public sealed class PackageStorage(IAmazonS3 s3, IOptions<ObjectStorageOptions> options, ILogger<PackageStorage> logger)
{
    private readonly ObjectStorageOptions _options = options.Value;

    /// <summary>确保两个桶都存在(启动时调用一次;桶已存在不算错误)。</summary>
    public async Task EnsureBucketsAsync(CancellationToken cancellationToken = default)
    {
        foreach (string bucket in (string[])[_options.QuarantineBucket, _options.PublicBucket])
        {
            try
            {
                await s3.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Created object storage bucket {Bucket}.", bucket);
            }
            catch (AmazonS3Exception ex) when (ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
            {
                // 已存在是正常路径。
            }
        }
    }

    /// <summary>把上传流写进**隔离桶**。返回写入的字节数。</summary>
    public async Task<long> PutQuarantineAsync(string key, Stream content, CancellationToken cancellationToken = default)
    {
        var request = new PutObjectRequest
        {
            BucketName = _options.QuarantineBucket,
            Key = key,
            InputStream = content,
            // 隔离区的东西一律按不可识别的二进制对待,绝不给一个会让浏览器尝试解析的类型。
            ContentType = "application/octet-stream"
        };
        PutObjectResponse response = await s3.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Stored {Key} in quarantine (etag {ETag}).", key, response.ETag);
        return content.CanSeek ? content.Length : 0;
    }

    /// <summary>打开隔离桶里的对象(检测流水线读取用)。</summary>
    public async Task<Stream> OpenQuarantineAsync(string key, CancellationToken cancellationToken = default)
    {
        GetObjectResponse response = await s3.GetObjectAsync(_options.QuarantineBucket, key, cancellationToken).ConfigureAwait(false);
        return response.ResponseStream;
    }

    /// <summary>
    /// 检测通过:把对象从隔离桶复制进正式桶,再删掉隔离桶里的那份。
    /// 顺序不能反 —— 先删后复制的话,中途失败就永久丢包了。
    /// </summary>
    public async Task PromoteAsync(string key, CancellationToken cancellationToken = default)
    {
        await s3.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket = _options.QuarantineBucket,
            SourceKey = key,
            DestinationBucket = _options.PublicBucket,
            DestinationKey = key
        }, cancellationToken).ConfigureAwait(false);
        await s3.DeleteObjectAsync(_options.QuarantineBucket, key, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Promoted {Key} from quarantine to the public bucket.", key);
    }

    /// <summary>删除隔离桶里的对象(拒收包过了保留期,或上传中途失败的清理)。</summary>
    public Task DeleteQuarantineAsync(string key, CancellationToken cancellationToken = default) =>
        s3.DeleteObjectAsync(_options.QuarantineBucket, key, cancellationToken);

    /// <summary>删除正式桶里的对象(版本被撤回)。</summary>
    public Task DeletePublicAsync(string key, CancellationToken cancellationToken = default) =>
        s3.DeleteObjectAsync(_options.PublicBucket, key, cancellationToken);

    /// <summary>
    /// 为正式桶里的对象签一个短时效下载 URL。**只签正式桶** —— 这个方法没有隔离桶的重载,
    /// 是为了让"给隔离区签 URL"连写都写不出来。
    /// </summary>
    public string CreateDownloadUrl(string key, string fileName)
    {
        return s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.PublicBucket,
            Key = key,
            Expires = DateTime.UtcNow.Add(_options.DownloadUrlLifetime),
            // 默认按 HTTPS 签;Endpoint 是 http 的 MinIO 时必须显式改回 HTTP,
            // 否则签出来的 URL 打不开(MinIO 上根本没有 TLS)。
            Protocol = Protocol.HTTP,
            // 让浏览器直接落成 .vpx 文件而不是尝试展示。
            ResponseHeaderOverrides = new()
            {
                ContentType = "application/vnd.velashell.plugin",
                ContentDisposition = $"attachment; filename=\"{fileName}\""
            }
        });
    }

    /// <summary>对象键的约定:<c>&lt;插件id&gt;/&lt;版本&gt;/&lt;文件名&gt;</c>。隔离桶与正式桶用同一个键,搬运即原样复制。</summary>
    public static string BuildObjectKey(string pluginId, string version) =>
        $"{pluginId}/{version}/{pluginId}-{version}.vpx";
}

using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using VelaShell.Market.Domain;
using VelaShell.Market.Infrastructure.Persistence;
using VelaShell.Market.Infrastructure.Scanning;
using VelaShell.Market.Infrastructure.Storage;
using VelaShell.PluginSdk;
using VelaShell.PluginSdk.Manifest;
using VelaShell.PluginSdk.Packaging;

namespace VelaShell.Market.Api.Endpoints;

/// <summary>上传与隔离检测相关的端点。</summary>
public static class UploadEndpoints
{
    /// <summary>挂载端点。</summary>
    public static void MapUploadEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/uploads").RequireAuthorization().WithTags("Uploads");

        group.MapPost("/", UploadAsync)
             .DisableAntiforgery()
             .WithSummary("上传一个 .vpx 包。包先落隔离区,检测通过后才会发布。");

        group.MapGet("/mine", MineAsync)
             .WithSummary("我上传的版本(含仍在隔离区与被拒的,带完整检测报告)。");
    }

    /// <summary>
    /// 上传。这个方法只做三件事:**先读清单认出这是谁的什么版本 → 落隔离桶 → 入队**。
    /// 真正的检测一律在后台流水线里做 —— 上传请求不该被一次全包病毒扫描拖成几分钟的长连接。
    /// </summary>
    private static async Task<IResult> UploadAsync(
        IFormFile file,
        [FromForm] string? description,
        [FromForm] string? releaseNotes,
        [FromForm] string? tags,
        ClaimsPrincipal user,
        MarketDbContext db,
        PackageStorage storage,
        ScanQueue queue,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ILogger logger = loggerFactory.CreateLogger("Upload");
        string subject = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated principal has no subject claim.");

        if (file.Length == 0)
        {
            return Results.BadRequest(new { error = "文件为空。" });
        }

        // 先把包落到本地临时文件:容器读取要 Seek,而上传流是只进不回的。
        string temp = Path.Combine(Path.GetTempPath(), $"vpx-upload-{Guid.NewGuid():N}.vpx");
        try
        {
            await using (FileStream local = File.Create(temp))
            {
                await file.CopyToAsync(local, cancellationToken).ConfigureAwait(false);
            }

            // 只做"认领"所需的最小解析:容器合法性 + 清单。这一步失败就没必要占用隔离区空间了。
            PluginManifest manifest;
            VpxPackageInfo info;
            try
            {
                await using Stream payload = VpxContainer.OpenPayload(temp, out info);
                using var archive = new System.IO.Compression.ZipArchive(payload, System.IO.Compression.ZipArchiveMode.Read);
                System.IO.Compression.ZipArchiveEntry? entry = archive.GetEntry(PluginManifestReader.FileName);
                if (entry is null)
                {
                    return Results.BadRequest(new { error = $"包内没有 {PluginManifestReader.FileName}。" });
                }
                using StreamReader reader = new(entry.Open());
                manifest = PluginManifestReader.Parse(await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (VpxFormatException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (PluginManifestException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            // 归属:插件 id 一旦被谁认领,后续版本只能由同一个人发。
            Plugin? plugin = await db.Plugins.Find(p => p.Id == manifest.Id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (plugin is null)
            {
                plugin = new()
                {
                    Id = manifest.Id,
                    OwnerSubject = subject,
                    OwnerName = user.FindFirstValue("name") ?? user.FindFirstValue("preferred_username"),
                    DisplayName = manifest.DisplayName,
                    Summary = manifest.Description,
                    Author = manifest.Author ?? manifest.Publisher,
                    Publisher = manifest.Publisher,
                    Homepage = manifest.Homepage,
                    License = manifest.License,
                    DescriptionMarkdown = description ?? "",
                    Tags = NormalizeTags(tags)
                };
                await db.Plugins.InsertOneAsync(plugin, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            else if (!string.Equals(plugin.OwnerSubject, subject, StringComparison.Ordinal))
            {
                logger.LogWarning("{Subject} attempted to publish {PluginId} owned by {Owner}.", subject, manifest.Id, plugin.OwnerSubject);
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                    detail: $"插件 id '{manifest.Id}' 已由其他账号认领。插件 id 是全局唯一的,请改用你自己的发布者前缀。");
            }
            else
            {
                UpdateDefinition<Plugin> update = Builders<Plugin>.Update.Set(p => p.UpdatedAt, DateTime.UtcNow);
                if (!string.IsNullOrWhiteSpace(description))
                {
                    update = update.Set(p => p.DescriptionMarkdown, description);
                }
                if (!string.IsNullOrWhiteSpace(tags))
                {
                    update = update.Set(p => p.Tags, NormalizeTags(tags));
                }
                await db.Plugins.UpdateOneAsync(p => p.Id == plugin.Id, update, cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            // 同版本重复上传:已发布的一律拒绝(改内容不改版本号会让已装用户永远拿不到修复);
            // 还在隔离区或被拒的允许覆盖重传。
            PluginVersion? existing = await db.Versions
                                              .Find(v => v.PluginId == manifest.Id && v.Version == manifest.Version)
                                              .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (existing is { Status: PluginVersionStatus.Published })
            {
                return Results.Conflict(new { error = $"{manifest.Id} 的 {manifest.Version} 已发布。请提升版本号后再上传。" });
            }

            string objectKey = PackageStorage.BuildObjectKey(manifest.Id, manifest.Version);
            string fileSha;
            await using (FileStream local = File.OpenRead(temp))
            {
                fileSha = VpxStaticInspector.ComputeFileSha256(local);
            }
            await using (FileStream local = File.OpenRead(temp))
            {
                await storage.PutQuarantineAsync(objectKey, local, cancellationToken).ConfigureAwait(false);
            }

            var version = new PluginVersion
            {
                Id = existing?.Id ?? MongoDB.Bson.ObjectId.GenerateNewId(),
                PluginId = manifest.Id,
                Version = manifest.Version,
                Status = PluginVersionStatus.Quarantined,
                ApiLevel = manifest.ApiLevel,
                MinHostVersion = manifest.MinHostVersion,
                HostMode = manifest.HostMode.ToString(),
                Entry = manifest.Entry,
                ReleaseNotesMarkdown = releaseNotes ?? "",
                PackageSize = file.Length,
                PayloadSha256 = info.PayloadSha256,
                FileSha256 = fileSha,
                SignatureState = VpxContainer.VerifySignature(info).ToString(),
                SignaturePublicKey = info.Signature?.PublicKey,
                ObjectKey = objectKey,
                UploadedBySubject = subject,
                UploadedAt = DateTime.UtcNow,
                Scan = new() { Verdict = ScanVerdict.Pending }
            };
            await db.Versions.ReplaceOneAsync(v => v.Id == version.Id, version,
                new ReplaceOptions { IsUpsert = true }, cancellationToken).ConfigureAwait(false);

            queue.Enqueue(version.Id);
            logger.LogInformation("{PluginId} {Version} accepted into quarantine by {Subject}.", manifest.Id, manifest.Version, subject);
            return Results.Accepted($"/api/uploads/mine", new
            {
                pluginId = manifest.Id,
                version = manifest.Version,
                status = version.Status.ToString(),
                message = "包已进入隔离区,检测通过后会自动发布。可在「我的上传」里查看检测报告。"
            });
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
                // 临时文件删不掉不影响上传结果。
            }
        }
    }

    private static async Task<IResult> MineAsync(ClaimsPrincipal user, MarketDbContext db, CancellationToken cancellationToken)
    {
        string subject = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        List<PluginVersion> versions = await db.Versions
                                               .Find(v => v.UploadedBySubject == subject)
                                               .SortByDescending(v => v.UploadedAt)
                                               .Limit(200)
                                               .ToListAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(versions.Select(v => new
        {
            v.PluginId,
            v.Version,
            status = v.Status.ToString(),
            v.UploadedAt,
            v.PublishedAt,
            v.PackageSize,
            signature = v.SignatureState,
            scan = v.Scan is null
                ? null
                : new
                {
                    verdict = v.Scan.Verdict.ToString(),
                    v.Scan.StartedAt,
                    v.Scan.CompletedAt,
                    v.Scan.Engines,
                    findings = v.Scan.Findings.Select(f => new { f.Code, severity = f.Severity.ToString(), f.Message, f.Path })
                }
        }));
    }

    /// <summary>标签归一:小写、去空白、去重、限量。标签是检索维度,不是自由文本。</summary>
    private static List<string> NormalizeTags(string? tags) =>
        (tags ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(t => t.ToLowerInvariant())
                    .Where(t => t.Length <= 32)
                    .Distinct(StringComparer.Ordinal)
                    .Take(10)
                    .ToList();
}

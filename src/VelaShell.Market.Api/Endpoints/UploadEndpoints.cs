using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using VelaShell.Market.Api.Services;
using VelaShell.Market.Domain;
using VelaShell.Market.Infrastructure.Persistence;
using VelaShell.Market.Infrastructure.Scanning;
using VelaShell.Market.Infrastructure.Storage;
using VelaShell.PluginSdk;
using VelaShell.PluginSdk.Manifest;
using VelaShell.PluginSdk.Packaging;

namespace VelaShell.Market.Api.Endpoints;

/// <summary>Markdown 预览请求体。</summary>
/// <param name="Markdown">原文。</param>
public sealed record PreviewRequest(string? Markdown);

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

        group.MapPost("/inspect", InspectAsync)
             .DisableAntiforgery()
             .WithSummary("只读预检:读出包内清单与签名状态,不落盘、不入库、不排队。");

        group.MapPost("/preview", PreviewAsync)
             .WithSummary("把 Markdown 渲染成插件页上最终会显示的 HTML。");

        group.MapGet("/mine", MineAsync)
             .WithSummary("我上传的版本(含仍在隔离区与被拒的,带完整检测报告)。");
    }

    /// <summary>
    /// 说明文本的预览。
    ///
    /// 走服务端而不是在前端塞一个 Markdown 库:插件页上的 HTML 是 Markdig 渲染并清洗过的
    /// (关掉 HTML 直通、洗掉 scheme 与内联事件),前端换一个渲染器就意味着
    /// **预览与实际发布出来的东西可能不一样** —— 而作者恰恰是靠这个预览决定"写好了没有"。
    /// 同一个渲染器出的结果才叫预览。
    /// </summary>
    private static IResult PreviewAsync([FromBody] PreviewRequest request, MarkdownRenderer markdown)
    {
        if (request.Markdown is { Length: > 50000 })
        {
            return Results.BadRequest(new { error = "文本过长。" });
        }
        return Results.Ok(new { html = markdown.ToHtml(request.Markdown) });
    }

    /// <summary>
    /// 预检。发布页在用户选好文件、还没点「上传并送检」之前调它,把包内清单摊开给人看。
    ///
    /// 为什么要有这一步:<c>plugin.json</c> 里的 id / 版本 / apiLevel 决定了这次上传
    /// **认领的是哪个插件的哪个版本**,而这些字段页面上改不了。等真上传完再在
    /// 「我的上传」里发现"版本号忘了提"或"id 打错前缀被判给了别人",代价是一次完整往返
    /// 加一次隔离区占用。这里提前一步把结论说清楚,包括"这个 id 已被别人认领"和
    /// "这个版本已经发布过"这两种上传必然失败的情况。
    ///
    /// 它**不做**病毒扫描,也不写任何状态 —— 真正的检测一律在隔离区里进行,
    /// 别把这个端点当成一条绕过隔离区的捷径。
    /// </summary>
    private static async Task<IResult> InspectAsync(
        IFormFile file,
        ClaimsPrincipal user,
        MarketDbContext db,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return Results.BadRequest(new { error = "文件为空。" });
        }

        string subject = user.Subject();
        string temp = Path.Combine(Path.GetTempPath(), $"vpx-inspect-{Guid.NewGuid():N}.vpx");
        try
        {
            await using (FileStream local = File.Create(temp))
            {
                await file.CopyToAsync(local, cancellationToken).ConfigureAwait(false);
            }

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

            Plugin? plugin = await db.Plugins.Find(p => p.Id == manifest.Id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            string ownership = plugin is null
                                   ? "new"
                                   : string.Equals(plugin.OwnerSubject, subject, StringComparison.Ordinal)
                                       ? "yours"
                                       : "taken";
            PluginVersion? existing = plugin is null
                                          ? null
                                          : await db.Versions.Find(v => v.PluginId == manifest.Id && v.Version == manifest.Version)
                                                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            string versionState = existing is null
                                      ? "new"
                                      : existing.Status == PluginVersionStatus.Published
                                          ? "published"
                                          : "reupload";

            string fileSha;
            await using (FileStream local = File.OpenRead(temp))
            {
                fileSha = VpxStaticInspector.ComputeFileSha256(local);
            }

            return Results.Ok(new
            {
                pluginId = manifest.Id,
                manifest.Version,
                manifest.DisplayName,
                manifest.Description,
                manifest.Author,
                manifest.Publisher,
                manifest.ApiLevel,
                hostMode = manifest.HostMode.ToString(),
                manifest.MinHostVersion,
                manifest.MinSdkVersion,
                manifest.Entry,
                manifest.License,
                manifest.Homepage,
                activationEvents = ManifestProjection.ToActivationEvents(manifest),
                contributes = ManifestProjection.ToContributions(manifest) is { IsEmpty: false } c ? c : null,
                packageSize = file.Length,
                payloadSha256 = info.PayloadSha256,
                fileSha256 = fileSha,
                signature = VpxContainer.VerifySignature(info).ToString(),
                signatureFingerprint = info.Signature is { } s ? VpxContainer.PublicKeyFingerprint(s.PublicKey) : null,
                // 上传必然失败的两种情况提前说清楚,别让人白等一次完整往返。
                ownership,
                versionState,
                ownerName = ownership == "taken" ? plugin!.OwnerName : null
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
                // 临时文件删不掉不影响预检结果。
            }
        }
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
        string subject = user.Subject();

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
                    Tags = TagList.Normalize(tags)
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
                    update = update.Set(p => p.Tags, TagList.Normalize(tags));
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
                MinSdkVersion = manifest.MinSdkVersion,
                HostMode = manifest.HostMode.ToString(),
                IdlePolicy = manifest.IdlePolicy.ToString(),
                Entry = manifest.Entry,
                ActivationEvents = ManifestProjection.ToActivationEvents(manifest),
                Contributes = ManifestProjection.ToContributions(manifest),
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
        string subject = user.Subject();
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

}

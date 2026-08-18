using MongoDB.Bson;
using MongoDB.Driver;
using VelaShell.Market.Domain;
using VelaShell.Market.Infrastructure.Persistence;
using VelaShell.Market.Infrastructure.Scanning;
using VelaShell.Market.Infrastructure.Storage;
using VelaShell.PluginSdk.Packaging;

namespace VelaShell.Market.Api.Services;

/// <summary>
/// 演示数据播种。**默认关闭**,由 <c>Market:SeedDemoData=true</c> 打开,且只在插件集合为空时执行 ——
/// 生产库里绝不会莫名多出几个假插件。
/// <para>
/// 刻意不直接往数据库里塞"已发布"的假记录:那样列表页有东西看,点下载却 404,
/// 演示出来的是个假象。这里的做法是**真的打出 .vpx 包、真的落隔离桶、真的走一遍检测流水线** ——
/// 于是你看到的首页、下载、检测报告全是真实链路的产物,顺带也把流水线跑通了一遍。
/// </para>
/// </summary>
public sealed class DemoDataSeeder(
    IServiceProvider services,
    IConfiguration configuration,
    ILogger<DemoDataSeeder> logger) : IHostedService
{
    private sealed record Demo(string Id, string Name, string Summary, string[] Tags, string Description);

    private static readonly Demo[] Samples =
    [
        new("demo.docker-manager", "容器管理器", "在 VelaShell 里查看与操作远端 Docker 容器。",
            ["docker", "运维", "容器"],
            """
            ## 它能做什么

            连上一台跑着 Docker 的机器后,在停靠面板里直接看到容器列表、日志与资源占用,
            常用操作(启停、重启、进容器)都不用再敲一遍命令。

            ## 怎么用

            1. 在命令面板里运行 `demo.docker-manager.open`;
            2. 选一个已连接的 SSH 会话;
            3. 面板会用 `RemoteExec` 拉取 `docker ps` 的结构化输出。

            > 这是市场的演示数据,包体是一个空壳,装上不会有实际功能。
            """),
        new("demo.log-tailer", "日志跟随", "多文件 tail -f,带高亮与正则过滤。",
            ["日志", "运维", "ssh"],
            """
            ## 特性

            - 同时跟随多个远端文件,合并成一条时间线
            - 正则过滤与关键字高亮
            - 断线自动续跟

            ```bash
            # 面板里等价于
            tail -F /var/log/nginx/*.log
            ```

            > 这是市场的演示数据。
            """),
        new("demo.json-toolkit", "JSON 工具箱", "格式化、JSONPath 查询与差异对比。",
            ["工具", "json"],
            """
            ## 说明

            把终端里拷出来的一坨 JSON 丢进面板,立刻得到格式化结果;
            支持 JSONPath 取值,以及两份 JSON 的差异对比。

            > 这是市场的演示数据。
            """),
    ];

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue("Market:SeedDemoData", false))
        {
            return;
        }
        using IServiceScope scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<PackageStorage>();
        var queue = scope.ServiceProvider.GetRequiredService<ScanQueue>();

        if (await db.Plugins.CountDocumentsAsync(FilterDefinition<Plugin>.Empty, cancellationToken: cancellationToken).ConfigureAwait(false) > 0)
        {
            logger.LogInformation("Demo seeding skipped: the catalogue already has plugins.");
            return;
        }

        foreach (Demo demo in Samples)
        {
            try
            {
                await SeedAsync(demo, db, storage, queue, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 演示数据播不进去绝不能让服务起不来。
                logger.LogWarning(ex, "Failed to seed demo plugin {PluginId}.", demo.Id);
            }
        }
        logger.LogInformation("Seeded {Count} demo plugin(s); they are now going through the real review pipeline.", Samples.Length);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task SeedAsync(Demo demo, MarketDbContext db, PackageStorage storage,
        ScanQueue queue, CancellationToken cancellationToken)
    {
        const string version = "1.0.0";
        string stage = Path.Combine(Path.GetTempPath(), $"vpx-demo-{Guid.NewGuid():N}");
        string package = stage + ".vpx";
        try
        {
            Directory.CreateDirectory(stage);
            await File.WriteAllTextAsync(Path.Combine(stage, "plugin.json"), $$"""
                {
                  "id": "{{demo.Id}}",
                  "version": "{{version}}",
                  "displayName": "{{demo.Name}}",
                  "description": "{{demo.Summary}}",
                  "author": "VelaShell Demo",
                  "publisher": "demo",
                  "entry": "Demo.dll",
                  "apiLevel": 1,
                  "license": "MIT",
                  "homepage": "https://github.com/joesdu/VelaShell"
                }
                """, cancellationToken).ConfigureAwait(false);
            // 入口程序集必须真的存在(检测器会查),内容是什么无所谓 —— 演示包本就不会被装载。
            await File.WriteAllBytesAsync(Path.Combine(stage, "Demo.dll"),
                [.. Enumerable.Range(0, 2048).Select(i => (byte)(i * 7 % 251))], cancellationToken).ConfigureAwait(false);
            VpxContainer.Pack(stage, package);

            VpxPackageInfo info = VpxContainer.ReadInfo(package);
            string objectKey = PackageStorage.BuildObjectKey(demo.Id, version);
            string fileSha;
            await using (FileStream file = File.OpenRead(package))
            {
                fileSha = VpxStaticInspector.ComputeFileSha256(file);
            }
            await using (FileStream file = File.OpenRead(package))
            {
                await storage.PutQuarantineAsync(objectKey, file, cancellationToken).ConfigureAwait(false);
            }

            await db.Plugins.InsertOneAsync(new()
            {
                Id = demo.Id,
                OwnerSubject = "demo-seed",
                OwnerName = "VelaShell Demo",
                DisplayName = demo.Name,
                Summary = demo.Summary,
                DescriptionMarkdown = demo.Description,
                Author = "VelaShell Demo",
                Publisher = "demo",
                License = "MIT",
                Homepage = "https://github.com/joesdu/VelaShell",
                Tags = [.. demo.Tags]
            }, cancellationToken: cancellationToken).ConfigureAwait(false);

            var pluginVersion = new PluginVersion
            {
                Id = ObjectId.GenerateNewId(),
                PluginId = demo.Id,
                Version = version,
                Status = PluginVersionStatus.Quarantined,
                ApiLevel = 1,
                HostMode = "InProcess",
                Entry = "Demo.dll",
                ReleaseNotesMarkdown = "首个演示版本。",
                PackageSize = new FileInfo(package).Length,
                PayloadSha256 = info.PayloadSha256,
                FileSha256 = fileSha,
                SignatureState = VpxContainer.VerifySignature(info).ToString(),
                ObjectKey = objectKey,
                UploadedBySubject = "demo-seed",
                Scan = new() { Verdict = ScanVerdict.Pending }
            };
            await db.Versions.InsertOneAsync(pluginVersion, cancellationToken: cancellationToken).ConfigureAwait(false);
            queue.Enqueue(pluginVersion.Id);
        }
        finally
        {
            try
            {
                Directory.Delete(stage, recursive: true);
                File.Delete(package);
            }
            catch (IOException)
            {
                // 临时文件清不掉不影响播种结果。
            }
        }
    }
}

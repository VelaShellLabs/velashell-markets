using System.IO.Compression;
using System.Text;
using VelaShell.Market.Domain;
using VelaShell.Market.Infrastructure.Scanning;
using VelaShell.PluginSdk.Packaging;

namespace VelaShell.Market.Tests;

/// <summary>
/// 静态检查器的地面真值。这些用例守的是**安全边界** —— 一旦它们全绿而真实行为变了,
/// 意味着有害的包能进正式桶。所以每条用例都直接构造真实的包字节,不用替身。
/// </summary>
[TestClass]
public class VpxStaticInspectorTests
{
    private string _work = null!;

    [TestInitialize]
    public void Setup()
    {
        _work = Path.Combine(Path.GetTempPath(), "market-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_work);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_work, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不影响结论。
        }
    }

    /// <summary>造一个真正的 .vpx:给定包内文件,清单按参数生成。</summary>
    private string BuildPackage(string id = "acme.demo", string version = "1.0.0",
        Dictionary<string, byte[]>? extraFiles = null, string? manifestOverride = null)
    {
        string stage = Path.Combine(_work, "stage-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(stage);
        File.WriteAllText(Path.Combine(stage, "plugin.json"), manifestOverride ?? $$"""
            { "id": "{{id}}", "version": "{{version}}", "displayName": "Demo", "entry": "Demo.dll" }
            """);
        File.WriteAllBytes(Path.Combine(stage, "Demo.dll"), [.. Enumerable.Range(0, 512).Select(i => (byte)i)]);
        foreach ((string name, byte[] content) in extraFiles ?? [])
        {
            string path = Path.Combine(stage, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, content);
        }
        string package = Path.Combine(_work, $"{id}-{version}.vpx");
        VpxContainer.Pack(stage, package);
        return package;
    }

    private static VpxInspection Inspect(string package, string id = "acme.demo", string version = "1.0.0")
    {
        using FileStream stream = File.OpenRead(package);
        return VpxStaticInspector.Inspect(stream, id, version);
    }

    [TestMethod]
    public void CleanPackage_HasNoBlockingOrWarningFindings()
    {
        VpxInspection result = Inspect(BuildPackage());
        Assert.DoesNotContain(f => f.Severity != ScanSeverity.Info, result.Findings,
            "干净的包不该产生任何告警:" + string.Join("; ", result.Findings.Select(f => f.Code)));
        Assert.IsNotNull(result.Manifest);
        Assert.AreEqual("acme.demo", result.Manifest.Id);
    }

    [TestMethod]
    public void PlainZipRenamedToVpx_IsBlocked()
    {
        // 市场只认容器格式。改后缀的 zip 在门口就该被挡下,而不是靠后面某一层碰巧发现。
        string stage = Path.Combine(_work, "plain");
        Directory.CreateDirectory(stage);
        File.WriteAllText(Path.Combine(stage, "plugin.json"), """{ "id": "a.b", "version": "1.0.0", "displayName": "X", "entry": "X.dll" }""");
        string fake = Path.Combine(_work, "fake.vpx");
        ZipFile.CreateFromDirectory(stage, fake);

        VpxInspection result = Inspect(fake, "a.b");
        Assert.Contains(f => f is { Code: "VPX_FORMAT", Severity: ScanSeverity.Blocking }, result.Findings);
    }

    [TestMethod]
    public void TamperedPayload_IsBlocked()
    {
        string package = BuildPackage();
        byte[] bytes = File.ReadAllBytes(package);
        bytes[VpxContainer.HeaderSize + 40] ^= 0xFF;
        File.WriteAllBytes(package, bytes);

        VpxInspection result = Inspect(package);
        Assert.Contains(f => f is { Code: "VPX_FORMAT", Severity: ScanSeverity.Blocking }, result.Findings);
    }

    [TestMethod]
    public void ExecutableInsidePackage_IsBlocked()
    {
        // 插件是托管程序集,包里带 .exe 没有正当理由,是最直接的投递面。
        string package = BuildPackage(extraFiles: new() { ["tool.exe"] = "MZ fake"u8.ToArray() });
        VpxInspection result = Inspect(package);
        Assert.Contains(f => f is { Code: "CONTENT_BLOCKED_TYPE", Severity: ScanSeverity.Blocking }, result.Findings);
    }

    [TestMethod]
    public void ScriptInsidePackage_NeedsReviewButIsNotBlocked()
    {
        // 脚本有正当用途,所以只转人工 —— 动辄拦截会让审核告警很快没人看。
        string package = BuildPackage(extraFiles: new() { ["setup.ps1"] = "Write-Host hi"u8.ToArray() });
        VpxInspection result = Inspect(package);
        Assert.Contains(f => f is { Code: "CONTENT_SCRIPT", Severity: ScanSeverity.Warning }, result.Findings);
        Assert.DoesNotContain(f => f.Severity == ScanSeverity.Blocking, result.Findings);
    }

    [TestMethod]
    public void ManifestIdMismatch_IsBlocked()
    {
        // 声明传的是 A 包里写的是 B:这正是"用别人的 id 顶替上架"的形状。
        VpxInspection result = Inspect(BuildPackage("acme.demo"), "victim.plugin");
        Assert.Contains(f => f is { Code: "MANIFEST_ID_MISMATCH", Severity: ScanSeverity.Blocking }, result.Findings);
    }

    [TestMethod]
    public void ApiLevelBeyondMarketSupport_IsBlocked()
    {
        string manifest = $$"""
            { "id": "acme.demo", "version": "1.0.0", "displayName": "Demo", "entry": "Demo.dll",
              "apiLevel": {{VpxStaticInspector.MaxApiLevel + 1}} }
            """;
        VpxInspection result = Inspect(BuildPackage(manifestOverride: manifest));
        Assert.Contains(f => f is { Code: "MANIFEST_API_LEVEL", Severity: ScanSeverity.Blocking }, result.Findings);
    }

    [TestMethod]
    public void MissingEntryAssembly_IsBlocked()
    {
        string stage = Path.Combine(_work, "no-entry");
        Directory.CreateDirectory(stage);
        File.WriteAllText(Path.Combine(stage, "plugin.json"), """
            { "id": "acme.demo", "version": "1.0.0", "displayName": "Demo", "entry": "Missing.dll" }
            """);
        string package = Path.Combine(_work, "no-entry.vpx");
        VpxContainer.Pack(stage, package);

        VpxInspection result = Inspect(package);
        Assert.Contains(f => f is { Code: "ENTRY_MISSING", Severity: ScanSeverity.Blocking }, result.Findings);
    }

    [TestMethod]
    public void BundledSharedAssembly_NeedsReview()
    {
        // 共享程序集不会被加载(装载器强制用宿主那份),带了要么是配置错,要么是想顶替宿主实现。
        string package = BuildPackage(extraFiles: new() { ["VelaShell.PluginSdk.dll"] = [1, 2, 3] });
        VpxInspection result = Inspect(package);
        Assert.Contains(f => f is { Code: "SHARED_ASSEMBLY_BUNDLED", Severity: ScanSeverity.Warning }, result.Findings);
    }

    [TestMethod]
    public void ZipBomb_IsBlocked()
    {
        // 高度可压缩的巨大文件:解压后撑爆磁盘的经典手法。
        string package = BuildPackage(extraFiles: new() { ["payload.bin"] = new byte[8 * 1024 * 1024] });
        VpxInspection result = Inspect(package);
        Assert.Contains(f => f is { Code: "ZIP_BOMB", Severity: ScanSeverity.Blocking }, result.Findings);
    }

    [TestMethod]
    [DataRow("1.0.0-beta.01", "可改成 '1.0.0-beta.1'")]
    [DataRow("1.0.0-a..b", "可改成 '1.0.0-a.b'")]
    [DataRow("2147483648.0.0", "超过了")]
    public void ManifestVersion_TheSdkRegexLetsThrough_IsBlocked(string version, string hint)
    {
        // SDK 的清单正则放行这些写法,但 SemanticVersion 解析不出:商店排不出它们的先后,在门口就拒。
        VpxInspection result = Inspect(BuildPackage(version: version), version: version);

        ScanFinding finding = result.Findings.Single(f => f.Code == "MANIFEST_INVALID");
        Assert.AreEqual(ScanSeverity.Blocking, finding.Severity);
        Assert.Contains($"version '{version}'", finding.Message, StringComparison.Ordinal);
        Assert.Contains(hint, finding.Message, StringComparison.Ordinal);
        Assert.IsNull(result.Manifest, "清单判为不合法时不该带出解析结果(发布时不会拿它写元数据)");
    }

    [TestMethod]
    public void ManifestMinSdkVersion_ThatSemanticVersionRejects_IsBlocked()
    {
        // 装载闸解析不出 minSdkVersion 时会直接放行 —— 这道闸等于没写,所以同样拒。
        string package = BuildPackage(manifestOverride: """
            { "id": "acme.demo", "version": "1.0.0", "displayName": "Demo", "entry": "Demo.dll", "minSdkVersion": "2.1.0-rc.01" }
            """);
        VpxInspection result = Inspect(package);

        Assert.Contains(f => f is { Code: "MANIFEST_INVALID", Severity: ScanSeverity.Blocking }
                             && f.Message.Contains("minSdkVersion '2.1.0-rc.01'", StringComparison.Ordinal), result.Findings);
    }

    [TestMethod]
    public void ValidPreReleaseVersion_Passes()
    {
        VpxInspection result = Inspect(BuildPackage(version: "1.0.0-beta.1"), version: "1.0.0-beta.1");

        Assert.DoesNotContain(f => f.Severity == ScanSeverity.Blocking, result.Findings);
        Assert.AreEqual("1.0.0-beta.1", result.Manifest?.Version);
    }

    [TestMethod]
    public void ComputeFileSha256_IsStable()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("velashell"));
        Assert.AreEqual(64, VpxStaticInspector.ComputeFileSha256(stream).Length);
    }
}

using System.IO.Compression;
using System.Security.Cryptography;
using VelaShell.Market.Domain;
using VelaShell.PluginSdk;
using VelaShell.PluginSdk.Manifest;
using VelaShell.PluginSdk.Packaging;

namespace VelaShell.Market.Infrastructure.Scanning;

/// <summary>静态检查的产出:发现列表 + 从包里解析出来的元数据(通过时才有值)。</summary>
/// <param name="Findings">发现列表。</param>
/// <param name="Manifest">解析出的清单(格式非法时为空)。</param>
/// <param name="Info">容器头信息(格式非法时为空)。</param>
/// <param name="EntryCount">条目数。</param>
/// <param name="UnpackedBytes">解压后总字节数。</param>
public sealed record VpxInspection(
    IReadOnlyList<ScanFinding> Findings,
    PluginManifest? Manifest,
    VpxPackageInfo? Info,
    int EntryCount,
    long UnpackedBytes);

/// <summary>
/// <c>.vpx</c> 的结构化静态检查。分四层,每层都可能直接判死:
/// <list type="number">
///   <item><b>容器</b> —— 魔数、头部 CRC、载荷长度、SHA-256、签名(复用 <see cref="VpxContainer" />,与宿主同一份实现);</item>
///   <item><b>压缩包结构</b> —— 路径逃逸、条目数、解压总量、压缩比(解压炸弹);</item>
///   <item><b>清单</b> —— 走宿主同一套 <see cref="PluginManifestReader" />,外加市场自己的 apiLevel 上限;</item>
///   <item><b>内容启发</b> —— 可执行文件、脚本、原生库、超大文件、可疑扩展名。</item>
/// </list>
/// <para>
/// 关于第 4 层要说清楚:插件本来就是可执行代码,**"包里有 dll"当然不是罪证**。
/// 这一层的定位是"值不值得人看一眼",所以它只产出 Warning 让包转人工复核,
/// 除了少数几种"插件根本不该有"的东西(如 .exe / .scr / .bat)才判 Blocking。
/// 把它调成动辄拦截,结果只会是审核形同虚设 —— 因为很快就没人看告警了。
/// </para>
/// </summary>
public static class VpxStaticInspector
{
    /// <summary>
    /// 市场当前认可的最高 apiLevel(高于它的包宿主也装不上,不如在门口就说清楚)。
    ///
    /// **直接取自 SDK,不要抄成字面量。** 这里原本硬编码成 1,SDK 从第 1 代升到第 2 代时
    /// 没人记得同步它,于是市场把所有为当代 SDK 编译的插件一律判为 Blocking 拒收 ——
    /// 而唯一的报错信号是作者上传后收到一条"市场只接受不超过 1 的插件",
    /// 没有任何构建期或启动期的提示。这个上限的语义本来就是"市场认得的最新代际",
    /// 那它就该跟着市场编译时用的那份 SDK 走,而不是靠人去记得改两处。
    ///
    /// 注意它与"某个宿主装不装得上"是两回事:后者由列表页按宿主 apiLevel 过滤
    /// (见 PluginEndpoints 的 <c>latestApiLevel</c> 过滤),老宿主自然看不到新代际的插件。
    /// </summary>
    public static int MaxApiLevel => VelaPluginApi.Level;

    private const int MaxEntries = 10_000;
    private const long MaxUnpackedBytes = 512L * 1024 * 1024;
    private const long MaxSingleEntryBytes = 128L * 1024 * 1024;

    /// <summary>压缩比上限:超过这个倍数的条目按解压炸弹处置。文本/资源能压到几十倍,上千倍就不正常了。</summary>
    private const int MaxCompressionRatio = 200;

    /// <summary>插件目录里根本不该出现的扩展名(可直接双击运行或被系统当成脚本执行的东西)。</summary>
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".scr", ".pif", ".msi", ".msp", ".cpl", ".hta", ".jar", ".lnk", ".url"
    };

    /// <summary>需要人看一眼的扩展名:合法用途存在,但也是最常见的投递载体。</summary>
    private static readonly HashSet<string> SuspiciousExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ps1", ".psm1", ".bat", ".cmd", ".sh", ".vbs", ".js", ".wsf", ".reg", ".dylib", ".so"
    };

    /// <summary>
    /// 给包内的一个条目贴标签,供审核台的「包内清单」着色。
    ///
    /// 与第 4 层内容启发**共用同一组扩展名表** —— 审核员在清单里看到的高亮,
    /// 必须和检测器实际判定的口径一致;各记一份的话,迟早出现"报告说有脚本、
    /// 清单里却没标出来"这种让人怀疑报告本身的场面。
    /// </summary>
    /// <param name="entryPath">包内相对路径。</param>
    /// <returns><c>blocked</c> / <c>suspicious</c>,都不是则为 null。</returns>
    public static string? Classify(string entryPath)
    {
        string extension = Path.GetExtension(entryPath);
        if (string.IsNullOrEmpty(extension))
        {
            return null;
        }
        if (BlockedExtensions.Contains(extension))
        {
            return "blocked";
        }
        return SuspiciousExtensions.Contains(extension) ? "suspicious" : null;
    }

    /// <summary>
    /// 跑一遍静态检查。<paramref name="package" /> 必须是可定位的流(容器读取要 Seek)。
    /// 不抛异常:任何问题都表达成 <see cref="ScanFinding" /> —— 检测器自己崩掉不该让上传者收到一句 500。
    /// </summary>
    public static VpxInspection Inspect(Stream package, string expectedPluginId, string expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(package);
        var findings = new List<ScanFinding>();

        // ---- 第 1 层:容器 ----------------------------------------------------
        string temp = Path.Combine(Path.GetTempPath(), $"vpx-scan-{Guid.NewGuid():N}.vpx");
        try
        {
            using (FileStream file = File.Create(temp))
            {
                package.CopyTo(file);
            }
            VpxPackageInfo info;
            Stream payload;
            try
            {
                payload = VpxContainer.OpenPayload(temp, out info);
            }
            catch (VpxFormatException ex)
            {
                // 容器不合法就到此为止:后面每一层都建立在"这确实是个 vpx"之上。
                findings.Add(new("VPX_FORMAT", ScanSeverity.Blocking, ex.Message));
                return new(findings, null, null, 0, 0);
            }

            using (payload)
            {
                VpxSignatureState signature = VpxContainer.VerifySignature(info);
                findings.Add(signature switch
                {
                    VpxSignatureState.Invalid => new("VPX_SIGNATURE_INVALID", ScanSeverity.Blocking,
                        "包的签名校验失败:内容在签名之后被改动过。"),
                    VpxSignatureState.Unsigned => new("VPX_UNSIGNED", ScanSeverity.Info,
                        "包未签名。市场当前允许未签名的包,但签名能让升级时的身份连续性得到保证,建议签名后再发布。"),
                    _ => new("VPX_SIGNED", ScanSeverity.Info,
                        "包带有效签名。")
                });
                if (signature is VpxSignatureState.Invalid)
                {
                    return new(findings, null, info, 0, 0);
                }

                // ---- 第 2、3、4 层:载荷 --------------------------------------
                return InspectPayload(payload, info, expectedPluginId, expectedVersion, findings);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            findings.Add(new("SCAN_IO", ScanSeverity.Blocking, $"读取包时出错:{ex.Message}"));
            return new(findings, null, null, 0, 0);
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
                // 临时文件删不掉不影响结论。
            }
        }
    }

    private static VpxInspection InspectPayload(Stream payload, VpxPackageInfo info,
        string expectedPluginId, string expectedVersion, List<ScanFinding> findings)
    {
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read);

        if (archive.Entries.Count > MaxEntries)
        {
            findings.Add(new("ZIP_TOO_MANY_ENTRIES", ScanSeverity.Blocking,
                $"包内有 {archive.Entries.Count} 个条目,超过上限 {MaxEntries}。"));
            return new(findings, null, info, archive.Entries.Count, 0);
        }

        long unpacked = 0;
        PluginManifest? manifest = null;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string name = entry.FullName.Replace('\\', '/');

            // 路径逃逸:解出来落到目标目录之外。宿主装包时也拦,但市场没有理由先把它收下。
            if (name.StartsWith('/') || name.Contains("../", StringComparison.Ordinal)
                || Path.IsPathRooted(name) || name.Split('/').Any(s => s == ".."))
            {
                findings.Add(new("ZIP_PATH_ESCAPE", ScanSeverity.Blocking,
                    "包内存在会逃出安装目录的条目路径。", name));
                continue;
            }
            if (name.EndsWith('/'))
            {
                continue; // 目录项
            }
            if (!names.Add(name))
            {
                // 同名条目在不同解包器下"最后一个赢"还是"第一个赢"并不一致,
                // 这正是"检测时看到 A、安装时落地 B"的经典把戏。
                findings.Add(new("ZIP_DUPLICATE_ENTRY", ScanSeverity.Blocking,
                    "包内存在重名条目 —— 不同解包器对它的取舍不一致,可用于绕过检测。", name));
                continue;
            }

            unpacked += entry.Length;
            if (entry.Length > MaxSingleEntryBytes)
            {
                findings.Add(new("ZIP_ENTRY_TOO_LARGE", ScanSeverity.Blocking,
                    $"单个文件解压后 {entry.Length} 字节,超过上限 {MaxSingleEntryBytes}。", name));
            }
            if (entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > MaxCompressionRatio)
            {
                findings.Add(new("ZIP_BOMB", ScanSeverity.Blocking,
                    $"压缩比 {entry.Length / entry.CompressedLength}:1 异常,疑似解压炸弹。", name));
            }

            string extension = Path.GetExtension(name);
            if (BlockedExtensions.Contains(extension))
            {
                findings.Add(new("CONTENT_BLOCKED_TYPE", ScanSeverity.Blocking,
                    $"插件包内不允许出现 {extension} 文件。", name));
            }
            else if (SuspiciousExtensions.Contains(extension))
            {
                findings.Add(new("CONTENT_SCRIPT", ScanSeverity.Warning,
                    $"包内含脚本或原生库({extension}),需要人工复核其用途。", name));
            }
            else if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) && IsNativeImage(entry))
            {
                // 托管 dll 是插件的正常形态;**原生** dll 意味着 P/Invoke 面,值得看一眼。
                findings.Add(new("CONTENT_NATIVE_LIBRARY", ScanSeverity.Warning,
                    "包内含原生库(非托管 DLL),需要人工复核。", name));
            }
        }

        if (unpacked > MaxUnpackedBytes)
        {
            findings.Add(new("ZIP_TOO_LARGE", ScanSeverity.Blocking,
                $"解压后总计 {unpacked} 字节,超过上限 {MaxUnpackedBytes}。"));
        }

        // ---- 第 3 层:清单 ---------------------------------------------------
        ZipArchiveEntry? manifestEntry = archive.GetEntry(PluginManifestReader.FileName);
        if (manifestEntry is null)
        {
            findings.Add(new("MANIFEST_MISSING", ScanSeverity.Blocking,
                $"包根目录没有 {PluginManifestReader.FileName}。"));
            return new(findings, null, info, archive.Entries.Count, unpacked);
        }
        try
        {
            using StreamReader reader = new(manifestEntry.Open());
            manifest = PluginManifestReader.Parse(reader.ReadToEnd());
        }
        catch (PluginManifestException ex)
        {
            findings.Add(new("MANIFEST_INVALID", ScanSeverity.Blocking, ex.Message, PluginManifestReader.FileName));
            return new(findings, null, info, archive.Entries.Count, unpacked);
        }

        if (!string.Equals(manifest.Id, expectedPluginId, StringComparison.Ordinal))
        {
            findings.Add(new("MANIFEST_ID_MISMATCH", ScanSeverity.Blocking,
                $"清单里的插件 id 是 '{manifest.Id}',与本次上传声明的 '{expectedPluginId}' 不符。"));
        }
        if (!string.Equals(manifest.Version, expectedVersion, StringComparison.Ordinal))
        {
            findings.Add(new("MANIFEST_VERSION_MISMATCH", ScanSeverity.Blocking,
                $"清单里的版本是 '{manifest.Version}',与本次上传声明的 '{expectedVersion}' 不符。"));
        }
        if (manifest.ApiLevel > MaxApiLevel)
        {
            findings.Add(new("MANIFEST_API_LEVEL", ScanSeverity.Blocking,
                $"清单要求 apiLevel {manifest.ApiLevel},市场当前只接受不超过 {MaxApiLevel} 的插件。"));
        }
        if (archive.GetEntry(manifest.Entry.Replace('\\', '/')) is null)
        {
            findings.Add(new("ENTRY_MISSING", ScanSeverity.Blocking,
                $"清单声明的入口程序集 '{manifest.Entry}' 不在包内。"));
        }
        // 共享程序集不该随包分发(装载器强制用宿主那一份),带了只是白占体积,也可能是想顶替宿主的实现。
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string fileName = Path.GetFileName(entry.FullName);
            if (fileName.Equals("VelaShell.PluginSdk.dll", StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase) && fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new("SHARED_ASSEMBLY_BUNDLED", ScanSeverity.Warning,
                    "包内带了本该由宿主提供的共享程序集(VelaShell.PluginSdk / Avalonia*),它不会被加载。" +
                    "请在工程里对这些引用设 ExcludeAssets=runtime。", entry.FullName));
            }
        }

        return new(findings, manifest, info, archive.Entries.Count, unpacked);
    }

    /// <summary>
    /// 粗判一个 .dll 是不是原生镜像:读 PE 头找 CLI 数据目录。
    /// 只用于产出 Warning,判错的代价是多一次人工复核而已。
    /// </summary>
    private static bool IsNativeImage(ZipArchiveEntry entry)
    {
        try
        {
            using Stream stream = entry.Open();
            using var buffer = new MemoryStream();
            // PE 头 + 数据目录都在前 1KB 内,不必读整个文件。
            byte[] chunk = new byte[1024];
            int read = stream.ReadAtLeast(chunk, chunk.Length, throwOnEndOfStream: false);
            buffer.Write(chunk, 0, read);
            byte[] bytes = buffer.ToArray();
            if (bytes.Length < 0x200 || bytes[0] != 'M' || bytes[1] != 'Z')
            {
                return false; // 不是 PE,当托管处理(判错只影响告警)
            }
            int peOffset = BitConverter.ToInt32(bytes, 0x3C);
            if (peOffset <= 0 || peOffset + 0x18 >= bytes.Length)
            {
                return false;
            }
            // 可选头的魔数:0x10B = PE32,0x20B = PE32+;CLI 头在数据目录第 15 项。
            int optional = peOffset + 24;
            ushort magic = BitConverter.ToUInt16(bytes, optional);
            int cliDirectory = optional + (magic == 0x20B ? 112 : 96) + 14 * 8;
            return cliDirectory + 4 >= bytes.Length || BitConverter.ToInt32(bytes, cliDirectory) == 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>算整包文件的 SHA-256(下载校验与去重用)。</summary>
    public static string ComputeFileSha256(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            sha.AppendData(buffer, 0, read);
        }
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }
}

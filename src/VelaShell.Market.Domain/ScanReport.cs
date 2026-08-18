namespace VelaShell.Market.Domain;

/// <summary>单条检测发现的严重度。</summary>
public enum ScanSeverity
{
    /// <summary>仅供参考(展示在版本详情里,不影响放行)。</summary>
    Info,

    /// <summary>可疑,需要人工复核 —— 包进"待审核"而不是直接发布。</summary>
    Warning,

    /// <summary>确定有害或格式非法,**直接拒收**。</summary>
    Blocking
}

/// <summary>检测流水线的总结论。</summary>
public enum ScanVerdict
{
    /// <summary>尚未开始。</summary>
    Pending,

    /// <summary>全部通过,可发布。</summary>
    Passed,

    /// <summary>有 Warning 但无 Blocking:转人工复核。</summary>
    NeedsReview,

    /// <summary>有 Blocking:拒收。</summary>
    Failed,

    /// <summary>检测本身出错(引擎不可用等)。**按拒收处理**,但原因是我们这边的问题,允许重试。</summary>
    Errored
}

/// <summary>一条检测发现。</summary>
/// <param name="Code">稳定的机器可读代码(如 <c>VPX_FORMAT</c>、<c>CLAMAV_HIT</c>),前端按它做本地化与筛选。</param>
/// <param name="Severity">严重度。</param>
/// <param name="Message">面向人的说明,直接展示给上传者。</param>
/// <param name="Path">包内相对路径(与整包相关时为空)。</param>
public sealed record ScanFinding(string Code, ScanSeverity Severity, string Message, string? Path = null);

/// <summary>
/// 一次检测的完整报告。**对上传者可见** —— 被拒了却看不到原因,只会换来一次次盲目重传。
/// </summary>
public sealed class ScanReport
{
    /// <summary>总结论。</summary>
    public ScanVerdict Verdict { get; set; } = ScanVerdict.Pending;

    /// <summary>开始时间(UTC)。</summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>结束时间(UTC)。</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>全部发现,按严重度降序。</summary>
    public List<ScanFinding> Findings { get; set; } = [];

    /// <summary>参与检测的引擎与版本(可复现性:同一个包在半年后被判不同,得知道是哪个引擎变了)。</summary>
    public Dictionary<string, string> Engines { get; set; } = [];

    /// <summary>解包后的条目数。</summary>
    public int EntryCount { get; set; }

    /// <summary>解包后的总字节数。</summary>
    public long UnpackedBytes { get; set; }

    /// <summary>失败重试次数(引擎不可用时会重排队)。</summary>
    public int Attempts { get; set; }

    /// <summary>是否存在阻断级发现。</summary>
    public bool HasBlocking => Findings.Any(f => f.Severity == ScanSeverity.Blocking);

    /// <summary>是否存在告警级发现。</summary>
    public bool HasWarning => Findings.Any(f => f.Severity == ScanSeverity.Warning);
}

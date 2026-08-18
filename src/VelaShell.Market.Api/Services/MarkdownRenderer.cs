using System.Text.RegularExpressions;
using Markdig;

namespace VelaShell.Market.Api.Services;

/// <summary>
/// Markdown 渲染。**存储永远只存原文,渲染在读取时做** —— 存 HTML 等于把一次转义失误
/// 永久固化进数据库,而且以后想换渲染器都换不动。
/// <para>
/// 安全上做两件事:关掉原始 HTML 直通(<c>DisableHtml</c>),再对产物做一遍白名单清洗。
/// 两道是有意的冗余 —— Markdig 的 <c>DisableHtml</c> 只管 Markdown 语法层面的 HTML 块,
/// 而链接里的 <c>javascript:</c> 之类要靠第二道拦。
/// </para>
/// </summary>
public sealed partial class MarkdownRenderer
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
                                                  .UseAdvancedExtensions()
                                                  .UseAutoLinks()
                                                  .DisableHtml()
                                                  .Build();

    [GeneratedRegex(@"(?i)\s(href|src)\s*=\s*(""|')\s*(javascript|vbscript|data)\s*:", RegexOptions.CultureInvariant)]
    private static partial Regex DangerousScheme();

    [GeneratedRegex(@"(?i)\son[a-z]+\s*=\s*(""[^""]*""|'[^']*')", RegexOptions.CultureInvariant)]
    private static partial Regex InlineEventHandler();

    /// <summary>把 Markdown 原文渲染成可安全嵌入页面的 HTML。</summary>
    public string ToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return "";
        }
        string html = Markdown.ToHtml(markdown, _pipeline);
        // 兜底清洗:即便上游哪天打开了 HTML 直通,这两类也进不去。
        html = DangerousScheme().Replace(html, " $1=$2#");
        html = InlineEventHandler().Replace(html, "");
        return html;
    }

    /// <summary>取纯文本摘要(列表页用,避免把整篇 Markdown 塞进列表响应)。</summary>
    public static string Excerpt(string? markdown, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return "";
        }
        string text = Markdown.ToPlainText(markdown).Replace('\n', ' ').Trim();
        return text.Length <= maxLength ? text : text[..maxLength] + "…";
    }
}

using VelaShell.Market.Api.Endpoints;
using VelaShell.Market.Api.Services;

namespace VelaShell.Market.Tests;

/// <summary>
/// Markdown 渲染的清洗。插件说明与评价正文都是**陌生人写的内容**,原样渲染就是存储型 XSS,
/// 所以这里的每条用例都是安全边界:全绿而实际行为变了,意味着市场在给访问者投毒。
/// </summary>
[TestClass]
public class MarkdownRendererTests
{
    private readonly MarkdownRenderer _renderer = new();

    [TestMethod]
    public void RawHtml_IsNotPassedThrough()
    {
        string html = _renderer.ToHtml("正常文本 <script>alert(1)</script> 后续");
        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void ImageWithOnErrorHandler_IsRenderedAsTextNotAnElement()
    {
        // 关掉 HTML 直通后,这段会被**整体转义成文本**(&lt;img …&gt;),浏览器不会把它当元素。
        // 所以断言要落在"没有生成真的 <img 元素"上 —— 断言输出里不出现 "onerror" 这几个字母
        // 是错的:转义后的文本里当然还有它,而那没有任何危害。
        string html = _renderer.ToHtml("""<img src=x onerror="alert(1)">""");
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;img", html, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    [DataRow("[点我](javascript:alert(1))")]
    [DataRow("[点我](JaVaScRiPt:alert(1))")]
    [DataRow("[点我](vbscript:msgbox(1))")]
    public void DangerousLinkScheme_IsStripped(string markdown)
    {
        string html = _renderer.ToHtml(markdown);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vbscript:", html, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void NormalMarkdown_StillRenders()
    {
        // 清洗不能把正常内容也洗没了 —— 那样作者会改用别的方式表达,反而更难管。
        string html = _renderer.ToHtml("## 标题\n\n**粗体** 与 [链接](https://example.com)\n\n```csharp\nvar x = 1;\n```");
        Assert.Contains("<h2", html);
        Assert.Contains("<strong>", html);
        Assert.Contains("https://example.com", html);
        Assert.Contains("<code", html);
    }

    [TestMethod]
    public void Excerpt_StripsMarkupAndTruncates()
    {
        string excerpt = MarkdownRenderer.Excerpt("# 标题\n\n这是一段**说明**文字。", 10);
        Assert.DoesNotContain("#", excerpt);
        Assert.DoesNotContain("**", excerpt);
        Assert.IsLessThanOrEqualTo(11, excerpt.Length, excerpt); // 10 + 省略号
    }

    [TestMethod]
    public void Excerpt_OfEmpty_IsEmpty() => Assert.AreEqual("", MarkdownRenderer.Excerpt(null));

    [TestMethod]
    public void TagList_NormalizesCaseSeparatorsAndDuplicates()
    {
        // 上传与编辑两处必须用同一套规则,否则同一个插件的标签会因为改了哪一边而不同。
        List<string> tags = TagList.Normalize("SSH, ssh;运维  数据库，Redis");
        Assert.AreSequenceEqual(["ssh", "运维", "数据库", "redis"], tags);
    }

    [TestMethod]
    public void TagList_CapsAtTen()
    {
        List<string> tags = TagList.Normalize(string.Join(',', Enumerable.Range(0, 30).Select(i => $"tag{i}")));
        Assert.HasCount(10, tags);
    }

    [TestMethod]
    public void TagList_OfEmpty_IsEmpty() => Assert.IsEmpty(TagList.Normalize(null));
}

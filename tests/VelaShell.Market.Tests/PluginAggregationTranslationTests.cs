using MongoDB.Driver;
using MongoDB.Driver.Linq;
using VelaShell.Market.Domain;

namespace VelaShell.Market.Tests;

/// <summary>
/// 钉住站点概览的累计下载与标签云这两条聚合**翻译成了什么**。
///
/// 这两处原先是手写的 BsonDocument 管道,字段名直接写成 C# 成员名(<c>$Downloads</c>、
/// <c>IsUnlisted</c>),而库里是 camelCase。Mongo 不报字段不存在:<c>$match</c> 先把全部文档
/// 筛掉,累计下载恒为 0、标签云恒为空,接口照常 200 —— 首屏那个 0 就是这么来的。
///
/// 改用 LINQ 之后字段名由驱动从映射翻译,写错就是编译错误。这里守的是剩下的两件事:
/// <list type="bullet">
///   <item><description>翻译出来的确实是**库里的元素名**(谁改了命名约定或加了 BsonElement,这里红)。</description></item>
///   <item><description>聚合确实在**服务端**做(谁把 unwind/group 挪回内存,这里红 —— 那不会出错,只会随插件数变慢)。</description></item>
/// </list>
///
/// 渲染管道不需要连库:<c>ToString()</c> 走的是驱动的翻译器,不发请求。
/// </summary>
[TestClass]
public class PluginAggregationTranslationTests
{
    private static IMongoCollection<Plugin> Plugins() =>
        new MongoClient("mongodb://127.0.0.1:1").GetDatabase("velashell-market-tests").GetCollection<Plugin>("plugins");

    /// <summary>累计下载:在未下架的插件上求 downloads 之和,由服务端算。</summary>
    [TestMethod]
    public void DownloadsSum_TranslatesToServerSideCamelCaseFields()
    {
        // SumAsync 是终结操作,渲染不出来;把它换成等价的 Select 看翻译结果 ——
        // 要验的是 Where/Sum 里那两个成员翻译成了什么,这一步足够。
        string pipeline = Plugins().AsQueryable()
                                   .Where(p => !p.IsUnlisted)
                                   .Select(p => p.Downloads)
                                   .ToString()!;

        Assert.Contains("\"isUnlisted\"", pipeline, "下架标记应当翻译成库里的 isUnlisted。");
        Assert.Contains("\"$downloads\"", pipeline, "累加的应当是库里的 downloads 字段。");
        Assert.IsFalse(pipeline.Contains("Downloads", StringComparison.Ordinal), "管道里不该出现 C# 成员名。");
    }

    /// <summary>标签云:unwind + group + sort + limit 全在服务端,不把插件拉回内存再统计。</summary>
    [TestMethod]
    public void TagCloud_TranslatesToServerSideAggregation()
    {
        string pipeline = Plugins().AsQueryable()
                                   .Where(p => !p.IsUnlisted && p.LatestVersion != null)
                                   .SelectMany(p => p.Tags)
                                   .GroupBy(tag => tag)
                                   .Select(g => new { tag = g.Key, count = g.Count() })
                                   .OrderByDescending(t => t.count)
                                   .Take(100)
                                   .ToString()!;

        Assert.Contains("\"isUnlisted\"", pipeline, "下架标记应当翻译成库里的 isUnlisted。");
        Assert.Contains("\"latestVersion\"", pipeline, "最新版本应当翻译成库里的 latestVersion。");
        Assert.Contains("\"$tags\"", pipeline, "展开的应当是库里的 tags 数组。");

        foreach (string stage in new[] { "$unwind", "$group", "$sort", "$limit" })
        {
            Assert.Contains(stage, pipeline, $"{stage} 应当下推到服务端,而不是在内存里统计。");
        }
    }
}

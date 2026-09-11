using VelaShell.Market.Api.Endpoints;
using VelaShell.Market.Domain;

namespace VelaShell.Market.Tests;

/// <summary>
/// <c>GET /api/plugins/latest</c> 的选版规则。
///
/// 这条规则有三个消费者 —— 市场、<c>vela-plugin update</c>、宿主的插件管理页,
/// 三边必须挑出同一个版本。口径一旦漂开,用户看到的就是「命令行说该升、管理页说没变」,
/// 而那种不一致没有任何办法向他解释。所以规则写成纯函数并在这里钉死。
/// </summary>
[TestClass]
public class LatestVersionSelectionTests
{
    private static PluginVersion Version(string version) => new()
    {
        PluginId = "acme.demo",
        Version = version,
        Status = PluginVersionStatus.Published,
        PayloadSha256 = "",
        FileSha256 = "",
        ObjectKey = "",
        UploadedBySubject = ""
    };

    [TestMethod]
    public void StableChannel_SkipsPreReleases()
    {
        PluginVersion? picked = PluginEndpoints.SelectLatest(
            [Version("1.0.0"), Version("1.1.0-beta.1")], includePreRelease: false);

        Assert.AreEqual("1.0.0", picked?.Version,
            "作者发了个 beta,不该把所有人的「最新版」变成那个 beta。");
    }

    [TestMethod]
    public void PreviewChannel_TakesThePreRelease()
    {
        PluginVersion? picked = PluginEndpoints.SelectLatest(
            [Version("1.0.0"), Version("1.1.0-beta.1")], includePreRelease: true);

        Assert.AreEqual("1.1.0-beta.1", picked?.Version);
    }

    [TestMethod]
    public void StableChannel_FallsBackWhenEverythingIsAPreRelease()
    {
        PluginVersion? picked = PluginEndpoints.SelectLatest(
            [Version("0.1.0-alpha.1"), Version("0.2.0-beta.1")], includePreRelease: false);

        // 与宿主自更新稳定通道同一条兜底:还没有正式版时放宽到最新的预发布,
        // 否则一个只发过 beta 的插件对稳定通道的用户永远"没有更新"。
        Assert.AreEqual("0.2.0-beta.1", picked?.Version);
    }

    [TestMethod]
    public void Ordering_IsSemVerNotLexicographic()
    {
        PluginVersion? picked = PluginEndpoints.SelectLatest(
            [Version("1.9.0"), Version("1.10.0")], includePreRelease: false);

        Assert.AreEqual("1.10.0", picked?.Version, "按字符串比,1.10.0 会排在 1.9.0 前面。");
    }

    [TestMethod]
    public void NoPublishedVersions_YieldsNothing()
    {
        Assert.IsNull(PluginEndpoints.SelectLatest([], includePreRelease: true),
            "一个已发布版本都没有的插件,不该出现在检查更新的结果里。");
    }
}

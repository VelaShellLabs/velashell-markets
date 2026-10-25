using VelaShell.Market.Infrastructure.Scanning;
using VelaShell.PluginSdk;
using VelaShell.PluginSdk.Manifest;
using VelaShell.PluginSdk.Versioning;

namespace VelaShell.Market.Tests;

/// <summary>
/// 清单版本号的收紧校验(维护者决定:与 SDK 的 <see cref="SemanticVersion" /> 一致,与 <c>vela-plugin pack</c> 同一口径)。
/// 守两件事:接受与否和 <see cref="SemanticVersion.TryParse" /> 完全一致;拒绝时说得出是哪个字段、哪一段、怎么改。
/// </summary>
[TestClass]
public class ManifestVersionRulesTests
{
    [TestMethod]
    [DataRow("1.0.0")]
    [DataRow("1.2")]
    [DataRow("1.2.3.4")]
    [DataRow("01.2.3")]
    [DataRow("1.0.0-beta.1")]
    [DataRow("1.0.0-0")]
    [DataRow("1.0.0--a")]
    [DataRow("1.0.0-beta.0a")]
    [DataRow("2147483647.0.0")]
    public void Explain_AcceptsWhatSemanticVersionAccepts(string version)
    {
        Assert.IsTrue(SemanticVersion.TryParse(version, out _), "用例本身要是 SemanticVersion 认的写法");
        Assert.IsNull(ManifestVersionRules.Explain(version));
    }

    [TestMethod]
    [DataRow("1.0.0-beta.01", "'01'", "1.0.0-beta.1")]
    [DataRow("1.0.0-01", "'01'", "1.0.0-1")]
    [DataRow("1.0.0-a..b", "空的标识符", "1.0.0-a.b")]
    [DataRow("1.0.0-.a", "空的标识符", "1.0.0-a")]
    [DataRow("1.0.0-a.", "空的标识符", "1.0.0-a")]
    [DataRow("2147483648.0.0", "'2147483648'", null)]
    [DataRow("١.٠.٠", "0-9", null)]
    [DataRow("1.0.0\n", "空白", "1.0.0")]
    public void Explain_NamesTheOffendingPartAndHowToFixIt(string version, string mentions, string? suggestion)
    {
        string? reason = ManifestVersionRules.Explain(version);

        Assert.IsNotNull(reason, $"'{version}' 必须被拒");
        Assert.Contains(mentions, reason, StringComparison.Ordinal);
        Assert.Contains(suggestion is null ? "应写成" : $"可改成 '{suggestion}'", reason, StringComparison.Ordinal);
        if (suggestion is not null)
        {
            Assert.IsNull(ManifestVersionRules.Explain(suggestion), "给出的改法自己必须合法");
        }
    }

    [TestMethod]
    public void FirstProblem_NamesTheField_AndChecksAllThree()
    {
        Assert.IsNull(ManifestVersionRules.FirstProblem("1.0.0", "1.7.0", "2.1.3"));
        Assert.IsNull(ManifestVersionRules.FirstProblem("1.0.0", null, null), "两个最低版本是可选字段");

        Assert.StartsWith("plugin.json 的 version '1.0.0-beta.01' 不合规:",
            ManifestVersionRules.FirstProblem("1.0.0-beta.01", null, null), StringComparison.Ordinal);
        Assert.StartsWith("plugin.json 的 minHostVersion '1.7.0-rc..1' 不合规:",
            ManifestVersionRules.FirstProblem("1.0.0", "1.7.0-rc..1", null), StringComparison.Ordinal);
        Assert.StartsWith("plugin.json 的 minSdkVersion '2.1.0-rc.01' 不合规:",
            ManifestVersionRules.FirstProblem("1.0.0", null, "2.1.0-rc.01"), StringComparison.Ordinal);
    }

    [TestMethod]
    public void Require_ThrowsTheSameExceptionTypeAsTheSdkReader()
    {
        // 上传端点与静态检查都只认 PluginManifestException —— 换一种异常就会漏成 500。
        PluginManifest manifest = PluginManifestReader.Parse(
            """{ "id": "acme.demo", "version": "1.0.0-a..b", "displayName": "Demo", "entry": "Demo.dll" }""");

        PluginManifestException error = Assert.ThrowsExactly<PluginManifestException>(() => ManifestVersionRules.Require(manifest));

        Assert.Contains("version '1.0.0-a..b'", error.Message, StringComparison.Ordinal);
    }

    [TestMethod]
    public void ControlCharacters_AreShownEscaped()
    {
        string? problem = ManifestVersionRules.FirstProblem("1.0.0\n", null, null);

        Assert.IsNotNull(problem);
        Assert.Contains("'1.0.0\\u000a'", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", problem, StringComparison.Ordinal);
    }
}

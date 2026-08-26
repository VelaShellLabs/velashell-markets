using VelaShell.Market.Domain;
using VelaShell.PluginSdk;

namespace VelaShell.Market.Infrastructure.Scanning;

/// <summary>
/// 把 SDK 的清单模型抄成市场自己的存储形状。
///
/// 上传落库、检测通过后回填、发布页预检三处都要这一份映射,放在这里而不是各写一遍 ——
/// 三份实现迟早会因为漏抄一个字段而对不上,而"详情页显示的东西和实际装的不一致"
/// 恰恰是这个市场最不能出的那类问题。
/// </summary>
public static class ManifestProjection
{
    /// <summary>抄出声明式贡献点。清单里没有 <c>contributes</c> 时返回一个三项皆空的实例。</summary>
    public static PluginContributionSummary ToContributions(PluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        PluginContributes? c = manifest.Contributes;
        return new()
        {
            Commands =
            [
                .. (c?.Commands ?? []).Select(x => new ContributedCommand(x.Id, x.Title, string.IsNullOrWhiteSpace(x.Category) ? null : x.Category))
            ],
            Protocols = [.. (c?.Protocols ?? []).Select(x => new ContributedConnection(x.Id, x.DisplayName, x.DefaultPort))],
            Workspaces = [.. (c?.Workspaces ?? []).Select(x => new ContributedConnection(x.Id, x.DisplayName, x.DefaultPort))]
        };
    }

    /// <summary>激活事件,归一成非空列表。</summary>
    public static List<string> ToActivationEvents(PluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return [.. manifest.ActivationEvents ?? []];
    }
}

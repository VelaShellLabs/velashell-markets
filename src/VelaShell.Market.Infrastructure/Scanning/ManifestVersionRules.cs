using System.Globalization;
using VelaShell.PluginSdk;
using VelaShell.PluginSdk.Manifest;
using VelaShell.PluginSdk.Versioning;

namespace VelaShell.Market.Infrastructure.Scanning;

/// <summary>
/// 清单三个版本字段(<c>version</c> / <c>minHostVersion</c> / <c>minSdkVersion</c>)的收紧校验:
/// 在 SDK <c>PluginManifestReader</c> 的正则之上,再要求 <see cref="SemanticVersion.TryParse" /> 能解析。
/// <para>
/// 为什么要收紧:SDK 清单正则 <c>^\d+(\.\d+){1,3}(-[0-9A-Za-z.-]+)?$</c> 比 <see cref="SemanticVersion" /> 宽 ——
/// <c>1.0.0-beta.01</c>(预发布里的数字标识符带前导零)、<c>1.0.0-a..b</c>(空标识符)、超出 int 的数字段、
/// 非 ASCII 数字都能过正则。商店按 <see cref="SemanticVersion.TextComparer" /> 排版本,这种版本解析不出,
/// 只能永远排在最旧;宿主与 <c>vela-plugin update</c> 也比不出它与别的版本谁新。
/// 正则的 <c>$</c> 还放行结尾一个换行(<c>"1.0.0\n"</c>),那会原样进对象键与日志,这里一并拒掉。
/// </para>
/// <para>
/// 接受与否**只看 <see cref="SemanticVersion.TryParse" />**(加上不许首尾空白),本类自己写的只是拒绝理由。
/// 与 <c>vela-plugin pack</c> / <c>validate</c> 同一口径。等 SDK 的清单读取器自己收紧之后,这个类可以删掉。
/// </para>
/// </summary>
public static class ManifestVersionRules
{
    /// <summary>
    /// 校验已经过 SDK 清单读取器的清单;不合格抛 <see cref="PluginManifestException" />。
    /// 上传、预检与静态检查都把它当作清单不合法处理(同一条报错文案)。
    /// </summary>
    public static void Require(PluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (FirstProblem(manifest.Version, manifest.MinHostVersion, manifest.MinSdkVersion) is { } problem)
        {
            throw new PluginManifestException(problem);
        }
    }

    /// <summary>
    /// 三个版本字段里第一个不合格的,给出完整的一句拒绝理由;都合格时为 <see langword="null" />。
    /// 审核台放行已入库的版本时也用它(那时手里只有入库的字段,没有清单对象)。
    /// </summary>
    public static string? FirstProblem(string? version, string? minHostVersion, string? minSdkVersion)
    {
        if (Explain(version) is { } reason)
        {
            return Sentence("version", version, reason);
        }
        if (minHostVersion is not null && Explain(minHostVersion) is { } hostReason)
        {
            return Sentence("minHostVersion", minHostVersion, hostReason);
        }
        if (minSdkVersion is not null && Explain(minSdkVersion) is { } sdkReason)
        {
            return Sentence("minSdkVersion", minSdkVersion, sdkReason);
        }
        return null;
    }

    /// <summary>这个版本字符串为什么不合格(不带字段名,末尾附改法);合格时为 <see langword="null" />。</summary>
    public static string? Explain(string? value)
    {
        if (value is null)
        {
            return "缺少版本号。" + Example(null);
        }
        if (value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])))
        {
            string trimmed = value.Trim();
            return "首尾带了空白(结尾的换行也算)。" + Example(Explain(trimmed) is null ? trimmed : null);
        }
        if (SemanticVersion.TryParse(value, out _))
        {
            return null;
        }
        int dash = value.IndexOf('-', StringComparison.Ordinal);
        string core = dash < 0 ? value : value[..dash];
        string? preRelease = dash < 0 ? null : value[(dash + 1)..];
        foreach (string part in core.Split('.'))
        {
            if (part.Length > 0 && part.Any(char.IsDigit) && !part.All(char.IsAsciiDigit))
            {
                return $"数字段 '{part}' 用了 0-9 以外的数字字符。" + Example(null);
            }
            if (part.Length > 0 && part.All(char.IsAsciiDigit)
                && !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                return $"数字段 '{part}' 超过了 {int.MaxValue}。" + Example(null);
            }
        }
        if (preRelease is not null)
        {
            string[] identifiers = preRelease.Split('.');
            if (identifiers.Any(id => id.Length == 0))
            {
                return $"预发布后缀 '{preRelease}' 里有空的标识符(连着两个点,或以点开头、结尾)。"
                       + Example(Repair(core, identifiers));
            }
            if (identifiers.FirstOrDefault(HasLeadingZero) is { } zeroPadded)
            {
                return $"预发布标识符 '{zeroPadded}' 是带前导零的数字,SemVer 2.0 不允许(它和 '{TrimZeros(zeroPadded)}' 分不出先后)。"
                       + Example(Repair(core, identifiers));
            }
        }
        return "不是合法的版本号。" + Example(null);
    }

    private static string Sentence(string field, string? value, string reason) =>
        $"plugin.json 的 {field} '{Printable(value)}' 不合规:{reason}";

    private static bool HasLeadingZero(string identifier) =>
        identifier.Length > 1 && identifier[0] == '0' && identifier.All(char.IsAsciiDigit);

    private static string TrimZeros(string identifier) => identifier.TrimStart('0') is { Length: > 0 } trimmed ? trimmed : "0";

    /// <summary>去掉空标识符、去掉数字标识符的前导零;修出来的仍不合法就不给建议。</summary>
    private static string? Repair(string core, IEnumerable<string> identifiers)
    {
        string[] kept = [.. identifiers.Where(id => id.Length > 0).Select(id => HasLeadingZero(id) ? TrimZeros(id) : id)];
        string candidate = kept.Length == 0 ? core : $"{core}-{string.Join('.', kept)}";
        return SemanticVersion.TryParse(candidate, out _) ? candidate : null;
    }

    private static string Example(string? suggestion) =>
        suggestion is not null
            ? $"可改成 '{suggestion}'。"
            : "应写成 '1.2.0' 或 '1.2.0-beta.1' 这样。";

    /// <summary>换行等控制字符写成转义:报错是给人看的,看不见的字符正是要指出来的那个问题。</summary>
    private static string Printable(string? value) =>
        value is null
            ? "(空)"
            : value.Any(char.IsControl)
                ? string.Concat(value.Select(c => char.IsControl(c) ? $"\\u{(int)c:x4}" : c.ToString()))
                : value;
}

using System.Security.Claims;
using System.Text.RegularExpressions;
using VelaShell.Market.Domain;

namespace VelaShell.Market.Api.Services;

/// <summary>
/// GitHub Actions OIDC 令牌里与可信发布相关的声明。
/// 字段含义见 GitHub 文档「About security hardening with OpenID Connect」。
/// </summary>
/// <param name="Repository"><c>repository</c>:<c>owner/repo</c>。</param>
/// <param name="RepositoryId"><c>repository_id</c>。</param>
/// <param name="RepositoryOwnerId"><c>repository_owner_id</c>。</param>
/// <param name="JobWorkflowRef"><c>job_workflow_ref</c>:<c>owner/repo/.github/workflows/x.yml@refs/...</c>。</param>
/// <param name="Environment"><c>environment</c>,没用部署环境时为空。</param>
/// <param name="Ref"><c>ref</c>,如 <c>refs/tags/v1.2.0</c>。</param>
/// <param name="RunId"><c>run_id</c>。</param>
public sealed record GitHubOidcClaims(
    string Repository,
    long RepositoryId,
    long RepositoryOwnerId,
    string JobWorkflowRef,
    string? Environment,
    string? Ref,
    string? RunId)
{
    /// <summary>从已通过签名校验的主体里取声明。缺任何一个必需声明都返回 null(当成令牌不合格)。</summary>
    public static GitHubOidcClaims? From(ClaimsPrincipal user)
    {
        string? repository = user.FindFirstValue("repository");
        string? jobWorkflowRef = user.FindFirstValue("job_workflow_ref");
        if (string.IsNullOrEmpty(repository)
            || string.IsNullOrEmpty(jobWorkflowRef)
            || !long.TryParse(user.FindFirstValue("repository_id"), out long repositoryId)
            || !long.TryParse(user.FindFirstValue("repository_owner_id"), out long ownerId))
        {
            return null;
        }
        return new(repository, repositoryId, ownerId, jobWorkflowRef,
            NullIfEmpty(user.FindFirstValue("environment")),
            NullIfEmpty(user.FindFirstValue("ref")),
            NullIfEmpty(user.FindFirstValue("run_id")));
    }

    /// <summary>一行能放进日志与审计字段的来源描述。</summary>
    public string Describe() => RunId is null ? $"github:{JobWorkflowRef}" : $"github:{JobWorkflowRef} run {RunId}";

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}

/// <summary>可信发布的纯函数部分:输入校验与「这枚令牌对得上哪条登记」。</summary>
public static partial class TrustedPublishing
{
    private const string WorkflowsDir = "/.github/workflows/";

    // GitHub 用户名 / 组织名:字母数字与连字符,≤39;仓库名:字母数字与 . _ -,≤100。
    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex RepositoryPattern();

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,100}\.ya?ml$")]
    private static partial Regex WorkflowPattern();

    /// <summary>校验仓库写法。通过返回 null,否则返回给人看的原因。</summary>
    public static string? ExplainRepository(string? repository) =>
        string.IsNullOrWhiteSpace(repository) ? "请填写仓库,格式 owner/repo。"
        : RepositoryPattern().IsMatch(repository.Trim()) ? null
        : $"“{repository}” 不是 owner/repo 格式的 GitHub 仓库名。";

    /// <summary>
    /// 校验工作流文件名。只收文件名本身:写成 <c>.github/workflows/release.yml</c> 的,
    /// 先由 <see cref="NormalizeWorkflow" /> 剥掉目录再来。
    /// </summary>
    public static string? ExplainWorkflow(string? workflow) =>
        string.IsNullOrWhiteSpace(workflow) ? "请填写工作流文件名,如 release.yml。"
        : WorkflowPattern().IsMatch(workflow) ? null
        : $"“{workflow}” 不是工作流文件名(应形如 release.yml,位于 .github/workflows/ 下)。";

    /// <summary>校验部署环境名(可空)。</summary>
    public static string? ExplainEnvironment(string? environment) =>
        environment is null ? null
        : environment.Length > 255 ? "环境名最多 255 个字符。"
        : environment.Any(char.IsControl) ? "环境名不能含控制字符。"
        : null;

    /// <summary>把用户可能粘进来的 <c>.github/workflows/release.yml</c> 归一成 <c>release.yml</c>。</summary>
    public static string NormalizeWorkflow(string workflow)
    {
        string trimmed = workflow.Trim().Replace('\\', '/');
        int slash = trimmed.LastIndexOf('/');
        return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
    }

    /// <summary>
    /// 从 <c>job_workflow_ref</c> 拆出「工作流所在仓库」与「工作流文件名」。
    /// 形如 <c>octo/repo/.github/workflows/release.yml@refs/tags/v1</c>;拆不出时返回 false。
    /// </summary>
    public static bool TryParseWorkflowRef(string jobWorkflowRef, out string repository, out string workflow)
    {
        repository = workflow = "";
        int at = jobWorkflowRef.IndexOf('@');
        string path = at >= 0 ? jobWorkflowRef[..at] : jobWorkflowRef;
        int dir = path.IndexOf(WorkflowsDir, StringComparison.Ordinal);
        if (dir <= 0)
        {
            return false;
        }
        repository = path[..dir];
        workflow = path[(dir + WorkflowsDir.Length)..];
        // 工作流只能直接在 workflows 目录下,不接受子目录(GitHub 本身也不认子目录里的工作流)。
        return workflow.Length > 0 && !workflow.Contains('/');
    }

    /// <summary>
    /// 在插件登记的可信发布者里找一条与令牌对得上的。全部条件都要满足:
    /// <list type="bullet">
    ///   <item>仓库与所有者的**数字 id** 一致(改名、转移、删了重建都过不去);</item>
    ///   <item>令牌来自该仓库**自己的**工作流文件,文件名一致 —— 别的仓库的可复用工作流不算;</item>
    ///   <item>登记了环境的,令牌必须来自该环境(GitHub 的环境名不分大小写)。</item>
    /// </list>
    /// </summary>
    public static TrustedPublisher? Match(IEnumerable<TrustedPublisher> publishers, GitHubOidcClaims claims)
    {
        if (!TryParseWorkflowRef(claims.JobWorkflowRef, out string workflowRepository, out string workflow)
            || !string.Equals(workflowRepository, claims.Repository, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return publishers.FirstOrDefault(p =>
            p.Provider == "github"
            && p.RepositoryId == claims.RepositoryId
            && p.RepositoryOwnerId == claims.RepositoryOwnerId
            // 工作流文件名在 GitHub 上区分大小写。
            && string.Equals(p.Workflow, workflow, StringComparison.Ordinal)
            && (p.Environment is null || string.Equals(p.Environment, claims.Environment, StringComparison.OrdinalIgnoreCase)));
    }
}

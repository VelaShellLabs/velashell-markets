using System.Security.Claims;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using VelaShell.Market.Api.Services;
using VelaShell.Market.Domain;

namespace VelaShell.Market.Tests;

/// <summary>
/// 可信发布的匹配规则。这是「哪个 CI 能替谁发版」的唯一判断点,每条放宽都等于多开一扇门,
/// 所以对得上的情形与每一种对不上的情形都各钉一条。
/// </summary>
[TestClass]
public class TrustedPublishingTests
{
    private const long RepoId = 123456;
    private const long OwnerId = 777;

    private static TrustedPublisher Publisher(string workflow = "release.yml", string? environment = null) =>
        new()
        {
            PublisherId = "p1",
            Repository = "VelaShellLabs/velashell-plugins",
            RepositoryId = RepoId,
            RepositoryOwnerId = OwnerId,
            Workflow = workflow,
            Environment = environment
        };

    private static GitHubOidcClaims Claims(
        string repository = "VelaShellLabs/velashell-plugins",
        long repositoryId = RepoId,
        long ownerId = OwnerId,
        string? jobWorkflowRef = null,
        string? environment = null) =>
        new(repository, repositoryId, ownerId,
            jobWorkflowRef ?? $"{repository}/.github/workflows/release.yml@refs/tags/v1.4.0",
            environment, "refs/tags/v1.4.0", "42");

    [TestMethod]
    public void Match_AcceptsTheRegisteredRepositoryAndWorkflow()
    {
        Assert.AreEqual("p1", TrustedPublishing.Match([Publisher()], Claims())?.PublisherId);
    }

    [TestMethod]
    public void Match_IgnoresRepositoryNameCase_ButUsesIdsAsTheIdentity()
    {
        // 名字大小写不同(GitHub 不区分),id 一致 → 通过。
        Assert.IsNotNull(TrustedPublishing.Match([Publisher()], Claims(repository: "velashelllabs/VelaShell-Plugins")));
    }

    [TestMethod]
    public void Match_RejectsSameNameWithDifferentRepositoryId()
    {
        // 仓库被删后有人用同名重建:名字对得上,id 对不上。
        Assert.IsNull(TrustedPublishing.Match([Publisher()], Claims(repositoryId: RepoId + 1)));
    }

    [TestMethod]
    public void Match_RejectsDifferentOwnerId()
    {
        Assert.IsNull(TrustedPublishing.Match([Publisher()], Claims(ownerId: OwnerId + 1)));
    }

    [TestMethod]
    public void Match_RejectsAnotherWorkflowInTheSameRepository()
    {
        Assert.IsNull(TrustedPublishing.Match([Publisher()],
            Claims(jobWorkflowRef: "VelaShellLabs/velashell-plugins/.github/workflows/ci.yml@refs/heads/dev")));
    }

    [TestMethod]
    public void Match_WorkflowFileNameIsCaseSensitive()
    {
        Assert.IsNull(TrustedPublishing.Match([Publisher()],
            Claims(jobWorkflowRef: "VelaShellLabs/velashell-plugins/.github/workflows/Release.yml@refs/tags/v1")));
    }

    [TestMethod]
    public void Match_RejectsReusableWorkflowFromAnotherRepository()
    {
        // 调用了别的仓库里同名的可复用工作流:job_workflow_ref 指向的是那个仓库。
        Assert.IsNull(TrustedPublishing.Match([Publisher()],
            Claims(jobWorkflowRef: "attacker/tools/.github/workflows/release.yml@refs/heads/main")));
    }

    [TestMethod]
    public void Match_RequiresTheRegisteredEnvironment_CaseInsensitively()
    {
        TrustedPublisher withEnv = Publisher(environment: "market");
        Assert.IsNull(TrustedPublishing.Match([withEnv], Claims()), "登记了环境,令牌却不来自任何环境");
        Assert.IsNull(TrustedPublishing.Match([withEnv], Claims(environment: "staging")));
        Assert.IsNotNull(TrustedPublishing.Match([withEnv], Claims(environment: "Market")));
    }

    [TestMethod]
    public void Match_PublisherWithoutEnvironment_AcceptsAnyEnvironment()
    {
        Assert.IsNotNull(TrustedPublishing.Match([Publisher()], Claims(environment: "market")));
    }

    [TestMethod]
    public void Match_ReturnsNullForEmptyList()
    {
        Assert.IsNull(TrustedPublishing.Match([], Claims()));
    }

    [TestMethod]
    [DataRow("o/r/.github/workflows/release.yml@refs/tags/v1", "o/r", "release.yml")]
    [DataRow("o/r/.github/workflows/release.yaml", "o/r", "release.yaml")]
    public void TryParseWorkflowRef_SplitsRepositoryAndFile(string value, string repository, string workflow)
    {
        Assert.IsTrue(TrustedPublishing.TryParseWorkflowRef(value, out string r, out string w));
        Assert.AreEqual(repository, r);
        Assert.AreEqual(workflow, w);
    }

    [TestMethod]
    [DataRow("o/r/release.yml@refs/tags/v1")]
    [DataRow("/.github/workflows/release.yml@x")]
    [DataRow("o/r/.github/workflows/sub/release.yml@x")]
    [DataRow("o/r/.github/workflows/@x")]
    public void TryParseWorkflowRef_RejectsMalformedRefs(string value)
    {
        Assert.IsFalse(TrustedPublishing.TryParseWorkflowRef(value, out _, out _));
    }

    [TestMethod]
    [DataRow("VelaShellLabs/velashell-plugins", true)]
    [DataRow("a/b.c_d-e", true)]
    [DataRow("velashell-plugins", false)]
    [DataRow("-bad/repo", false)]
    [DataRow("owner/repo/extra", false)]
    [DataRow("https://github.com/owner/repo", false)]
    [DataRow("", false)]
    public void ExplainRepository(string value, bool valid)
    {
        Assert.AreEqual(valid, TrustedPublishing.ExplainRepository(value) is null);
    }

    [TestMethod]
    [DataRow(".github/workflows/release.yml", "release.yml")]
    [DataRow(" release.yaml ", "release.yaml")]
    [DataRow(@".github\workflows\publish.yml", "publish.yml")]
    public void NormalizeWorkflow_StripsDirectory(string value, string expected)
    {
        string normalized = TrustedPublishing.NormalizeWorkflow(value);
        Assert.AreEqual(expected, normalized);
        Assert.IsNull(TrustedPublishing.ExplainWorkflow(normalized));
    }

    [TestMethod]
    [DataRow("release")]
    [DataRow("release.json")]
    [DataRow("")]
    public void ExplainWorkflow_RejectsNonWorkflowNames(string value)
    {
        Assert.IsNotNull(TrustedPublishing.ExplainWorkflow(value));
    }

    [TestMethod]
    public void Claims_FromPrincipal_RequiresAllIdentityClaims()
    {
        ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
            new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));

        (string, string)[] full =
        [
            ("repository", "o/r"), ("repository_id", "1"), ("repository_owner_id", "2"),
            ("job_workflow_ref", "o/r/.github/workflows/release.yml@refs/tags/v1"), ("run_id", "9")
        ];
        GitHubOidcClaims? parsed = GitHubOidcClaims.From(Principal(full));
        Assert.IsNotNull(parsed);
        Assert.AreEqual(1, parsed.RepositoryId);
        Assert.IsNull(parsed.Environment);
        Assert.AreEqual("github:o/r/.github/workflows/release.yml@refs/tags/v1 run 9", parsed.Describe());

        foreach ((string type, _) in full.Where(c => c.Item1 != "run_id"))
        {
            Assert.IsNull(GitHubOidcClaims.From(Principal(full.Where(c => c.Item1 != type).ToArray())), $"缺 {type} 时应判为不合格");
        }
        Assert.IsNull(GitHubOidcClaims.From(Principal([.. full.Where(c => c.Item1 != "repository_id"), ("repository_id", "abc")])));
    }

    /// <summary>
    /// 登记条目的成员一旦被改回 <c>Id</c>,EasilyNET 的约定会把 32 位 hex 的 GUID 拿去当 ObjectId 解析,
    /// 登记接口写库即 500。这里在生产约定下做一次往返。
    /// </summary>
    [TestMethod]
    public void Plugin_WithTrustedPublishers_RoundTripsUnderProductionConventions()
    {
        var plugin = new Plugin
        {
            Id = "velashell.redis",
            OwnerSubject = "sub-1",
            DisplayName = "Redis",
            TrustedPublishers = [WithGuidId(Publisher(environment: "market"))]
        };

        BsonDocument document = plugin.ToBsonDocument();
        BsonDocument stored = document["trustedPublishers"].AsBsonArray[0].AsBsonDocument;
        Assert.AreEqual(BsonType.String, stored["id"].BsonType, "登记条目的 id 必须按字符串存");
        Assert.AreEqual(RepoId, stored["repositoryId"].AsInt64);

        Plugin back = BsonSerializer.Deserialize<Plugin>(document);
        Assert.AreEqual(plugin.TrustedPublishers[0].PublisherId, back.TrustedPublishers[0].PublisherId);
        Assert.AreEqual("market", back.TrustedPublishers[0].Environment);

        static TrustedPublisher WithGuidId(TrustedPublisher publisher)
        {
            publisher.PublisherId = Guid.NewGuid().ToString("N");
            return publisher;
        }
    }

    /// <summary>
    /// 登记上限的过滤条件里有一处手写的字段路径(数组下标),它取自类映射的元素名。
    /// 这里确认那个元素名就是库里实际的字段名,而不是 C# 成员名。
    /// </summary>
    [TestMethod]
    public void TrustedPublishers_ElementName_MatchesStoredFieldName()
    {
        var plugin = new Plugin { Id = "a.b", OwnerSubject = "s", DisplayName = "d" };
        string element = BsonClassMap.LookupClassMap(typeof(Plugin)).GetMemberMap(nameof(Plugin.TrustedPublishers)).ElementName;
        Assert.IsTrue(plugin.ToBsonDocument().Contains(element), $"库里没有字段 {element}");
    }

    /// <summary>旧文档没有这个字段:读出来应是空列表,而不是 null(匹配与界面都直接枚举它)。</summary>
    [TestMethod]
    public void Plugin_WithoutTrustedPublishersField_DeserializesToEmptyList()
    {
        BsonDocument document = new Plugin { Id = "a.b", OwnerSubject = "s", DisplayName = "d" }.ToBsonDocument();
        document.Remove("trustedPublishers");
        Assert.AreEqual(0, BsonSerializer.Deserialize<Plugin>(document).TrustedPublishers.Count);
    }
}

using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using VelaShell.Market.Domain;
using VelaShell.Market.Infrastructure.Persistence;

namespace VelaShell.Market.Tests;

/// <summary>
/// 让整个测试程序集跑在**和生产一致的 BSON 序列化配置**下。
///
/// 这一步不是可有可无的仪式。EasilyNET 的约定是在 <c>AddMongoContext</c> 里注册到全局
/// <c>ConventionRegistry</c> 的,不调它,测试进程用的就是驱动的裸默认值 —— 于是
/// "测试全绿、生产照样 500" 完全可能同时成立,这个 bug 第一次就是这么漏过去的。
///
/// <c>AddMongoContext</c> 只往 <see cref="IServiceCollection" /> 里登记服务并注册约定,
/// 不会真的去连数据库(连接字符串指向一个不存在的端口也无所谓),所以单测里调它是安全的。
/// 约定是全局且一次性的,必须赶在任何 <see cref="BsonClassMap" /> 被解析、冻结之前完成,
/// 因此放在 <c>AssemblyInitialize</c> 而不是某个类的初始化里。
/// </summary>
[TestClass]
public static class SerializationConventionsFixture
{
    /// <summary>在任何测试跑起来之前,按生产的方式注册一次全局约定。</summary>
    /// <param name="context">MSTest 传入的上下文,这里用不到。</param>
    [AssemblyInitialize]
    public static void Initialize(TestContext context)
    {
        _ = context;
        new ServiceCollection().AddMongoContext<MarketDbContext>("mongodb://127.0.0.1:1/velashell-market-tests");
    }
}

/// <summary>
/// 贡献点内嵌文档的 BSON 映射。
///
/// 这里守的是一条很容易被无声破坏的边界:EasilyNET 的
/// <c>StringToObjectIdIdGeneratorConvention</c> 会递归走进每一层子对象,把凡是叫
/// <c>Id</c> / <c>ID</c> 的 string 成员按 ObjectId 表示存储 —— 于是
/// <c>velashell.redis.discover</c> 这种命令 id 会在写库那一刻被拿去 <c>ObjectId.Parse</c>,
/// 整个上传请求 500。症状出现在上传接口,病因却在领域模型的成员名上,
/// 所以断言直接压在序列化结果上。
///
/// 谁要是把 <c>CommandId</c> / <c>ConnectionId</c> 改回 <c>Id</c>,这里必须红。
/// </summary>
[TestClass]
public class PluginContributionMappingTests
{
    private static PluginVersion NewVersion() =>
        new()
        {
            Id = ObjectId.GenerateNewId(),
            PluginId = "velashell.redis",
            Version = "2.0.0",
            PayloadSha256 = new string('a', 64),
            FileSha256 = new string('b', 64),
            ObjectKey = "velashell.redis/2.0.0.vpx",
            UploadedBySubject = "sub-1",
            Contributes = new()
            {
                Commands = [new("velashell.redis.discover", "发现 Redis 实例", "Redis")],
                Protocols = [new("redis", "Redis", 6379)],
                Workspaces = [new("redis-workbench", "Redis 工作台", 6379)]
            }
        };

    [TestMethod]
    public void CommandIdWithDots_SerializesInsteadOfBeingParsedAsObjectId()
    {
        BsonDocument doc = NewVersion().ToBsonDocument();

        Assert.AreEqual("velashell.redis.discover", doc["contributes"]["commands"][0]["id"].AsString);
    }

    [TestMethod]
    public void ConnectionIds_AreOrdinaryStringFields()
    {
        BsonDocument doc = NewVersion().ToBsonDocument();

        Assert.AreEqual("redis", doc["contributes"]["protocols"][0]["id"].AsString);
        Assert.AreEqual("redis-workbench", doc["contributes"]["workspaces"][0]["id"].AsString);
    }

    /// <summary>
    /// 改名只能改在 C# 这一侧:库里与 API 上的字段名都必须还是 <c>id</c>。
    /// 前端 ContributesBox 读的就是 <c>command.id</c>,元素名飘了它整块渲染不出来。
    /// </summary>
    [TestMethod]
    public void RenamedMembers_KeepTheirWireNameAsId()
    {
        foreach (Type type in (Type[])[typeof(ContributedCommand), typeof(ContributedConnection)])
        {
            BsonClassMap map = BsonClassMap.LookupClassMap(type);
            Assert.IsNull(map.IdMemberMap, $"{type.Name} 不该有主键成员。");
            Assert.ContainsSingle(map.AllMemberMaps.Where(m => m.ElementName == "id"),
                $"{type.Name} 应当恰好有一个元素名为 id 的成员。");
        }
    }

    [TestMethod]
    public void RoundTrip_PreservesEveryContribution()
    {
        PluginVersion back = BsonSerializer.Deserialize<PluginVersion>(NewVersion().ToBsonDocument());

        Assert.IsNotNull(back.Contributes);
        Assert.AreEqual("velashell.redis.discover", back.Contributes.Commands[0].CommandId);
        Assert.AreEqual("发现 Redis 实例", back.Contributes.Commands[0].Title);
        Assert.AreEqual("Redis", back.Contributes.Commands[0].Category);
        Assert.AreEqual("redis", back.Contributes.Protocols[0].ConnectionId);
        Assert.AreEqual(6379, back.Contributes.Workspaces[0].DefaultPort);
    }

    /// <summary>
    /// 顺带钉住整份文档的形状。EasilyNET 的内置默认约定(camelCase + 枚举存字符串)
    /// 一旦被 <c>ConfigureMongoConventions</c> 之类的改动顶掉,库里既有文档会集体读不出来,
    /// 而那种事故在代码评审里几乎看不出来 —— 让它在这里就炸。
    /// </summary>
    [TestMethod]
    public void DocumentShape_StaysCamelCaseWithStringEnums()
    {
        BsonDocument doc = NewVersion().ToBsonDocument();

        Assert.IsTrue(doc.Contains("pluginId"), "字段名应当是 camelCase。");
        Assert.AreEqual("Quarantined", doc["status"].AsString, "枚举应当存成字符串而不是序号。");
    }
}

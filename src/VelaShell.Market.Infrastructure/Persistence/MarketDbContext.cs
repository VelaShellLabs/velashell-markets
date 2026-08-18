using EasilyNET.Mongo.Core;
using MongoDB.Driver;
using VelaShell.Market.Domain;

namespace VelaShell.Market.Infrastructure.Persistence;

/// <summary>
/// 市场业务库。沿用 EasilyNET 的 <see cref="MongoContext" />(与 EasilyNET 生态一致的注册方式与序列化约定)。
/// </summary>
public sealed class MarketDbContext : MongoContext
{
    /// <summary>插件条目,主键即插件 id。</summary>
    public IMongoCollection<Plugin> Plugins => GetCollection<Plugin>("plugins");

    /// <summary>插件版本。</summary>
    public IMongoCollection<PluginVersion> Versions => GetCollection<PluginVersion>("plugin.versions");

    /// <summary>评价。</summary>
    public IMongoCollection<Review> Reviews => GetCollection<Review>("plugin.reviews");
}

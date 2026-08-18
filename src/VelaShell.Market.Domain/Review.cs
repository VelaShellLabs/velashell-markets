using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace VelaShell.Market.Domain;

/// <summary>
/// 一条评价。**每人每插件至多一条**(唯一索引 <c>pluginId + subject</c>)——
/// 允许重复评价等于把评分变成刷分游戏,而且均值也就没意义了。改评价是更新同一条。
/// </summary>
public sealed class Review
{
    /// <summary>主键。</summary>
    [BsonId]
    public ObjectId Id { get; set; }

    /// <summary>被评价的插件 id。</summary>
    public required string PluginId { get; set; }

    /// <summary>评价者身份主体。</summary>
    public required string Subject { get; set; }

    /// <summary>评价者展示名(快照:改名不该让历史评价的署名跟着变)。</summary>
    public string? DisplayName { get; set; }

    /// <summary>评分,1–5。</summary>
    public int Rating { get; set; }

    /// <summary>评价正文,Markdown 原文(可空:只打分不写字是常态)。</summary>
    public string? BodyMarkdown { get; set; }

    /// <summary>评价时所用的插件版本(有助于读者判断评价是否过时)。</summary>
    public string? PluginVersion { get; set; }

    /// <summary>创建时间(UTC)。</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>最后修改时间(UTC)。</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>是否被管理员隐藏(违规内容),隐藏后不计入均值。</summary>
    public bool IsHidden { get; set; }
}

import { Chip, PluginIcon } from '@/components';
import { DownloadOutlined, SafetyCertificateOutlined, StarFilled } from '@ant-design/icons';
import { history } from '@umijs/max';
import { Tooltip } from 'antd';

/** 浏览页的插件卡片:图标 + 名称/版本/作者、两行简介、标签、底部评分与下载量、签名结论。 */
export default function PluginCard({ plugin }: { plugin: MarketAPI.PluginSummary }) {
  const author = plugin.author ?? plugin.id;
  const rated = plugin.ratingCount > 0;
  const trusted = plugin.latestSignature === 'Trusted';

  return (
    <article className="plugin-card" onClick={() => history.push(`/plugins/${plugin.id}`)}>
      <div className="plugin-card-head">
        <PluginIcon id={plugin.id} name={plugin.displayName} size={44} />
        <div className="plugin-card-headtext">
          {/* title 兜住被省略号截掉的长名字 —— 插件名经常比卡片宽。 */}
          <span className="plugin-card-name" title={plugin.displayName}>
            {plugin.displayName}
          </span>
          <div className="plugin-card-sub">
            {plugin.latestVersion ? <span className="plugin-card-version">v{plugin.latestVersion}</span> : null}
            <span className="plugin-card-author" title={author}>
              {author}
            </span>
          </div>
        </div>
      </div>

      <p className="plugin-card-summary">{plugin.summary || plugin.excerpt || '作者未填写简介'}</p>

      <div className="plugin-card-tags">
        {plugin.tags?.slice(0, 3).map((item) => (
          <Chip key={item}>{item}</Chip>
        ))}
      </div>

      <div className="plugin-card-rule" />

      <div className="plugin-card-foot">
        <div className="plugin-card-stats">
          <Tooltip title={rated ? `${plugin.ratingCount} 条评价` : '还没有人评价'}>
            <span className="plugin-card-meta plugin-card-meta-star">
              <StarFilled />
              {rated ? (
                <>
                  {plugin.ratingAverage.toFixed(1)} <span style={{ opacity: 0.7 }}>({plugin.ratingCount})</span>
                </>
              ) : (
                '暂无评分'
              )}
            </span>
          </Tooltip>
          <Tooltip title="下载次数">
            <span className="plugin-card-meta">
              <DownloadOutlined />
              {plugin.downloads}
            </span>
          </Tooltip>
        </div>
        {/* 只有真验过签才亮绿灯。未签名和自签名不在卡片上占位置 ——
            一张卡片上同时挂三种签名状态,读者只会全部略过。 */}
        {trusted ? (
          <Chip tone="ok" icon={<SafetyCertificateOutlined />} title="包带有效签名,升级时可校验发布者身份连续性">
            已验签
          </Chip>
        ) : null}
      </div>
    </article>
  );
}

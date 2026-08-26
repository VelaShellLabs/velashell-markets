import { Chip, PluginIcon } from '@/components';
import { formatRelative } from '@/utils/format';
import { CloudDownloadOutlined, DownloadOutlined, SafetyCertificateOutlined, StarFilled, ThunderboltFilled } from '@ant-design/icons';
import { history } from '@umijs/max';
import { Button } from 'antd';

/**
 * 首屏那张双宽的「编辑推荐」卡片。
 *
 * 存在的理由是打破网格:十二张一模一样的卡片排下来,眼睛没有落点,谁都不会被看见。
 * 推荐位由审核员人工设置(见审核台的插件治理),不按下载量自动算 ——
 * 自动榜单只会把已经很火的插件推得更火,而这个位置的价值恰恰在于能顶起一个没人知道的好插件。
 */
export default function FeaturedCard({ plugin }: { plugin: MarketAPI.PluginSummary }) {
  const rated = plugin.ratingCount > 0;
  const open = () => history.push(`/plugins/${plugin.id}`);

  return (
    <article className="featured-card plugin-grid-featured" onClick={open}>
      <PluginIcon id={plugin.id} name={plugin.displayName} size={56} />

      <div className="featured-main">
        <div className="featured-title">
          <h3>{plugin.displayName}</h3>
          {plugin.latestVersion ? <span className="plugin-card-version">v{plugin.latestVersion}</span> : null}
          <Chip tone="accent" icon={<ThunderboltFilled />}>
            编辑推荐
          </Chip>
        </div>

        <div className="featured-meta">
          <span>{plugin.author ?? plugin.id}</span>
          <span>·</span>
          <span>{formatRelative(plugin.updatedAt)}更新</span>
          {plugin.latestSignature === 'Trusted' ? (
            <Chip tone="ok" icon={<SafetyCertificateOutlined />}>
              已验签
            </Chip>
          ) : null}
        </div>

        <p className="featured-summary">{plugin.summary || plugin.excerpt || '作者未填写简介'}</p>

        <div className="plugin-card-tags">
          {plugin.tags?.slice(0, 4).map((item) => (
            <Chip key={item}>{item}</Chip>
          ))}
        </div>
      </div>

      <div className="featured-side">
        <Button
          type="primary"
          icon={<CloudDownloadOutlined />}
          onClick={(event) => {
            // 卡片整体可点,按钮要自己拦下冒泡,否则点"查看"会被外层再跳一次。
            event.stopPropagation();
            open();
          }}
        >
          查看详情
        </Button>
        <div className="featured-side-stats">
          <span className="plugin-card-meta plugin-card-meta-star">
            <StarFilled />
            {rated ? `${plugin.ratingAverage.toFixed(1)} (${plugin.ratingCount})` : '暂无评分'}
          </span>
          <span className="plugin-card-meta">
            <DownloadOutlined />
            {plugin.downloads}
          </span>
          {plugin.latestApiLevel ? (
            <span className="mono" style={{ fontSize: 11, color: 'var(--ink-3)' }}>
              apiLevel {plugin.latestApiLevel}
              {plugin.latestMinHostVersion ? ` · host ≥ ${plugin.latestMinHostVersion}` : ''}
            </span>
          ) : null}
        </div>
      </div>
    </article>
  );
}

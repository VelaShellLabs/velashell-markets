import { PluginIcon } from '@/components';
import { DownloadOutlined, StarFilled } from '@ant-design/icons';
import { history } from '@umijs/max';
import { Card, Tag } from 'antd';

/** 浏览页的插件卡片:图标 + 名称/作者、两行简介、标签、底部评分与下载量。 */
export default function PluginCard({ plugin }: { plugin: MarketAPI.PluginSummary }) {
  return (
    <Card
      className="plugin-card"
      hoverable
      onClick={() => history.push(`/plugins/${plugin.id}`)}
      styles={{ body: { padding: 18 } }}
    >
      <div className="plugin-card-head">
        <PluginIcon id={plugin.id} name={plugin.displayName} />
        <div className="plugin-card-headtext">
          <div className="plugin-card-title">
            <span className="plugin-card-name">{plugin.displayName}</span>
            {plugin.latestVersion ? (
              <Tag color="blue" bordered={false}>
                v{plugin.latestVersion}
              </Tag>
            ) : null}
          </div>
          <div className="plugin-card-author">{plugin.author ?? plugin.id}</div>
        </div>
      </div>

      <div className="plugin-card-summary">{plugin.summary || plugin.excerpt || '作者未填写简介'}</div>

      <div className="plugin-card-tags">
        {plugin.tags?.slice(0, 3).map((t) => (
          <Tag key={t} bordered={false}>
            {t}
          </Tag>
        ))}
      </div>

      <div className="plugin-card-foot">
        <span className="plugin-card-meta">
          <StarFilled style={{ color: '#faad14' }} />
          {plugin.ratingCount > 0 ? `${plugin.ratingAverage}(${plugin.ratingCount})` : '暂无评分'}
        </span>
        <span className="plugin-card-meta">
          <DownloadOutlined />
          {plugin.downloads}
        </span>
      </div>
    </Card>
  );
}

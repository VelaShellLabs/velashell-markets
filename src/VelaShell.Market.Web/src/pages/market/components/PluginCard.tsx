import { PluginIcon } from '@/components';
import { DownloadOutlined, StarFilled } from '@ant-design/icons';
import { history } from '@umijs/max';
import { Card, Tag, Tooltip } from 'antd';

/** 浏览页的插件卡片:图标 + 名称/版本/作者、两行简介、标签、底部评分与下载量。 */
export default function PluginCard({ plugin }: { plugin: MarketAPI.PluginSummary }) {
  const author = plugin.author ?? plugin.id;
  const rated = plugin.ratingCount > 0;

  return (
    <Card className="plugin-card" hoverable onClick={() => history.push(`/plugins/${plugin.id}`)} styles={{ body: { padding: 18 } }}>
      <div className="plugin-card-head">
        <PluginIcon id={plugin.id} name={plugin.displayName} size={48} />
        <div className="plugin-card-headtext">
          {/* title 兜住被省略号截掉的长名字 —— 插件名经常比卡片宽。 */}
          <span className="plugin-card-name" title={plugin.displayName}>
            {plugin.displayName}
          </span>
          <div className="plugin-card-sub">
            {plugin.latestVersion ? (
              <Tag color="blue" bordered={false}>
                v{plugin.latestVersion}
              </Tag>
            ) : null}
            <span className="plugin-card-author" title={author}>
              {author}
            </span>
          </div>
        </div>
      </div>

      <div className="plugin-card-summary">{plugin.summary || plugin.excerpt || '作者未填写简介'}</div>

      <div className="plugin-card-tags">
        {plugin.tags?.slice(0, 3).map((item) => (
          <Tag key={item} bordered={false}>
            {item}
          </Tag>
        ))}
      </div>

      <div className="plugin-card-foot">
        <Tooltip title={rated ? `${plugin.ratingCount} 条评价` : '还没有人评价'}>
          <span className="plugin-card-meta">
            <StarFilled className={rated ? 'market-star' : 'market-star-empty'} />
            {rated ? `${plugin.ratingAverage}(${plugin.ratingCount})` : '暂无评分'}
          </span>
        </Tooltip>
        <Tooltip title="下载次数">
          <span className="plugin-card-meta">
            <DownloadOutlined />
            {plugin.downloads}
          </span>
        </Tooltip>
      </div>
    </Card>
  );
}

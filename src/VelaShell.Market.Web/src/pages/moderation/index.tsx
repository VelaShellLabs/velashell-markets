import { getQueue } from '@/services/moderation';
import { Card, Result, Skeleton, Space, Tabs, Typography } from 'antd';
import { useEffect, useState } from 'react';
import PluginsPanel from './components/PluginsPanel';
import QueuePanel from './components/QueuePanel';
import ReviewsPanel from './components/ReviewsPanel';

/**
 * 审核台。三件事分三个页签:
 *
 * - **隔离队列**:检测判为"需人工复核"的包,放行或驳回。
 * - **插件治理**:下架 / 恢复 / 强制下架 / 清空违规描述。
 * - **评价治理**:隐藏或彻底删除违规评价。
 *
 * 权限探测放在这里而不是各面板里:路由的 access 已经挡掉了菜单入口,但直接敲 URL 仍然进得来,
 * 那时该看到的是一张"没有权限",而不是三个各自报错的空表格。
 */
export default function ModerationPage() {
  const [denied, setDenied] = useState<boolean | null>(null);

  useEffect(() => {
    // 拿队列当探针:它是最轻的一个审核端点,403 就说明这个 sub 不在审核员名单里。
    getQueue()
      .then(() => setDenied(false))
      .catch((e: any) =>
        setDenied(e?.response?.status === 401 || e?.response?.status === 403),
      );
  }, []);

  if (denied === null) {
    return (
      <div className="market-page">
        <Card>
          <Skeleton active paragraph={{ rows: 4 }} />
        </Card>
      </div>
    );
  }

  if (denied) {
    return (
      <Result
        status="403"
        title="没有审核权限"
        subTitle="审核员名单由部署方在 Auth:ModeratorSubjects 中配置(compose 的 MODERATOR_SUBJECT)。"
      />
    );
  }

  return (
    <div className="market-page">
      <Space direction="vertical" size={0} style={{ marginBottom: 12 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          审核台
        </Typography.Title>
        <Typography.Text type="secondary">
          所有处置都要填原因,并记入服务端日志。不可逆的操作会单独提示。
        </Typography.Text>
      </Space>
      <Tabs
        // destroyInactiveTabPane:切走的面板卸载掉,回来时重新拉一次 ——
        // 审核是多人同时在做的,拿着五分钟前的列表点按钮只会撞 409。
        destroyOnHidden
        items={[
          { key: 'queue', label: '隔离队列', children: <QueuePanel /> },
          { key: 'plugins', label: '插件治理', children: <PluginsPanel /> },
          { key: 'reviews', label: '评价治理', children: <ReviewsPanel /> },
        ]}
      />
    </div>
  );
}

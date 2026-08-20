import { PageShell } from '@/components';
import { useModel } from '@umijs/max';
import { Result, Tabs } from 'antd';
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
 * 是不是审核员直接读 initialState —— 那份结论本来就来自服务端的 /me。
 * 早先这里会先打一次 /moderation/queue 当探针,靠 403 判断权限:多一次请求不说,
 * 非审核员每次进来都要等一个注定失败的往返才看到"没有权限"。
 * 路由上的 access 已经把菜单藏掉,这里只负责直接敲 URL 进来的那种情况。
 */
export default function ModerationPage() {
  const { initialState } = useModel('@@initialState');

  if (!initialState?.currentUser?.isModerator) {
    return <Result status="403" title="没有审核权限" subTitle="审核员名单由部署方在 Auth:ModeratorSubjects 中配置(compose 的 MODERATOR_SUBJECT)。" />;
  }

  return (
    <PageShell title="审核台" description="所有处置都要填原因,并记入服务端日志。不可逆的操作会单独提示。">
      <Tabs
        // 切走的面板卸载掉,回来时重新拉一次 —— 审核是多人同时在做的,
        // 拿着五分钟前的列表点按钮只会撞 409。
        destroyOnHidden
        items={[
          { key: 'queue', label: '隔离队列', children: <QueuePanel /> },
          { key: 'plugins', label: '插件治理', children: <PluginsPanel /> },
          { key: 'reviews', label: '评价治理', children: <ReviewsPanel /> },
        ]}
      />
    </PageShell>
  );
}

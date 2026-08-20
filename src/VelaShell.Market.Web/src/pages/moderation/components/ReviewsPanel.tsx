import { REVIEW_SCOPES, REVIEW_SCOPE_FLAG, type ReviewScope } from '@/configs';
import { usePagedTable } from '@/hooks';
import { getModeratedReviews, hideReview, purgeReview, unhideReview } from '@/services/moderation';
import { formatDateTime } from '@/utils/format';
import { ReloadOutlined } from '@ant-design/icons';
import { App, Button, Empty, Input, Rate, Segmented, Space, Table, Tag, Tooltip, Typography, type TableColumnsType } from 'antd';
import { askReason } from './askReason';

type Filters = { q?: string; pluginId?: string; scope: ReviewScope };

/**
 * 评价治理。两级处置:
 *
 * - **隐藏**是日常手段 —— 可撤销、留档、不计入评分均值。
 * - **彻底删除**用于政治敏感等"留档本身就是负担"的内容,正文从库里消失,不可逆。
 *
 * 正文按 Markdown 原文展示而不是渲染:审核员要判断的是对方写了什么,
 * 渲染这一步恰好会吃掉最该被看见的东西。
 */
export default function ReviewsPanel() {
  const api = App.useApp();

  const { rows, loading, filters, setFilters, reload, pagination } = usePagedTable<ModerationAPI.ModeratedReview, Filters>(
    ({ q, pluginId, scope, page, size }) => getModeratedReviews({ q: q || undefined, pluginId: pluginId || undefined, hidden: REVIEW_SCOPE_FLAG[scope], page, size }),
    { scope: '全部' },
  );

  const hide = (row: ModerationAPI.ModeratedReview) =>
    askReason(
      api,
      {
        title: '隐藏这条评价',
        description: '评价从插件页消失、不再计入评分均值。可以随时取消隐藏,正文保留在库里。',
        placeholder: '隐藏原因(仅审核台可见)',
        okText: '隐藏',
      },
      async (reason) => {
        await hideReview(row.id, reason);
        api.message.success('已隐藏');
        reload();
      },
    );

  const unhide = (row: ModerationAPI.ModeratedReview) =>
    api.modal.confirm({
      title: '取消隐藏',
      content: '这条评价会重新出现在插件页,并重新计入评分均值。',
      okText: '取消隐藏',
      cancelText: '返回',
      onOk: async () => {
        await unhideReview(row.id);
        api.message.success('已取消隐藏');
        reload();
      },
    });

  const purge = (row: ModerationAPI.ModeratedReview) =>
    askReason(
      api,
      {
        title: '彻底删除这条评价',
        danger: '不可逆:正文会从数据库物理删除,无法恢复,也无法用于日后申诉。',
        description: '日常违规请用「隐藏」。这条路径是给必须让内容消失的情况准备的。',
        placeholder: '删除原因(记入服务端日志)',
        okText: '确认彻底删除',
      },
      async (reason) => {
        await purgeReview(row.id, reason);
        api.message.success('已彻底删除');
        reload();
      },
    );

  const columns: TableColumnsType<ModerationAPI.ModeratedReview> = [
    {
      title: '插件',
      dataIndex: 'pluginId',
      width: 180,
      // 点插件 id 即把它填进过滤框:顺着一条差评去看这个插件的全部评价是最常见的动作。
      render: (value: string) => (
        <Typography.Link onClick={() => setFilters({ pluginId: value })} code style={{ fontSize: 12 }}>
          {value}
        </Typography.Link>
      ),
    },
    {
      title: '评价者',
      width: 180,
      render: (_, row) => (
        <Space direction="vertical" size={2}>
          <Typography.Text>{row.displayName ?? '匿名用户'}</Typography.Text>
          <Typography.Text type="secondary" copyable style={{ fontSize: 11 }}>
            {row.subject}
          </Typography.Text>
        </Space>
      ),
    },
    { title: '评分', dataIndex: 'rating', width: 120, render: (value: number) => <Rate disabled value={value} style={{ fontSize: 12 }} /> },
    {
      title: '正文',
      dataIndex: 'body',
      render: (_, row) => (
        <Space direction="vertical" size={4} style={{ width: '100%' }}>
          <Space size={8} wrap>
            {row.isHidden ? (
              <Tooltip title={`${row.hiddenReason ?? ''}(${row.hiddenBySubject ?? '未知审核员'})`}>
                <Tag color="red" bordered={false}>
                  已隐藏
                </Tag>
              </Tooltip>
            ) : null}
            {row.pluginVersion ? (
              <Tag bordered={false} style={{ fontSize: 11 }}>
                v{row.pluginVersion}
              </Tag>
            ) : null}
            <Typography.Text type="secondary" style={{ fontSize: 11 }}>
              {formatDateTime(row.updatedAt)}
            </Typography.Text>
          </Space>
          <Typography.Paragraph type={row.body ? undefined : 'secondary'} style={{ whiteSpace: 'pre-wrap', margin: 0 }} ellipsis={{ rows: 4, expandable: true, symbol: '展开' }}>
            {row.body || '(只打分,没有正文)'}
          </Typography.Paragraph>
        </Space>
      ),
    },
    {
      title: '操作',
      width: 200,
      render: (_, row) => (
        <Space size={4} wrap>
          {row.isHidden ? (
            <Button size="small" onClick={() => unhide(row)}>
              取消隐藏
            </Button>
          ) : (
            <Button size="small" onClick={() => hide(row)}>
              隐藏
            </Button>
          )}
          <Button size="small" danger onClick={() => purge(row)}>
            彻底删除
          </Button>
        </Space>
      ),
    },
  ];

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }} wrap>
        <Space wrap>
          <Input.Search allowClear placeholder="搜索正文 / 昵称 / sub" style={{ width: 260 }} onSearch={(value) => setFilters({ q: value })} />
          <Input allowClear placeholder="按插件 id 过滤" style={{ width: 200 }} value={filters.pluginId ?? ''} onChange={(e) => setFilters({ pluginId: e.target.value })} />
          <Segmented value={filters.scope} options={REVIEW_SCOPES as unknown as string[]} onChange={(value) => setFilters({ scope: value as ReviewScope })} />
        </Space>
        <Button icon={<ReloadOutlined />} onClick={reload} loading={loading}>
          刷新
        </Button>
      </Space>

      <Table<ModerationAPI.ModeratedReview>
        rowKey="id"
        size="small"
        loading={loading}
        columns={columns}
        dataSource={rows}
        locale={{ emptyText: <Empty description="没有匹配的评价" /> }}
        pagination={{ ...pagination, showTotal: (n) => `共 ${n} 条评价` }}
      />
    </>
  );
}

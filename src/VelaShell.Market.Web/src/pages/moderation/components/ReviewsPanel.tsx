import { getModeratedReviews, hideReview, purgeReview, unhideReview } from '@/services/moderation';
import { ReloadOutlined } from '@ant-design/icons';
import {
  App,
  Button,
  Empty,
  Input,
  Rate,
  Segmented,
  Space,
  Table,
  Tag,
  Tooltip,
  Typography,
  type TableColumnsType,
} from 'antd';
import { useCallback, useEffect, useState } from 'react';
import { askReason } from './askReason';

type Scope = '全部' | '显示中' | '已隐藏';

const SCOPE_TO_FLAG: Record<Scope, boolean | undefined> = {
  全部: undefined,
  显示中: false,
  已隐藏: true,
};

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
  const [rows, setRows] = useState<MarketAPI.ModeratedReview[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [size, setSize] = useState(20);
  const [q, setQ] = useState('');
  const [pluginId, setPluginId] = useState('');
  const [scope, setScope] = useState<Scope>('全部');
  const [loading, setLoading] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const data = await getModeratedReviews({
        q: q || undefined,
        pluginId: pluginId || undefined,
        hidden: SCOPE_TO_FLAG[scope],
        page,
        size,
      });
      setRows(data.items);
      setTotal(data.total);
    } catch {
      setRows([]);
      setTotal(0);
    } finally {
      setLoading(false);
    }
  }, [q, pluginId, scope, page, size]);

  useEffect(() => {
    load();
  }, [load]);

  const hide = (row: MarketAPI.ModeratedReview) =>
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
        await load();
      },
    );

  const unhide = (row: MarketAPI.ModeratedReview) =>
    api.modal.confirm({
      title: '取消隐藏',
      content: '这条评价会重新出现在插件页,并重新计入评分均值。',
      okText: '取消隐藏',
      cancelText: '返回',
      onOk: async () => {
        await unhideReview(row.id);
        api.message.success('已取消隐藏');
        await load();
      },
    });

  const purge = (row: MarketAPI.ModeratedReview) =>
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
        await load();
      },
    );

  const columns: TableColumnsType<MarketAPI.ModeratedReview> = [
    {
      title: '插件',
      dataIndex: 'pluginId',
      width: 180,
      render: (v: string) => (
        <Typography.Link onClick={() => setPluginId(v)} code style={{ fontSize: 12 }}>
          {v}
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
    {
      title: '评分',
      dataIndex: 'rating',
      width: 120,
      render: (v: number) => <Rate disabled value={v} style={{ fontSize: 12 }} />,
    },
    {
      title: '正文',
      dataIndex: 'body',
      render: (_, row) => (
        <Space direction="vertical" size={4} style={{ width: '100%' }}>
          <Space size={8} wrap>
            {row.isHidden ? (
              <Tooltip title={`${row.hiddenReason ?? ''}（${row.hiddenBySubject ?? '未知审核员'}）`}>
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
              {new Date(row.updatedAt).toLocaleString()}
            </Typography.Text>
          </Space>
          <Typography.Paragraph
            type={row.body ? undefined : 'secondary'}
            style={{ whiteSpace: 'pre-wrap', margin: 0 }}
            ellipsis={{ rows: 4, expandable: true, symbol: '展开' }}
          >
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
          <Input.Search
            allowClear
            placeholder="搜索正文 / 昵称 / sub"
            style={{ width: 260 }}
            onSearch={(value) => {
              setPage(1);
              setQ(value);
            }}
          />
          <Input
            allowClear
            placeholder="按插件 id 过滤"
            style={{ width: 200 }}
            value={pluginId}
            onChange={(e) => {
              setPage(1);
              setPluginId(e.target.value);
            }}
          />
          <Segmented
            value={scope}
            options={['全部', '显示中', '已隐藏']}
            onChange={(value) => {
              setPage(1);
              setScope(value as Scope);
            }}
          />
        </Space>
        <Button icon={<ReloadOutlined />} onClick={load}>
          刷新
        </Button>
      </Space>

      <Table<MarketAPI.ModeratedReview>
        rowKey="id"
        size="small"
        loading={loading}
        columns={columns}
        dataSource={rows}
        locale={{ emptyText: <Empty description="没有匹配的评价" /> }}
        pagination={{
          current: page,
          pageSize: size,
          total,
          showSizeChanger: true,
          showTotal: (n) => `共 ${n} 条评价`,
          onChange: (nextPage, nextSize) => {
            setPage(nextPage);
            setSize(nextSize);
          },
        }}
      />
    </>
  );
}

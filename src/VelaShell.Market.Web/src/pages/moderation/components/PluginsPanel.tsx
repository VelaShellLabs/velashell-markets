import { PLUGIN_SCOPES, PLUGIN_SCOPE_FLAG, type PluginScope } from '@/configs';
import { usePagedTable } from '@/hooks';
import { clearPluginDescription, getModeratedPlugins, relistPlugin, setPluginFeatured, takedownPlugin, unlistPlugin } from '@/services/moderation';
import { formatDateTime } from '@/utils/format';
import { ReloadOutlined, ThunderboltOutlined } from '@ant-design/icons';
import { App, Button, Empty, Input, Segmented, Space, Table, Tag, Tooltip, Typography, type TableColumnsType } from 'antd';
import { askReason } from './askReason';

type Filters = { q?: string; scope: PluginScope };

/**
 * 插件治理。与浏览页最大的差别:**这里看得见已下架的条目** ——
 * 要恢复一个被误下架的插件,前提是找得到它。
 */
export default function PluginsPanel() {
  const api = App.useApp();

  const { rows, loading, filters, setFilters, reload, pagination } = usePagedTable<ModerationAPI.ModeratedPlugin, Filters>(
    ({ q, scope, page, size }) => getModeratedPlugins({ q: q || undefined, unlisted: PLUGIN_SCOPE_FLAG[scope], page, size }),
    { scope: '全部' },
  );

  const unlist = (row: ModerationAPI.ModeratedPlugin) =>
    askReason(
      api,
      {
        title: `软下架 ${row.id}`,
        description: '插件从检索与详情页消失,但正式桶里的包仍然可以下载,已装用户不受影响。有害的包请用「强制下架」。',
        placeholder: '下架原因(展示给作者)',
        okText: '下架',
      },
      async (reason) => {
        await unlistPlugin(row.id, reason);
        api.message.success('已下架');
        reload();
      },
    );

  const takedown = (row: ModerationAPI.ModeratedPlugin) =>
    askReason(
      api,
      {
        title: `强制下架 ${row.id}`,
        danger: '不可逆:正式桶里的全部已发布版本会被物理删除,待检版本一并封停。',
        description: '恢复上架只能让页面重新可见,包回不来 —— 作者需要重新发版。',
        placeholder: '强制下架原因(展示给作者)',
        okText: '确认强制下架',
      },
      async (reason) => {
        const result = await takedownPlugin(row.id, reason);
        if (result.failedKeys?.length) {
          // 状态已经改成"已撤回",但字节还在桶里 —— 必须说出来,否则会被当成删干净了。
          api.modal.error({
            title: '部分对象未能删除',
            content: (
              <Space orientation="vertical" size={8}>
                <Typography.Text>已删除 {result.deletedVersions} 个版本,以下对象删除失败,需要到对象存储里手工清理:</Typography.Text>
                <Typography.Text code copyable>
                  {result.failedKeys.join('\n')}
                </Typography.Text>
              </Space>
            ),
          });
        } else {
          api.message.success(`已强制下架:删除 ${result.deletedVersions} 个已发布版本、封停 ${result.blockedVersions} 个待检版本`);
        }
        reload();
      },
    );

  const relist = (row: ModerationAPI.ModeratedPlugin) =>
    api.modal.confirm({
      title: `恢复上架 ${row.id}`,
      content: row.latestVersion ? '插件将重新出现在检索与详情页。' : '注意:这个插件没有任何已发布版本(可能被强制下架过),恢复出来是个空壳页面,需要作者重新发版。',
      okText: '恢复上架',
      cancelText: '取消',
      onOk: async () => {
        await relistPlugin(row.id);
        api.message.success('已恢复上架');
        reload();
      },
    });

  const clearDescription = (row: ModerationAPI.ModeratedPlugin) =>
    askReason(
      api,
      {
        title: `清空 ${row.id} 的描述`,
        description: '只清描述,插件继续可用。原因会显示在插件页上,作者重写描述后这条说明自动消失。',
        placeholder: '移除原因(展示给作者与读者)',
        okText: '清空描述',
      },
      async (reason) => {
        await clearPluginDescription(row.id, reason);
        api.message.success('描述已清空');
        reload();
      },
    );

  const toggleFeatured = async (row: ModerationAPI.ModeratedPlugin) => {
    try {
      await setPluginFeatured(row.id, !row.isFeatured);
      api.message.success(row.isFeatured ? '已取消推荐' : `${row.displayName} 已设为编辑推荐`);
      reload();
    } catch {
      // 失败信息已由统一错误处理展示(比如插件已下架、或者一个版本都还没发布)。
    }
  };

  const columns: TableColumnsType<ModerationAPI.ModeratedPlugin> = [
    {
      title: '插件',
      dataIndex: 'id',
      render: (_, row) => (
        <Space orientation="vertical" size={2}>
          <Space size={8} wrap>
            <Typography.Text strong>{row.displayName}</Typography.Text>
            {row.isUnlisted ? (
              <Tooltip title={row.unlistedReason}>
                <Tag color="red" bordered={false}>
                  已下架
                </Tag>
              </Tooltip>
            ) : null}
            {row.descriptionRemovedReason ? (
              <Tooltip title={row.descriptionRemovedReason}>
                <Tag color="orange" bordered={false}>
                  描述已移除
                </Tag>
              </Tooltip>
            ) : null}
          </Space>
          <Typography.Text code style={{ fontSize: 12 }}>
            {row.id}
          </Typography.Text>
        </Space>
      ),
    },
    {
      title: '作者',
      dataIndex: 'ownerSubject',
      width: 200,
      render: (_, row) => (
        <Space orientation="vertical" size={2}>
          <Typography.Text>{row.ownerName ?? '—'}</Typography.Text>
          <Typography.Text type="secondary" copyable style={{ fontSize: 11 }}>
            {row.ownerSubject}
          </Typography.Text>
        </Space>
      ),
    },
    {
      title: '最新版本',
      dataIndex: 'latestVersion',
      width: 110,
      render: (value: string | undefined) => (value ? <Tag bordered={false}>v{value}</Tag> : <Typography.Text type="secondary">无</Typography.Text>),
    },
    {
      title: '下载 / 评分',
      width: 130,
      render: (_, row) => (
        <Typography.Text type="secondary">
          {row.downloads} · {row.ratingAverage.toFixed(1)}({row.ratingCount})
        </Typography.Text>
      ),
    },
    {
      title: '操作',
      width: 370,
      render: (_, row) => (
        <Space size={4} wrap>
          {/* 编辑推荐是加分动作,不必填原因(那条约束是给**处置**用的);
              但服务端仍然记日志 —— 首屏那个位置是稀缺资源。 */}
          <Tooltip title={row.isFeatured ? '取消后浏览页首屏不再展示它' : '设为编辑推荐:浏览页首屏那张双宽卡片'}>
            <Button size="small" type={row.isFeatured ? 'primary' : 'default'} icon={<ThunderboltOutlined />} disabled={!row.isFeatured && (row.isUnlisted || !row.latestVersion)} onClick={() => toggleFeatured(row)}>
              {row.isFeatured ? '推荐中' : '设为推荐'}
            </Button>
          </Tooltip>
          {row.isUnlisted ? (
            <Button size="small" onClick={() => relist(row)}>
              恢复上架
            </Button>
          ) : (
            <Button size="small" onClick={() => unlist(row)}>
              软下架
            </Button>
          )}
          <Button size="small" onClick={() => clearDescription(row)} disabled={!row.descriptionMarkdown}>
            清空描述
          </Button>
          <Button size="small" danger onClick={() => takedown(row)} disabled={!row.latestVersion}>
            强制下架
          </Button>
        </Space>
      ),
    },
  ];

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }} wrap>
        <Space wrap>
          <Input.Search allowClear placeholder="搜索插件 id / 名称 / 作者 sub" style={{ width: 280 }} onSearch={(value) => setFilters({ q: value })} />
          <Segmented value={filters.scope} options={PLUGIN_SCOPES as unknown as string[]} onChange={(value) => setFilters({ scope: value as PluginScope })} />
        </Space>
        <Button icon={<ReloadOutlined />} onClick={reload} loading={loading}>
          刷新
        </Button>
      </Space>

      <Table<ModerationAPI.ModeratedPlugin>
        rowKey="id"
        size="small"
        loading={loading}
        columns={columns}
        dataSource={rows}
        locale={{ emptyText: <Empty description="没有匹配的插件" /> }}
        // 描述是最常见的违规载体,直接展开看原文,不用点进插件页再回来。
        expandable={{
          expandedRowRender: (row) => (
            <Space orientation="vertical" size={8} style={{ width: '100%' }}>
              {row.descriptionRemovedReason ? (
                <Typography.Text type="warning">
                  描述已于 {formatDateTime(row.descriptionRemovedAt)} 被移除:{row.descriptionRemovedReason}
                </Typography.Text>
              ) : null}
              <Typography.Paragraph type={row.descriptionMarkdown ? undefined : 'secondary'} style={{ whiteSpace: 'pre-wrap', margin: 0 }}>
                {row.descriptionMarkdown || '(没有描述)'}
              </Typography.Paragraph>
            </Space>
          ),
        }}
        pagination={{ ...pagination, showTotal: (n) => `共 ${n} 个插件` }}
      />
    </>
  );
}

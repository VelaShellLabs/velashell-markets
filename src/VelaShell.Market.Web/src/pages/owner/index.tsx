import { PageShell } from '@/components';
import { getMyPlugins, updatePlugin } from '@/services/me';
import { formatDateTime } from '@/utils/format';
import { EditOutlined, ReloadOutlined } from '@ant-design/icons';
import { keepResult } from '@/utils/request';
import { history, useRequest } from '@umijs/max';
import { App, Button, Card, Drawer, Empty, Form, Input, Skeleton, Space, Table, Tag, Tooltip, Typography } from 'antd';
import { useState } from 'react';

/**
 * 我的插件:改页面文案与标签。
 *
 * 刻意**不能在这里改 id、版本、兼容信息** —— 那些一律取自包内的 plugin.json,
 * 让页面能改它们等于让展示与实际安装的东西对不上。
 */
export default function OwnerPage() {
  const { message } = App.useApp();
  const [form] = Form.useForm<MeAPI.PluginPatch>();
  const [editing, setEditing] = useState<MeAPI.MyPlugin | null>(null);

  const { data, loading, refresh } = useRequest(getMyPlugins, { formatResult: keepResult, onError: () => undefined });
  const items = data ?? [];

  const { run: save, loading: saving } = useRequest((values: MeAPI.PluginPatch) => updatePlugin(editing!.id, values), {
    manual: true,
    onSuccess: () => {
      message.success('已保存');
      setEditing(null);
      refresh();
    },
    // 失败信息已由统一错误处理展示。
    onError: () => undefined,
  });

  const openEditor = (plugin: MeAPI.MyPlugin) => {
    setEditing(plugin);
    form.setFieldsValue({
      descriptionMarkdown: plugin.descriptionMarkdown,
      tags: plugin.tags?.join(', '),
      homepage: plugin.homepage,
    });
  };

  return (
    <PageShell
      title="我的插件"
      description="这里改的是插件页面的文案;id、版本与兼容信息取自包内的 plugin.json,改不了。"
      extra={
        <Button icon={<ReloadOutlined />} onClick={refresh}>
          刷新
        </Button>
      }
    >
      {loading && !data ? (
        <Card>
          <Skeleton active paragraph={{ rows: 4 }} />
        </Card>
      ) : items.length === 0 ? (
        <Card>
          <Empty description="你还没有发布过插件">
            <Button type="primary" onClick={() => history.push('/upload')}>
              去发布
            </Button>
          </Empty>
        </Card>
      ) : (
        <Card styles={{ body: { padding: 0 } }}>
          <Table<MeAPI.MyPlugin>
            rowKey="id"
            dataSource={items}
            loading={loading}
            pagination={false}
            scroll={{ x: 720 }}
            columns={[
              {
                title: '插件',
                render: (_, row) => (
                  <Space direction="vertical" size={0}>
                    <a onClick={() => history.push(`/plugins/${row.id}`)}>{row.displayName}</a>
                    <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                      {row.id}
                    </Typography.Text>
                  </Space>
                ),
              },
              { title: '最新版本', dataIndex: 'latestVersion', width: 110, render: (value) => (value ? <Tag bordered={false}>v{value}</Tag> : <Tag bordered={false}>未发布</Tag>) },
              { title: '下载', dataIndex: 'downloads', width: 90 },
              { title: '评分', width: 120, render: (_, row) => (row.ratingCount > 0 ? `${row.ratingAverage.toFixed(1)} (${row.ratingCount})` : '—') },
              {
                title: '状态',
                width: 110,
                // 下架原因挂在标签上:作者最想知道的就是"为什么被下了"。
                render: (_, row) =>
                  row.isUnlisted ? (
                    <Tooltip title={row.unlistedReason}>
                      <Tag color="error" bordered={false}>
                        已下架
                      </Tag>
                    </Tooltip>
                  ) : (
                    <Tag color="success" bordered={false}>
                      正常
                    </Tag>
                  ),
              },
              { title: '更新于', dataIndex: 'updatedAt', width: 180, render: formatDateTime },
              {
                title: '',
                width: 90,
                render: (_, row) => (
                  <Button type="link" icon={<EditOutlined />} onClick={() => openEditor(row)}>
                    编辑
                  </Button>
                ),
              },
            ]}
          />
        </Card>
      )}

      <Drawer
        title={editing ? `编辑 ${editing.displayName}` : ''}
        width={640}
        open={!!editing}
        onClose={() => setEditing(null)}
        extra={
          <Button type="primary" loading={saving} onClick={() => form.submit()}>
            保存
          </Button>
        }
      >
        <Form form={form} layout="vertical" onFinish={save}>
          <Form.Item name="descriptionMarkdown" label="插件说明(Markdown)" extra="名称、版本与兼容信息取自包内的 plugin.json,不能在这里改。">
            <Input.TextArea rows={16} />
          </Form.Item>
          <Form.Item name="tags" label="标签" extra="逗号分隔,最多 10 个">
            <Input />
          </Form.Item>
          <Form.Item name="homepage" label="主页 / 仓库地址">
            <Input placeholder="https://github.com/…" />
          </Form.Item>
        </Form>
      </Drawer>
    </PageShell>
  );
}

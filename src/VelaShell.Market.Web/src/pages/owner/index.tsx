import { getMyPlugins, updatePlugin } from '@/services/me';
import { EditOutlined } from '@ant-design/icons';
import { history } from '@umijs/max';
import { App, Button, Card, Drawer, Empty, Form, Input, Skeleton, Space, Table, Tag, Typography } from 'antd';
import { useEffect, useState } from 'react';

/**
 * 我的插件:改页面文案与标签。
 *
 * 刻意**不能在这里改 id、版本、兼容信息** —— 那些一律取自包内的 plugin.json,
 * 让页面能改它们等于让展示与实际安装的东西对不上。
 */
export default function OwnerPage() {
  const { message } = App.useApp();
  const [items, setItems] = useState<MarketAPI.MyPlugin[] | null>(null);
  const [editing, setEditing] = useState<MarketAPI.MyPlugin | null>(null);
  const [form] = Form.useForm();

  const load = () =>
    getMyPlugins()
      .then(setItems)
      .catch(() => setItems([]));

  useEffect(() => {
    load();
  }, []);

  const openEditor = (plugin: MarketAPI.MyPlugin) => {
    setEditing(plugin);
    form.setFieldsValue({
      descriptionMarkdown: plugin.descriptionMarkdown,
      tags: plugin.tags?.join(', '),
      homepage: plugin.homepage,
    });
  };

  const save = async (values: any) => {
    try {
      await updatePlugin(editing!.id, values);
      message.success('已保存');
      setEditing(null);
      await load();
    } catch {
      // 失败信息已由统一错误处理展示。
    }
  };

  return (
    <div className="market-page">
      <Typography.Title level={3}>我的插件</Typography.Title>

      {items === null ? (
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
          <Table<MarketAPI.MyPlugin>
            rowKey="id"
            dataSource={items}
            pagination={false}
            scroll={{ x: 720 }}
            columns={[
              {
                title: '插件',
                render: (_, r) => (
                  <Space direction="vertical" size={0}>
                    <a onClick={() => history.push(`/plugins/${r.id}`)}>{r.displayName}</a>
                    <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                      {r.id}
                    </Typography.Text>
                  </Space>
                ),
              },
              {
                title: '最新版本',
                dataIndex: 'latestVersion',
                render: (v) => v ?? <Tag bordered={false}>未发布</Tag>,
              },
              { title: '下载', dataIndex: 'downloads', width: 90 },
              {
                title: '评分',
                width: 120,
                render: (_, r) => (r.ratingCount > 0 ? `${r.ratingAverage} (${r.ratingCount})` : '—'),
              },
              {
                title: '状态',
                width: 110,
                render: (_, r) =>
                  r.isUnlisted ? (
                    <Tag color="error" bordered={false}>
                      已下架
                    </Tag>
                  ) : (
                    <Tag color="success" bordered={false}>
                      正常
                    </Tag>
                  ),
              },
              {
                title: '',
                width: 90,
                render: (_, r) => (
                  <Button type="link" icon={<EditOutlined />} onClick={() => openEditor(r)}>
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
          <Button type="primary" onClick={() => form.submit()}>
            保存
          </Button>
        }
      >
        <Form form={form} layout="vertical" onFinish={save}>
          <Form.Item
            name="descriptionMarkdown"
            label="插件说明(Markdown)"
            extra="名称、版本与兼容信息取自包内的 plugin.json,不能在这里改。"
          >
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
    </div>
  );
}

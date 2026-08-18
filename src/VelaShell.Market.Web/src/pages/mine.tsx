import { useEffect, useState } from 'react';
import { Button, Card, Descriptions, Empty, Skeleton, Space, Table, Tag, Typography } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import { history } from 'umi';
import { api } from '../auth';
import Findings, { statusTag, verdictTag, type Finding } from '../components/Findings';

type Upload = {
  pluginId: string;
  version: string;
  status: string;
  uploadedAt: string;
  publishedAt?: string;
  packageSize: number;
  signature: string;
  scan?: {
    verdict: string;
    startedAt: string;
    completedAt?: string;
    engines: Record<string, string>;
    findings: Finding[];
  } | null;
};

/** 我的上传:检测进度与完整报告。被拒却看不到原因,只会换来盲目重传。 */
export default function MinePage() {
  const [items, setItems] = useState<Upload[] | null>(null);

  const load = () =>
    api('/uploads/mine')
      .then((r) => r.json())
      .then(setItems)
      .catch(() => setItems([]));

  useEffect(() => {
    load();
  }, []);

  return (
    <div className="market-page">
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 20 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>我的上传</Typography.Title>
        <Space>
          <Button icon={<ReloadOutlined />} onClick={load}>刷新</Button>
          <Button type="primary" onClick={() => history.push('/upload')}>发布新版本</Button>
        </Space>
      </Space>

      {items === null ? (
        <Card><Skeleton active paragraph={{ rows: 5 }} /></Card>
      ) : items.length === 0 ? (
        <Card>
          <Empty description="还没有上传过插件">
            <Button type="primary" onClick={() => history.push('/upload')}>发布第一个</Button>
          </Empty>
        </Card>
      ) : (
        <Card styles={{ body: { padding: 0 } }}>
          <Table<Upload>
            rowKey={(r) => `${r.pluginId}@${r.version}`}
            dataSource={items}
            pagination={false}
            scroll={{ x: 720 }}
            columns={[
              {
                title: '插件',
                dataIndex: 'pluginId',
                render: (id: string, r) =>
                  r.status === 'Published' ? (
                    <a onClick={() => history.push(`/plugins/${id}`)}>{id}</a>
                  ) : (
                    <Typography.Text>{id}</Typography.Text>
                  ),
              },
              { title: '版本', dataIndex: 'version', width: 110 },
              {
                title: '状态',
                dataIndex: 'status',
                width: 110,
                render: (s: string) => {
                  const t = statusTag[s] ?? { color: 'default', text: s };
                  return <Tag color={t.color} bordered={false}>{t.text}</Tag>;
                },
              },
              {
                title: '检测结论',
                width: 130,
                render: (_, r) => {
                  const v = r.scan?.verdict;
                  if (!v) return '—';
                  const t = verdictTag[v] ?? { color: 'default', text: v };
                  return <Tag color={t.color} bordered={false}>{t.text}</Tag>;
                },
              },
              { title: '签名', dataIndex: 'signature', width: 100, render: (v) => <Tag bordered={false}>{v}</Tag> },
              {
                title: '上传时间',
                dataIndex: 'uploadedAt',
                render: (v: string) => new Date(v).toLocaleString(),
              },
            ]}
            expandable={{
              expandedRowRender: (r) => (
                <Space direction="vertical" size={12} style={{ width: '100%' }}>
                  <Descriptions size="small" column={{ xs: 1, sm: 2, md: 3 }} colon={false}>
                    <Descriptions.Item label="开始">
                      {r.scan?.startedAt ? new Date(r.scan.startedAt).toLocaleString() : '—'}
                    </Descriptions.Item>
                    <Descriptions.Item label="结束">
                      {r.scan?.completedAt ? new Date(r.scan.completedAt).toLocaleString() : '进行中'}
                    </Descriptions.Item>
                    <Descriptions.Item label="引擎">
                      {r.scan?.engines
                        ? Object.entries(r.scan.engines).map(([k, v]) => `${k} ${v}`).join(' · ')
                        : '—'}
                    </Descriptions.Item>
                  </Descriptions>
                  <Findings findings={r.scan?.findings ?? []} />
                </Space>
              ),
            }}
          />
        </Card>
      )}
    </div>
  );
}

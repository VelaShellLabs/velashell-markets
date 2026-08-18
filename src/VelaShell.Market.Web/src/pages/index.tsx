import { useEffect, useState } from 'react';
import { Card, Input, List, Rate, Tag, Space, Typography } from 'antd';
import { history } from 'umi';
import { api } from '../auth';

type PluginSummary = {
  id: string;
  displayName: string;
  summary?: string;
  excerpt?: string;
  author?: string;
  tags: string[];
  latestVersion?: string;
  latestApiLevel?: number;
  downloads: number;
  ratingAverage: number;
  ratingCount: number;
};

/** 插件列表页(骨架):检索 + 卡片列表。 */
export default function IndexPage() {
  const [items, setItems] = useState<PluginSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [q, setQ] = useState('');

  useEffect(() => {
    setLoading(true);
    api(`/plugins?q=${encodeURIComponent(q)}&size=20`)
      .then((r) => r.json())
      .then((d) => setItems(d.items ?? []))
      .finally(() => setLoading(false));
  }, [q]);

  return (
    <div style={{ maxWidth: 960, margin: '32px auto', padding: '0 16px' }}>
      <Typography.Title level={3}>VelaShell 插件市场</Typography.Title>
      <Input.Search placeholder="搜索插件…" onSearch={setQ} allowClear style={{ marginBottom: 24 }} />
      <List
        loading={loading}
        dataSource={items}
        renderItem={(p) => (
          <Card hoverable style={{ marginBottom: 12 }} onClick={() => history.push(`/plugins/${p.id}`)}>
            <Space direction="vertical" size={4} style={{ width: '100%' }}>
              <Space>
                <Typography.Text strong>{p.displayName}</Typography.Text>
                <Tag>{p.latestVersion}</Tag>
                {p.latestApiLevel ? <Tag color="blue">apiLevel {p.latestApiLevel}</Tag> : null}
              </Space>
              <Typography.Text type="secondary">{p.summary || p.excerpt}</Typography.Text>
              <Space>
                <Rate disabled allowHalf value={p.ratingAverage} style={{ fontSize: 12 }} />
                <Typography.Text type="secondary">
                  {p.ratingCount} 条评价 · {p.downloads} 次下载
                </Typography.Text>
              </Space>
              <Space size={4}>
                {p.tags?.map((t) => (
                  <Tag key={t}>{t}</Tag>
                ))}
              </Space>
            </Space>
          </Card>
        )}
      />
    </div>
  );
}

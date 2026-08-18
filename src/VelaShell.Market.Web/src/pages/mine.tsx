import { useEffect, useState } from 'react';
import { Card, Table, Tag, Typography } from 'antd';
import { api } from '../auth';

const severityColor: Record<string, string> = { Blocking: 'red', Warning: 'orange', Info: 'default' };

/** 我的上传(骨架):这里能看到完整检测报告 —— 被拒却看不到原因,只会换来盲目重传。 */
export default function MinePage() {
  const [items, setItems] = useState<any[]>([]);

  useEffect(() => {
    api('/uploads/mine')
      .then((r) => r.json())
      .then(setItems);
  }, []);

  return (
    <div style={{ maxWidth: 960, margin: '32px auto', padding: '0 16px' }}>
      <Typography.Title level={3}>我的上传</Typography.Title>
      <Table
        rowKey={(r: any) => `${r.pluginId}@${r.version}`}
        dataSource={items}
        columns={[
          { title: '插件', dataIndex: 'pluginId' },
          { title: '版本', dataIndex: 'version' },
          {
            title: '状态',
            dataIndex: 'status',
            render: (s: string) => (
              <Tag color={s === 'Published' ? 'green' : s === 'Rejected' ? 'red' : 'orange'}>{s}</Tag>
            ),
          },
          { title: '结论', render: (_, r: any) => r.scan?.verdict ?? '—' },
        ]}
        expandable={{
          expandedRowRender: (r: any) => (
            <Card size="small" title="检测报告">
              {(r.scan?.findings ?? []).map((f: any, i: number) => (
                <div key={i}>
                  <Tag color={severityColor[f.severity]}>{f.severity}</Tag>
                  <code>{f.code}</code> {f.message} {f.path ? <em>({f.path})</em> : null}
                </div>
              ))}
            </Card>
          ),
        }}
      />
    </div>
  );
}

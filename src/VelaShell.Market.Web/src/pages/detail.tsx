import { useEffect, useState } from 'react';
import { Button, Card, Descriptions, Rate, Space, Table, Typography, message } from 'antd';
import { useParams } from 'umi';
import { api } from '../auth';

/** 插件详情页(骨架):Markdown 描述由后端渲染并清洗后下发,前端只负责展示。 */
export default function DetailPage() {
  const { id } = useParams<{ id: string }>();
  const [data, setData] = useState<any>(null);

  useEffect(() => {
    api(`/plugins/${id}`)
      .then((r) => r.json())
      .then(setData);
  }, [id]);

  const download = async (version: string) => {
    const response = await api(`/plugins/${id}/versions/${version}/download`);
    if (!response.ok) {
      message.error('获取下载地址失败');
      return;
    }
    const { url } = await response.json();
    window.location.href = url;
  };

  if (!data) return null;
  return (
    <div style={{ maxWidth: 960, margin: '32px auto', padding: '0 16px' }}>
      <Typography.Title level={3}>{data.displayName}</Typography.Title>
      <Space>
        <Rate disabled allowHalf value={data.ratingAverage} />
        <span>{data.ratingCount} 条评价</span>
      </Space>
      <Descriptions bordered size="small" style={{ margin: '16px 0' }} column={2}>
        <Descriptions.Item label="插件 id">{data.id}</Descriptions.Item>
        <Descriptions.Item label="作者">{data.author ?? '—'}</Descriptions.Item>
        <Descriptions.Item label="许可证">{data.license ?? '—'}</Descriptions.Item>
        <Descriptions.Item label="下载">{data.downloads}</Descriptions.Item>
      </Descriptions>
      {/* 后端已用 Markdig 关掉 HTML 直通并做过白名单清洗,这里直接渲染其产物。 */}
      <Card title="说明">
        <div dangerouslySetInnerHTML={{ __html: data.descriptionHtml }} />
      </Card>
      <Card title="版本" style={{ marginTop: 16 }}>
        <Table
          rowKey="version"
          dataSource={data.versions}
          pagination={false}
          columns={[
            { title: '版本', dataIndex: 'version' },
            { title: 'apiLevel', dataIndex: 'apiLevel' },
            { title: '最低宿主版本', dataIndex: 'minHostVersion', render: (v) => v ?? '—' },
            { title: '宿主模式', dataIndex: 'hostMode' },
            { title: '签名', dataIndex: 'signature' },
            {
              title: '',
              render: (_, r: any) => (
                <Button type="link" onClick={() => download(r.version)}>
                  下载 .vpx
                </Button>
              ),
            },
          ]}
        />
      </Card>
    </div>
  );
}

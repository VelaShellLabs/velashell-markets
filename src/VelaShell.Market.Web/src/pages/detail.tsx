import { useEffect, useState } from 'react';
import {
  App, Alert, Button, Card, Col, Descriptions, Row, Skeleton, Space, Table, Tabs, Tag, Tooltip, Typography, Result,
} from 'antd';
import { CloudDownloadOutlined, SafetyCertificateOutlined, WarningOutlined } from '@ant-design/icons';
import { useParams } from 'umi';
import { api, getUser } from '../auth';
import ReviewSection from '../components/ReviewSection';

type Version = {
  version: string;
  apiLevel: number;
  minHostVersion?: string;
  hostMode: string;
  packageSize: number;
  payloadSha256: string;
  fileSha256: string;
  signature: string;
  releaseNotesHtml: string;
  publishedAt?: string;
  downloads: number;
};

type Detail = {
  id: string;
  displayName: string;
  summary?: string;
  descriptionHtml: string;
  author?: string;
  publisher?: string;
  tags: string[];
  homepage?: string;
  license?: string;
  downloads: number;
  ratingAverage: number;
  ratingCount: number;
  createdAt: string;
  updatedAt: string;
  versions: Version[];
};

const formatSize = (bytes: number) =>
  bytes > 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;

/** 签名状态的展示。未签名不是错误,但值得让人看见 —— 它决定了升级时能不能验身份连续性。 */
function SignatureTag({ state }: { state: string }) {
  if (state === 'Trusted') {
    return (
      <Tooltip title="包带有效签名,升级时可校验发布者身份连续性">
        <Tag color="success" bordered={false} icon={<SafetyCertificateOutlined />}>已签名</Tag>
      </Tooltip>
    );
  }
  if (state === 'Untrusted') {
    return <Tag color="warning" bordered={false} icon={<WarningOutlined />}>自签名</Tag>;
  }
  return (
    <Tooltip title="作者未对该包签名。市场当前允许未签名的包上架">
      <Tag bordered={false}>未签名</Tag>
    </Tooltip>
  );
}

/** 插件详情页:左侧内容(说明/版本/评价),右侧元信息与下载。 */
export default function DetailPage() {
  const { id = '' } = useParams<{ id: string }>();
  const { message } = App.useApp();
  const [data, setData] = useState<Detail | null>(null);
  const [missing, setMissing] = useState(false);
  const [isOwner, setIsOwner] = useState(false);

  useEffect(() => {
    api(`/plugins/${id}`).then(async (r) => {
      if (!r.ok) {
        setMissing(true);
        return;
      }
      setData(await r.json());
    });
    // 是不是拥有者决定评价区的形态(自己不能评自己)。
    getUser().then(async (user) => {
      if (!user) return;
      const response = await api('/me/plugins');
      if (!response.ok) return;
      const mine: { id: string }[] = await response.json();
      setIsOwner(mine.some((p) => p.id === id));
    });
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

  if (missing) {
    return <Result status="404" title="插件不存在" subTitle="它可能已被下架,或者链接有误。" />;
  }
  if (!data) {
    return (
      <div className="market-page">
        <Card><Skeleton active paragraph={{ rows: 8 }} /></Card>
      </div>
    );
  }

  const latest = data.versions[0];

  return (
    <div className="market-page">
      <Row gutter={24}>
        <Col xs={24} lg={17}>
          <Space direction="vertical" size={4} style={{ marginBottom: 20 }}>
            <Space align="center" wrap>
              <Typography.Title level={3} style={{ margin: 0 }}>{data.displayName}</Typography.Title>
              {latest ? <Tag color="blue" bordered={false}>v{latest.version}</Tag> : null}
              {latest ? <SignatureTag state={latest.signature} /> : null}
            </Space>
            <Typography.Text type="secondary">{data.summary}</Typography.Text>
            <Space size={4} wrap style={{ marginTop: 6 }}>
              {data.tags?.map((t) => <Tag key={t} bordered={false}>{t}</Tag>)}
            </Space>
          </Space>

          <Card>
            <Tabs
              items={[
                {
                  key: 'readme',
                  label: '说明',
                  children: data.descriptionHtml ? (
                    <div className="markdown-body" dangerouslySetInnerHTML={{ __html: data.descriptionHtml }} />
                  ) : (
                    <Typography.Text type="secondary">作者还没有填写说明。</Typography.Text>
                  ),
                },
                {
                  key: 'versions',
                  label: `版本 (${data.versions.length})`,
                  children: (
                    <Table<Version>
                      rowKey="version"
                      dataSource={data.versions}
                      pagination={false}
                      size="middle"
                      scroll={{ x: 720 }}
                      expandable={{
                        rowExpandable: (r) => !!r.releaseNotesHtml,
                        expandedRowRender: (r) => (
                          <div className="markdown-body" dangerouslySetInnerHTML={{ __html: r.releaseNotesHtml }} />
                        ),
                      }}
                      columns={[
                        { title: '版本', dataIndex: 'version', render: (v) => <Typography.Text strong>{v}</Typography.Text> },
                        { title: 'apiLevel', dataIndex: 'apiLevel', width: 92 },
                        { title: '最低宿主版本', dataIndex: 'minHostVersion', render: (v) => v ?? '—' },
                        { title: '宿主模式', dataIndex: 'hostMode', render: (v) => <Tag bordered={false}>{v}</Tag> },
                        { title: '大小', dataIndex: 'packageSize', render: formatSize },
                        { title: '签名', dataIndex: 'signature', render: (v) => <SignatureTag state={v} /> },
                        { title: '下载', dataIndex: 'downloads', width: 80 },
                        {
                          title: '',
                          width: 110,
                          render: (_, r) => (
                            <Button type="link" icon={<CloudDownloadOutlined />} onClick={() => download(r.version)}>
                              下载
                            </Button>
                          ),
                        },
                      ]}
                    />
                  ),
                },
                {
                  key: 'reviews',
                  label: `评价 (${data.ratingCount})`,
                  children: <ReviewSection pluginId={id} isOwner={isOwner} />,
                },
              ]}
            />
          </Card>
        </Col>

        <Col xs={24} lg={7}>
          <Card size="small" style={{ marginBottom: 16 }}>
            <Space direction="vertical" size={12} style={{ width: '100%' }}>
              <Button
                type="primary"
                size="large"
                block
                icon={<CloudDownloadOutlined />}
                disabled={!latest}
                onClick={() => latest && download(latest.version)}
              >
                下载 .vpx
              </Button>
              <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                在 VelaShell 的插件管理页「安装 .vpx…」选择该文件即可安装。
              </Typography.Text>
            </Space>
          </Card>

          <Card size="small" title="信息" style={{ marginBottom: 16 }}>
            <Descriptions column={1} size="small" colon={false}>
              <Descriptions.Item label="插件 id">
                <Typography.Text copyable code style={{ fontSize: 12 }}>{data.id}</Typography.Text>
              </Descriptions.Item>
              <Descriptions.Item label="作者">{data.author ?? '—'}</Descriptions.Item>
              <Descriptions.Item label="许可证">{data.license ?? '—'}</Descriptions.Item>
              <Descriptions.Item label="主页">
                {data.homepage ? (
                  <a href={data.homepage} target="_blank" rel="noreferrer noopener">{data.homepage}</a>
                ) : '—'}
              </Descriptions.Item>
              <Descriptions.Item label="下载量">{data.downloads}</Descriptions.Item>
              <Descriptions.Item label="评分">
                {data.ratingCount > 0 ? `${data.ratingAverage} / 5 · ${data.ratingCount} 条` : '暂无评价'}
              </Descriptions.Item>
              <Descriptions.Item label="更新于">{new Date(data.updatedAt).toLocaleDateString()}</Descriptions.Item>
            </Descriptions>
          </Card>

          {latest ? (
            <Card size="small" title="完整性校验">
              <Space direction="vertical" size={8} style={{ width: '100%' }}>
                <Alert
                  type="info"
                  showIcon
                  message="下载后可核对校验和"
                  description={<span>用 <code>vela-plugin verify</code> 可以一并校验容器完整性与签名。</span>}
                />
                <div>
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>载荷 SHA-256</Typography.Text>
                  <Typography.Paragraph copyable={{ text: latest.payloadSha256 }} style={{ fontSize: 11, wordBreak: 'break-all', marginBottom: 8 }}>
                    {latest.payloadSha256}
                  </Typography.Paragraph>
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>整包 SHA-256</Typography.Text>
                  <Typography.Paragraph copyable={{ text: latest.fileSha256 }} style={{ fontSize: 11, wordBreak: 'break-all', marginBottom: 0 }}>
                    {latest.fileSha256}
                  </Typography.Paragraph>
                </div>
              </Space>
            </Card>
          ) : null}
        </Col>
      </Row>
    </div>
  );
}

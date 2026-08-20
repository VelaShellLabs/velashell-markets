import { PluginIcon, SignatureTag } from '@/components';
import { getMyPlugins } from '@/services/me';
import { getDownloadUrl, getPlugin } from '@/services/market';
import { CloudDownloadOutlined } from '@ant-design/icons';
import { useParams } from '@umijs/max';
import {
  Alert, App, Button, Card, Col, Descriptions, Result, Row, Skeleton, Space, Table, Tabs, Tag, Typography,
} from 'antd';
import { useEffect, useState } from 'react';
import { getUser } from '@/utils/auth';
import ReviewSection from './components/ReviewSection';

const formatSize = (bytes: number) =>
  bytes > 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;

/** 插件详情页:左侧内容(说明/版本/评价),右侧元信息与下载。 */
export default function PluginDetailPage() {
  const { id = '' } = useParams<{ id: string }>();
  const { message } = App.useApp();
  const [data, setData] = useState<MarketAPI.PluginDetail | null>(null);
  const [missing, setMissing] = useState(false);
  const [isOwner, setIsOwner] = useState(false);

  useEffect(() => {
    getPlugin(id)
      .then(setData)
      .catch(() => setMissing(true));
    // 是不是拥有者决定评价区的形态(自己不能评自己)。
    getUser().then(async (user) => {
      if (!user) return;
      try {
        const mine = await getMyPlugins();
        setIsOwner(mine.some((p) => p.id === id));
      } catch {
        setIsOwner(false);
      }
    });
  }, [id]);

  const download = async (version: string) => {
    try {
      const { url } = await getDownloadUrl(id, version);
      window.location.href = url;
    } catch {
      message.error('获取下载地址失败');
    }
  };

  if (missing) {
    return <Result status="404" title="插件不存在" subTitle="它可能已被下架,或者链接有误。" />;
  }
  if (!data) {
    return (
      <div className="market-page">
        <Card>
          <Skeleton active paragraph={{ rows: 8 }} />
        </Card>
      </div>
    );
  }

  const latest = data.versions[0];

  return (
    <div className="market-page">
      <Row gutter={24}>
        <Col xs={24} lg={17}>
          <Space align="start" size={16} style={{ marginBottom: 20 }}>
            <PluginIcon id={data.id} name={data.displayName} size={64} />
            <Space direction="vertical" size={4}>
              <Space align="center" wrap>
                <Typography.Title level={3} style={{ margin: 0 }}>
                  {data.displayName}
                </Typography.Title>
                {latest ? (
                  <Tag color="blue" bordered={false}>
                    v{latest.version}
                  </Tag>
                ) : null}
                {latest ? <SignatureTag state={latest.signature} /> : null}
              </Space>
              <Typography.Text type="secondary">{data.summary}</Typography.Text>
              <Space size={4} wrap style={{ marginTop: 6 }}>
                {data.tags?.map((t) => (
                  <Tag key={t} bordered={false}>
                    {t}
                  </Tag>
                ))}
              </Space>
            </Space>
          </Space>

          <Card>
            <Tabs
              items={[
                {
                  key: 'readme',
                  label: '说明',
                  // 描述被审核员清空时,必须说清"这里为什么是空的" ——
                  // 否则读者只会以为作者懒得写,而作者也不知道自己该改什么。
                  children: data.descriptionRemovedReason ? (
                    <Alert
                      type="warning"
                      showIcon
                      message="该说明因违规已被移除"
                      description={data.descriptionRemovedReason}
                    />
                  ) : data.descriptionHtml ? (
                    <div className="markdown-body" dangerouslySetInnerHTML={{ __html: data.descriptionHtml }} />
                  ) : (
                    <Typography.Text type="secondary">作者还没有填写说明。</Typography.Text>
                  ),
                },
                {
                  key: 'versions',
                  label: `版本 (${data.versions.length})`,
                  children: (
                    <Table<MarketAPI.Version>
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
                        {
                          title: '版本',
                          dataIndex: 'version',
                          render: (v) => <Typography.Text strong>{v}</Typography.Text>,
                        },
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
                <Typography.Text copyable code style={{ fontSize: 12 }}>
                  {data.id}
                </Typography.Text>
              </Descriptions.Item>
              <Descriptions.Item label="作者">{data.author ?? '—'}</Descriptions.Item>
              <Descriptions.Item label="许可证">{data.license ?? '—'}</Descriptions.Item>
              <Descriptions.Item label="主页">
                {data.homepage ? (
                  <a href={data.homepage} target="_blank" rel="noreferrer noopener">
                    {data.homepage}
                  </a>
                ) : (
                  '—'
                )}
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
                  description={
                    <span>
                      用 <code>vela-plugin verify</code> 可以一并校验容器完整性与签名。
                    </span>
                  }
                />
                <div>
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    载荷 SHA-256
                  </Typography.Text>
                  <Typography.Paragraph
                    copyable={{ text: latest.payloadSha256 }}
                    style={{ fontSize: 11, wordBreak: 'break-all', marginBottom: 8 }}
                  >
                    {latest.payloadSha256}
                  </Typography.Paragraph>
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    整包 SHA-256
                  </Typography.Text>
                  <Typography.Paragraph
                    copyable={{ text: latest.fileSha256 }}
                    style={{ fontSize: 11, wordBreak: 'break-all', marginBottom: 0 }}
                  >
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

import { Alert, App, Button, Card, Col, Descriptions, Result, Row, Skeleton, Space, Table, Tabs, Tag, Typography } from 'antd';
import { PluginIcon, SignatureTag } from '@/components';
import { formatDate, formatRating, formatSize } from '@/utils/format';
import { getDownloadUrl, getPlugin } from '@/services/market';
import { useModel, useParams, useRequest } from '@umijs/max';

import { CloudDownloadOutlined } from '@ant-design/icons';
import ReviewSection from './components/ReviewSection';
import { getMyPlugins } from '@/services/me';
import { keepResult } from '@/utils/request';

/** 插件详情页:左侧内容(说明/版本/评价),右侧元信息与下载。 */
export default function PluginDetailPage() {
  const { id = '' } = useParams<{ id: string }>();
  const { message } = App.useApp();
  const { initialState } = useModel('@@initialState');
  const signedIn = !!initialState?.currentUser;

  const { data, error } = useRequest(() => getPlugin(id), { formatResult: keepResult, refreshDeps: [id], onError: () => undefined });

  /**
   * 是不是拥有者决定评价区的形态(自己不能评自己)。
   * `ready` 挡住匿名访客:详情页是全站最常被打开的一页,没登录还去打一次 /me/plugins
   * 只会换回一个 401。
   */
  const { data: myPlugins } = useRequest(getMyPlugins, { formatResult: keepResult, ready: signedIn, onError: () => undefined });
  const isOwner = !!myPlugins?.some((plugin) => plugin.id === id);

  const download = async (version: string) => {
    try {
      const { url } = await getDownloadUrl(id, version);
      window.location.href = url;
    } catch {
      message.error('获取下载地址失败');
    }
  };

  if (error) {
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
            <Space orientation="vertical" size={4}>
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
                {data.tags?.map((item) => (
                  <Tag key={item} bordered={false}>
                    {item}
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
                    <Alert type="warning" showIcon message="该说明因违规已被移除" description={data.descriptionRemovedReason} />
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
                        rowExpandable: (row) => !!row.releaseNotesHtml,
                        expandedRowRender: (row) => <div className="markdown-body" dangerouslySetInnerHTML={{ __html: row.releaseNotesHtml }} />,
                      }}
                      columns={[
                        { title: '版本', dataIndex: 'version', render: (value) => <Typography.Text strong>{value}</Typography.Text> },
                        { title: 'apiLevel', dataIndex: 'apiLevel', width: 92 },
                        { title: '最低宿主版本', dataIndex: 'minHostVersion', render: (value) => value ?? '—' },
                        { title: '宿主模式', dataIndex: 'hostMode', render: (value) => <Tag bordered={false}>{value}</Tag> },
                        { title: '大小', dataIndex: 'packageSize', render: formatSize },
                        { title: '签名', dataIndex: 'signature', render: (value) => <SignatureTag state={value} /> },
                        { title: '下载', dataIndex: 'downloads', width: 80 },
                        {
                          title: '',
                          width: 110,
                          render: (_, row) => (
                            <Button type="link" icon={<CloudDownloadOutlined />} onClick={() => download(row.version)}>
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
            <Space orientation="vertical" size={12} style={{ width: '100%' }}>
              <Button type="primary" size="large" block icon={<CloudDownloadOutlined />} disabled={!latest} onClick={() => latest && download(latest.version)}>
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
              <Descriptions.Item label="评分">{formatRating(data.ratingAverage, data.ratingCount)}</Descriptions.Item>
              <Descriptions.Item label="更新于">{formatDate(data.updatedAt)}</Descriptions.Item>
            </Descriptions>
          </Card>

          {latest ? (
            <Card size="small" title="完整性校验">
              <Space orientation="vertical" size={8} style={{ width: '100%' }}>
                <Alert
                  type="info"
                  showIcon
                  title="下载后可核对校验和"
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
                  <Typography.Paragraph copyable={{ text: latest.payloadSha256 }} className="market-hash">
                    {latest.payloadSha256}
                  </Typography.Paragraph>
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    整包 SHA-256
                  </Typography.Text>
                  <Typography.Paragraph copyable={{ text: latest.fileSha256 }} className="market-hash market-hash-last">
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

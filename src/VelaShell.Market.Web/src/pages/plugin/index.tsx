import { Chip, PluginIcon, SignatureTag } from '@/components';
import { getDownloadUrl, getPlugin, getRelated } from '@/services/market';
import { getMyPlugins } from '@/services/me';
import { formatDate, formatRelative, formatSize } from '@/utils/format';
import { keepResult } from '@/utils/request';
import { CloudDownloadOutlined, CopyOutlined, InfoCircleOutlined, ThunderboltFilled } from '@ant-design/icons';
import { Link, useModel, useParams, useRequest } from '@umijs/max';
import { Alert, App, Button, Result, Skeleton, Table, Tabs, Typography } from 'antd';
import ContributesBox from './components/ContributesBox';
import DetailRail from './components/DetailRail';
import ReviewSection from './components/ReviewSection';

/** 插件详情页:头部动作区 + 说明/版本/评价三页签,右侧是发丝线分区的信息栏。 */
export default function PluginDetailPage() {
  const { id = '' } = useParams<{ id: string }>();
  const { message } = App.useApp();
  const { initialState } = useModel('@@initialState');
  const signedIn = !!initialState?.currentUser;

  const { data, error } = useRequest(() => getPlugin(id), { formatResult: keepResult, refreshDeps: [id], onError: () => undefined });
  const { data: related } = useRequest(() => getRelated(id), { formatResult: keepResult, refreshDeps: [id], onError: () => undefined });

  /**
   * 是不是拥有者决定评价区的形态(自己不能评自己,但能回复别人)。
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

  const copyId = async () => {
    try {
      await navigator.clipboard.writeText(id);
      message.success('已复制插件 id');
    } catch {
      message.warning('浏览器不允许写剪贴板,请手动复制');
    }
  };

  if (error) {
    return <Result status="404" title="插件不存在" subTitle="它可能已被下架,或者链接有误。" />;
  }
  if (!data) {
    return (
      <div className="market-page">
        <Skeleton active paragraph={{ rows: 8 }} />
      </div>
    );
  }

  const latest = data.versions[0];

  return (
    <div className="market-shell">
      <nav className="detail-crumbs">
        <Link to="/">浏览</Link>
        <span>/</span>
        <span>{data.displayName}</span>
      </nav>

      <header className="detail-head">
        <div className="detail-head-left">
          <PluginIcon id={data.id} name={data.displayName} size={72} />
          <div className="detail-titles">
            <div className="detail-title-row">
              <h1>{data.displayName}</h1>
              {latest ? <span className="detail-version-pill">v{latest.version}</span> : null}
              {latest ? <SignatureTag state={latest.signature} /> : null}
              {data.isFeatured ? (
                <Chip tone="accent" icon={<ThunderboltFilled />}>
                  编辑推荐
                </Chip>
              ) : null}
            </div>

            {data.summary ? <p className="detail-summary">{data.summary}</p> : null}

            <div className="detail-meta">
              <span>{data.author ?? data.ownerName ?? data.id}</span>
              {data.license ? (
                <>
                  <span>·</span>
                  <span>{data.license}</span>
                </>
              ) : null}
              {latest ? (
                <>
                  <span>·</span>
                  <span className="mono">
                    apiLevel {latest.apiLevel}
                    {latest.minHostVersion ? ` · host ≥ ${latest.minHostVersion}` : ''}
                  </span>
                </>
              ) : null}
              <span>·</span>
              <span>{formatRelative(data.updatedAt)}更新</span>
            </div>

            {data.tags?.length ? (
              <div className="plugin-card-tags">
                {data.tags.map((item) => (
                  <Chip key={item}>{item}</Chip>
                ))}
              </div>
            ) : null}
          </div>
        </div>

        <div className="detail-head-right">
          <div style={{ display: 'flex', gap: 9 }}>
            <Button icon={<CopyOutlined />} onClick={copyId}>
              复制插件 id
            </Button>
            <Button type="primary" size="large" icon={<CloudDownloadOutlined />} disabled={!latest} onClick={() => latest && download(latest.version)}>
              下载 .vpx
            </Button>
          </div>
          {latest ? (
            <span className="mono" style={{ fontSize: 11.5, color: 'var(--ink-3)' }}>
              {formatSize(latest.packageSize)} · 已被下载 {data.downloads} 次
            </span>
          ) : null}
          {/* 安装必须走宿主 —— `vela-plugin install` 在 CLI 里是**禁用**的,
              它会绕过发布者授权与受保护安装收据。这里就照实说,不要编一条命令行。 */}
          <span className="detail-install-hint">
            <InfoCircleOutlined />在 VelaShell 的插件管理页「安装 .vpx…」选中下载好的文件即可安装
          </span>
        </div>
      </header>

      <div className="detail-body">
        <main className="detail-main">
          <Tabs
            items={[
              {
                key: 'readme',
                label: '说明',
                // 描述被审核员清空时,必须说清"这里为什么是空的" ——
                // 否则读者只会以为作者懒得写,而作者也不知道自己该改什么。
                children: (
                  <>
                    {data.descriptionRemovedReason ? (
                      <Alert type="warning" showIcon message="该说明因违规已被移除" description={data.descriptionRemovedReason} style={{ marginBottom: 16 }} />
                    ) : data.descriptionHtml ? (
                      <div className="markdown-body" dangerouslySetInnerHTML={{ __html: data.descriptionHtml }} />
                    ) : (
                      <Typography.Text type="secondary">作者还没有填写说明。</Typography.Text>
                    )}
                    <ContributesBox contributes={latest?.contributes} hostMode={latest?.hostMode} idlePolicy={latest?.idlePolicy} />
                  </>
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
                    scroll={{ x: 760 }}
                    expandable={{
                      rowExpandable: (row) => !!row.releaseNotesHtml,
                      expandedRowRender: (row) => <div className="markdown-body" dangerouslySetInnerHTML={{ __html: row.releaseNotesHtml }} />,
                    }}
                    columns={[
                      { title: '版本', dataIndex: 'version', render: (value) => <span className="mono">v{value}</span> },
                      { title: 'apiLevel', dataIndex: 'apiLevel', width: 92 },
                      { title: '最低宿主版本', dataIndex: 'minHostVersion', render: (value) => <span className="mono">{value ?? '—'}</span> },
                      { title: '宿主模式', dataIndex: 'hostMode', render: (value) => <Chip>{value === 'Isolated' ? '隔离进程' : '进程内'}</Chip> },
                      { title: '大小', dataIndex: 'packageSize', render: formatSize },
                      { title: '签名', dataIndex: 'signature', render: (value) => <SignatureTag state={value} icon={false} /> },
                      { title: '发布于', dataIndex: 'publishedAt', width: 120, render: formatDate },
                      { title: '下载', dataIndex: 'downloads', width: 80 },
                      {
                        title: '',
                        width: 100,
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
        </main>

        <DetailRail plugin={data} latest={latest} related={related} />
      </div>
    </div>
  );
}

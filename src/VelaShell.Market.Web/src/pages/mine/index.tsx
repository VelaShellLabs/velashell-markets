import { Findings, PageShell, SignatureTag, StatusTag } from '@/components';
import { STATUS_TAG, VERDICT_TAG } from '@/configs';
import { getMyUploads } from '@/services/uploads';
import { formatDateTime, formatSize } from '@/utils/format';
import { keepResult } from '@/utils/request';
import { CloudUploadOutlined, ReloadOutlined } from '@ant-design/icons';
import { history, useRequest } from '@umijs/max';
import { Alert, Button, Empty, Skeleton, Space, Table, Typography } from 'antd';

/** 汇总条里的一格。 */
function Cell({ value, label, tone }: { value: number; label: string; tone?: string }) {
  return (
    <div className="upload-summary-cell">
      <b style={tone ? { color: `var(--${tone})` } : undefined}>{value}</b>
      <span>{label}</span>
    </div>
  );
}

/** 我的上传:送检汇总 + 每次送检的结论与完整报告。被拒却看不到原因,只会换来盲目重传。 */
export default function MinePage() {
  const { data, loading, refresh } = useRequest(getMyUploads, { formatResult: keepResult, onError: () => undefined });
  const items = data ?? [];

  const published = items.filter((item) => item.status === 'Published').length;
  const rejected = items.filter((item) => item.status === 'Rejected').length;
  const pending = items.filter((item) => item.scan?.verdict === 'NeedsReview' && item.status !== 'Published').length;
  const quarantined = items.filter((item) => item.status === 'Quarantined' || item.status === 'Scanning').length;

  return (
    <PageShell
      title="我的上传"
      description="每一次送检的结论与完整报告都在这里,展开可以看到具体命中了哪些检测项。"
      extra={[
        <Button key="refresh" icon={<ReloadOutlined />} onClick={refresh}>
          刷新
        </Button>,
        <Button key="upload" type="primary" icon={<CloudUploadOutlined />} onClick={() => history.push('/upload')}>
          发布新版本
        </Button>,
      ]}
    >
      {loading && !data ? (
        <Skeleton active paragraph={{ rows: 5 }} />
      ) : items.length === 0 ? (
        <Empty description="还没有上传过插件" style={{ padding: '64px 0' }}>
          <Button type="primary" onClick={() => history.push('/upload')}>
            发布第一个
          </Button>
        </Empty>
      ) : (
        <>
          <div className="upload-summary">
            <Cell value={items.length} label="累计送检" />
            <div className="upload-summary-rule" />
            <Cell value={published} label="已发布" tone="ok" />
            <div className="upload-summary-rule" />
            <Cell value={pending} label="等待人工复核" tone="warn" />
            <div className="upload-summary-rule" />
            <Cell value={rejected} label="被拒收" tone="danger" />
            <div className="upload-summary-rule" />
            <div className="upload-summary-cell" style={{ flex: 2 }}>
              <b style={{ fontSize: 13, fontWeight: 500 }}>{quarantined > 0 ? `当前隔离区内 ${quarantined} 个包` : '隔离区里没有你的包'}</b>
              <span>{quarantined > 0 ? '检测跑完会自动更新,命中可疑项的会转人工复核' : '所有送检都已有结论'}</span>
            </div>
          </div>

          <Table<UploadsAPI.MyUpload>
            rowKey={(row) => `${row.pluginId}@${row.version}`}
            dataSource={items}
            loading={loading}
            pagination={false}
            scroll={{ x: 900 }}
            columns={[
              {
                title: '插件',
                dataIndex: 'pluginId',
                // 只有已发布的才点得进去 —— 隔离中的包还没有详情页,链接过去是个 404。
                render: (id: string, row) =>
                  row.status === 'Published' ? (
                    <a onClick={() => history.push(`/plugins/${id}`)}>{id}</a>
                  ) : (
                    <Typography.Text>{id}</Typography.Text>
                  ),
              },
              { title: '版本', dataIndex: 'version', width: 110, render: (value) => <span className="mono">v{value}</span> },
              { title: '状态', dataIndex: 'status', width: 120, render: (value: string) => <StatusTag value={value} presets={STATUS_TAG} /> },
              { title: '检测结论', width: 130, render: (_, row) => <StatusTag value={row.scan?.verdict} presets={VERDICT_TAG} /> },
              { title: '大小', dataIndex: 'packageSize', width: 100, render: formatSize },
              { title: '签名', dataIndex: 'signature', width: 120, render: (value) => <SignatureTag state={value} icon={false} /> },
              { title: '上传时间', dataIndex: 'uploadedAt', width: 180, render: formatDateTime },
            ]}
            expandable={{
              expandedRowRender: (row) => (
                <Space orientation="vertical" size={14} style={{ width: '100%' }}>
                  <div style={{ display: 'flex', gap: 36, flexWrap: 'wrap' }}>
                    <div className="upload-manifest-item">
                      <span>开始</span>
                      <b>{formatDateTime(row.scan?.startedAt)}</b>
                    </div>
                    <div className="upload-manifest-item">
                      <span>结束</span>
                      <b>{row.scan?.completedAt ? formatDateTime(row.scan.completedAt) : '进行中'}</b>
                    </div>
                    <div className="upload-manifest-item">
                      <span>引擎</span>
                      <b>
                        {row.scan?.engines
                          ? Object.entries(row.scan.engines)
                              .map(([name, version]) => `${name} ${version}`)
                              .join(' · ')
                          : '—'}
                      </b>
                    </div>
                    {row.scan?.entryCount ? (
                      <div className="upload-manifest-item">
                        <span>包内条目</span>
                        <b>{row.scan.entryCount}</b>
                      </div>
                    ) : null}
                  </div>

                  <Findings findings={row.scan?.findings} />

                  {row.scan?.verdict === 'NeedsReview' && row.status !== 'Published' ? (
                    <Alert
                      type="warning"
                      showIcon
                      message="包仍在隔离桶里,对外不可见"
                      description="审核员放行后会自动搬进正式桶;若被驳回,这里会显示原因。"
                    />
                  ) : null}
                </Space>
              ),
            }}
          />
        </>
      )}
    </PageShell>
  );
}

import { Findings, PageShell, SignatureTag, StatusTag } from '@/components';
import { STATUS_TAG, VERDICT_TAG } from '@/configs';
import { getMyUploads } from '@/services/uploads';
import { formatDateTime, formatSize } from '@/utils/format';
import { ReloadOutlined } from '@ant-design/icons';
import { keepResult } from '@/utils/request';
import { history, useRequest } from '@umijs/max';
import { Button, Card, Descriptions, Empty, Skeleton, Space, Table, Typography } from 'antd';

/** 我的上传:检测进度与完整报告。被拒却看不到原因,只会换来盲目重传。 */
export default function MinePage() {
  const { data, loading, refresh } = useRequest(getMyUploads, { formatResult: keepResult, onError: () => undefined });
  const items = data ?? [];

  return (
    <PageShell
      title="我的上传"
      description="每一次送检的结论与完整报告都在这里,展开可以看到具体命中了哪些检测项。"
      extra={[
        <Button key="refresh" icon={<ReloadOutlined />} onClick={refresh}>
          刷新
        </Button>,
        <Button key="upload" type="primary" onClick={() => history.push('/upload')}>
          发布新版本
        </Button>,
      ]}
    >
      {loading && !data ? (
        <Card>
          <Skeleton active paragraph={{ rows: 5 }} />
        </Card>
      ) : items.length === 0 ? (
        <Card>
          <Empty description="还没有上传过插件">
            <Button type="primary" onClick={() => history.push('/upload')}>
              发布第一个
            </Button>
          </Empty>
        </Card>
      ) : (
        <Card styles={{ body: { padding: 0 } }}>
          <Table<UploadsAPI.MyUpload>
            rowKey={(row) => `${row.pluginId}@${row.version}`}
            dataSource={items}
            loading={loading}
            pagination={false}
            scroll={{ x: 720 }}
            columns={[
              {
                title: '插件',
                dataIndex: 'pluginId',
                // 只有已发布的才点得进去 —— 隔离中的包还没有详情页,链接过去是个 404。
                render: (id: string, row) => (row.status === 'Published' ? <a onClick={() => history.push(`/plugins/${id}`)}>{id}</a> : <Typography.Text>{id}</Typography.Text>),
              },
              { title: '版本', dataIndex: 'version', width: 110 },
              { title: '状态', dataIndex: 'status', width: 110, render: (value: string) => <StatusTag value={value} presets={STATUS_TAG} /> },
              { title: '检测结论', width: 130, render: (_, row) => <StatusTag value={row.scan?.verdict} presets={VERDICT_TAG} /> },
              { title: '大小', dataIndex: 'packageSize', width: 100, render: formatSize },
              { title: '签名', dataIndex: 'signature', width: 110, render: (value) => <SignatureTag state={value} /> },
              { title: '上传时间', dataIndex: 'uploadedAt', render: formatDateTime },
            ]}
            expandable={{
              expandedRowRender: (row) => (
                <Space direction="vertical" size={12} style={{ width: '100%' }}>
                  <Descriptions size="small" column={{ xs: 1, sm: 2, md: 3 }} colon={false}>
                    <Descriptions.Item label="开始">{formatDateTime(row.scan?.startedAt)}</Descriptions.Item>
                    <Descriptions.Item label="结束">{row.scan?.completedAt ? formatDateTime(row.scan.completedAt) : '进行中'}</Descriptions.Item>
                    <Descriptions.Item label="引擎">
                      {row.scan?.engines
                        ? Object.entries(row.scan.engines)
                            .map(([name, version]) => `${name} ${version}`)
                            .join(' · ')
                        : '—'}
                    </Descriptions.Item>
                  </Descriptions>
                  <Findings findings={row.scan?.findings} />
                </Space>
              ),
            }}
          />
        </Card>
      )}
    </PageShell>
  );
}

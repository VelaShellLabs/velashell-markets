import { Findings, SignatureTag } from '@/components';
import { approveVersion, getQueue, rejectVersion } from '@/services/moderation';
import { formatDateTime, formatSize } from '@/utils/format';
import { ReloadOutlined } from '@ant-design/icons';
import { keepResult } from '@/utils/request';
import { useRequest } from '@umijs/max';
import { App, Button, Card, Descriptions, Empty, Skeleton, Space, Tag, Typography } from 'antd';
import { useState } from 'react';
import { askReason } from './askReason';

/**
 * 隔离队列。这里处理的是**检测判为"需人工复核"的包** —— 它们既没被拒,也绝不可下载,
 * 一直留在隔离区等一个人来看。
 */
export default function QueuePanel() {
  const api = App.useApp();
  const [busy, setBusy] = useState<string | null>(null);

  const { data, loading, refresh } = useRequest(getQueue, { formatResult: keepResult, onError: () => undefined });
  const items = data ?? [];

  const approve = async (item: ModerationAPI.PendingVersion) => {
    setBusy(item.id);
    try {
      await approveVersion(item.id);
      api.message.success(`${item.pluginId} ${item.version} 已放行`);
      refresh();
    } catch {
      // 失败信息已由统一错误处理展示。
    } finally {
      setBusy(null);
    }
  };

  const reject = (item: ModerationAPI.PendingVersion) =>
    askReason(
      api,
      {
        title: `驳回 ${item.pluginId} ${item.version}`,
        description: '包留在隔离区,原因会展示给上传者。',
        placeholder: '驳回原因(会展示给上传者)',
        okText: '驳回',
      },
      async (reason) => {
        await rejectVersion(item.id, reason);
        api.message.success('已驳回');
        refresh();
      },
    );

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }} wrap>
        <Typography.Text type="secondary">这些包命中了需要人看一眼的项,在放行之前它们一直留在隔离区、不可下载。</Typography.Text>
        <Button icon={<ReloadOutlined />} onClick={refresh} loading={loading}>
          刷新
        </Button>
      </Space>

      {loading && !data ? (
        <Card>
          <Skeleton active paragraph={{ rows: 4 }} />
        </Card>
      ) : items.length === 0 ? (
        <Card>
          <Empty description="队列是空的" style={{ padding: '32px 0' }} />
        </Card>
      ) : (
        <Space direction="vertical" size={16} style={{ width: '100%' }}>
          {items.map((item) => (
            <Card
              key={item.id}
              title={
                <Space wrap>
                  <Typography.Text strong>{item.pluginId}</Typography.Text>
                  <Tag color="blue" bordered={false}>
                    v{item.version}
                  </Tag>
                  <SignatureTag state={item.signature} />
                </Space>
              }
              extra={
                <Space>
                  <Button danger onClick={() => reject(item)}>
                    驳回
                  </Button>
                  <Button type="primary" loading={busy === item.id} onClick={() => approve(item)}>
                    放行并发布
                  </Button>
                </Space>
              }
            >
              <Descriptions size="small" column={{ xs: 1, sm: 2, md: 3 }} colon={false} style={{ marginBottom: 12 }}>
                <Descriptions.Item label="上传者">
                  <Typography.Text code style={{ fontSize: 12 }}>
                    {item.uploadedBySubject}
                  </Typography.Text>
                </Descriptions.Item>
                <Descriptions.Item label="上传时间">{formatDateTime(item.uploadedAt)}</Descriptions.Item>
                <Descriptions.Item label="大小">{formatSize(item.packageSize)}</Descriptions.Item>
              </Descriptions>
              <Findings findings={item.findings} />
            </Card>
          ))}
        </Space>
      )}
    </>
  );
}

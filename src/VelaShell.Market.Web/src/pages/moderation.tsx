import { useEffect, useState } from 'react';
import { App, Button, Card, Descriptions, Empty, Input, Modal, Result, Skeleton, Space, Tag, Typography } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import { api, apiSend } from '../auth';
import Findings, { type Finding } from '../components/Findings';

type Pending = {
  id: string;
  pluginId: string;
  version: string;
  uploadedBySubject: string;
  uploadedAt: string;
  packageSize: number;
  signature: string;
  findings: Finding[];
};

/**
 * 审核台。这里处理的是**检测判为"需人工复核"的包** —— 它们既没被拒,也绝不可下载,
 * 一直留在隔离区等一个人来看。
 */
export default function ModerationPage() {
  const { message, modal } = App.useApp();
  const [items, setItems] = useState<Pending[] | null>(null);
  const [denied, setDenied] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);

  const load = async () => {
    const response = await api('/moderation/queue');
    if (response.status === 401 || response.status === 403) {
      setDenied(true);
      return;
    }
    setItems(await response.json());
  };

  useEffect(() => {
    load();
  }, []);

  const approve = async (item: Pending) => {
    setBusy(item.id);
    try {
      const response = await api(`/moderation/versions/${item.id}/approve`, { method: 'POST' });
      if (response.ok) {
        message.success(`${item.pluginId} ${item.version} 已放行`);
        await load();
      } else {
        const payload = await response.json().catch(() => ({}));
        message.error(payload.error ?? payload.detail ?? '放行失败');
      }
    } finally {
      setBusy(null);
    }
  };

  const reject = (item: Pending) => {
    let reason = '';
    modal.confirm({
      title: `驳回 ${item.pluginId} ${item.version}`,
      // 原因是必填的:不给原因的驳回等于让作者盲目重传。
      content: (
        <Input.TextArea
          rows={3}
          placeholder="驳回原因(会展示给上传者)"
          onChange={(e) => (reason = e.target.value)}
        />
      ),
      okText: '驳回',
      okButtonProps: { danger: true },
      cancelText: '取消',
      onOk: async () => {
        if (!reason.trim()) {
          message.warning('请填写驳回原因');
          throw new Error('reason required');
        }
        const response = await apiSend(`/moderation/versions/${item.id}/reject`, 'POST', { reason });
        if (!response.ok) {
          message.error('驳回失败');
          throw new Error('reject failed');
        }
        message.success('已驳回');
        await load();
      },
    });
  };

  if (denied) {
    return <Result status="403" title="没有审核权限" subTitle="审核员名单由部署方在 Auth:ModeratorSubjects 中配置。" />;
  }

  return (
    <div className="market-page">
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 20 }}>
        <Space direction="vertical" size={0}>
          <Typography.Title level={3} style={{ margin: 0 }}>审核台</Typography.Title>
          <Typography.Text type="secondary">
            这些包命中了需要人看一眼的项,在放行之前它们一直留在隔离区、不可下载。
          </Typography.Text>
        </Space>
        <Button icon={<ReloadOutlined />} onClick={load}>刷新</Button>
      </Space>

      {items === null ? (
        <Card><Skeleton active paragraph={{ rows: 4 }} /></Card>
      ) : items.length === 0 ? (
        <Card><Empty description="队列是空的" style={{ padding: '32px 0' }} /></Card>
      ) : (
        <Space direction="vertical" size={16} style={{ width: '100%' }}>
          {items.map((item) => (
            <Card
              key={item.id}
              title={
                <Space wrap>
                  <Typography.Text strong>{item.pluginId}</Typography.Text>
                  <Tag color="blue" bordered={false}>v{item.version}</Tag>
                  <Tag bordered={false}>{item.signature}</Tag>
                </Space>
              }
              extra={
                <Space>
                  <Button danger onClick={() => reject(item)}>驳回</Button>
                  <Button type="primary" loading={busy === item.id} onClick={() => approve(item)}>
                    放行并发布
                  </Button>
                </Space>
              }
            >
              <Descriptions size="small" column={{ xs: 1, sm: 2, md: 3 }} colon={false} style={{ marginBottom: 12 }}>
                <Descriptions.Item label="上传者">
                  <Typography.Text code style={{ fontSize: 12 }}>{item.uploadedBySubject}</Typography.Text>
                </Descriptions.Item>
                <Descriptions.Item label="上传时间">{new Date(item.uploadedAt).toLocaleString()}</Descriptions.Item>
                <Descriptions.Item label="大小">{(item.packageSize / 1024 / 1024).toFixed(2)} MB</Descriptions.Item>
              </Descriptions>
              <Findings findings={item.findings} />
            </Card>
          ))}
        </Space>
      )}
    </div>
  );
}

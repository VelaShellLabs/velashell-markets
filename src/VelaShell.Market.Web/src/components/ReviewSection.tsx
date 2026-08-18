import { useEffect, useState } from 'react';
import { App, Avatar, Button, Card, Empty, Form, Input, List, Pagination, Popconfirm, Rate, Space, Tag, Typography } from 'antd';
import { UserOutlined } from '@ant-design/icons';
import { api, apiSend, getUser } from '../auth';

type Review = {
  displayName?: string;
  rating: number;
  bodyHtml: string;
  pluginVersion?: string;
  createdAt: string;
  updatedAt: string;
};

type MyReview = { rating: number; body?: string; updatedAt: string };

const SIZE = 10;

/**
 * 评价区。三种状态各有各的界面:未登录(提示去登录)、还没评价过(空表单)、
 * 已评价过(表单预填成"修改",并给出删除)。
 *
 * 作者不能评价自己的插件,这条由服务端 403 兜底;这里同样把表单藏掉,
 * 免得让人填完才被拒。
 */
export default function ReviewSection({ pluginId, isOwner }: { pluginId: string; isOwner: boolean }) {
  const { message } = App.useApp();
  const [form] = Form.useForm();
  const [items, setItems] = useState<Review[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [signedIn, setSignedIn] = useState(false);
  const [mine, setMine] = useState<MyReview | null>(null);
  const [saving, setSaving] = useState(false);

  const loadList = () =>
    api(`/plugins/${pluginId}/reviews?page=${page}&size=${SIZE}`)
      .then((r) => r.json())
      .then((d) => {
        setItems(d.items ?? []);
        setTotal(d.total ?? 0);
      });

  const loadMine = async () => {
    const user = await getUser();
    setSignedIn(!!user);
    if (!user) return;
    const response = await api(`/plugins/${pluginId}/reviews/mine`);
    if (response.status === 204) {
      setMine(null);
      form.resetFields();
      return;
    }
    if (response.ok) {
      const data: MyReview = await response.json();
      setMine(data);
      form.setFieldsValue({ rating: data.rating, body: data.body });
    }
  };

  useEffect(() => {
    loadList();
  }, [pluginId, page]);

  useEffect(() => {
    loadMine();
  }, [pluginId]);

  const submit = async (values: { rating: number; body?: string }) => {
    setSaving(true);
    try {
      const response = await apiSend(`/plugins/${pluginId}/reviews`, 'PUT', {
        rating: values.rating,
        body: values.body ?? null,
      });
      if (!response.ok) {
        const payload = await response.json().catch(() => ({}));
        message.error(payload.error ?? payload.detail ?? '提交失败');
        return;
      }
      message.success(mine ? '评价已更新' : '感谢你的评价');
      await Promise.all([loadList(), loadMine()]);
    } finally {
      setSaving(false);
    }
  };

  const remove = async () => {
    const response = await api(`/plugins/${pluginId}/reviews`, { method: 'DELETE' });
    if (response.ok) {
      message.success('评价已删除');
      setMine(null);
      form.resetFields();
      await loadList();
    }
  };

  return (
    <Space direction="vertical" size={16} style={{ width: '100%' }}>
      {isOwner ? (
        <Card size="small">
          <Typography.Text type="secondary">这是你发布的插件,不能自评。</Typography.Text>
        </Card>
      ) : signedIn ? (
        <Card size="small" title={mine ? '修改我的评价' : '写一条评价'}>
          <Form form={form} layout="vertical" onFinish={submit}>
            <Form.Item name="rating" label="评分" rules={[{ required: true, message: '请先打分' }]}>
              <Rate />
            </Form.Item>
            <Form.Item name="body" label="说点什么(支持 Markdown,可留空)">
              <Input.TextArea rows={4} maxLength={5000} showCount placeholder="它解决了什么问题?哪里好用、哪里别扭?" />
            </Form.Item>
            <Space>
              <Button type="primary" htmlType="submit" loading={saving}>
                {mine ? '更新评价' : '发表评价'}
              </Button>
              {mine ? (
                <Popconfirm title="删除我的评价?" onConfirm={remove}>
                  <Button danger type="text">删除</Button>
                </Popconfirm>
              ) : null}
            </Space>
          </Form>
        </Card>
      ) : (
        <Card size="small">
          <Typography.Text type="secondary">登录后可以发表评价。</Typography.Text>
        </Card>
      )}

      {items.length === 0 ? (
        <Empty description="还没有评价" style={{ padding: '32px 0' }} />
      ) : (
        <>
          <List
            itemLayout="vertical"
            dataSource={items}
            renderItem={(r) => (
              <List.Item key={`${r.displayName}-${r.createdAt}`}>
                <List.Item.Meta
                  avatar={<Avatar icon={<UserOutlined />} />}
                  title={
                    <Space>
                      <span>{r.displayName ?? '匿名用户'}</span>
                      <Rate disabled value={r.rating} style={{ fontSize: 12 }} />
                      {r.pluginVersion ? <Tag bordered={false}>v{r.pluginVersion}</Tag> : null}
                    </Space>
                  }
                  description={
                    <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                      {new Date(r.updatedAt).toLocaleString()}
                      {r.updatedAt !== r.createdAt ? '(已编辑)' : ''}
                    </Typography.Text>
                  }
                />
                {r.bodyHtml ? (
                  <div className="markdown-body" dangerouslySetInnerHTML={{ __html: r.bodyHtml }} />
                ) : null}
              </List.Item>
            )}
          />
          <Pagination
            align="center"
            current={page}
            pageSize={SIZE}
            total={total}
            showSizeChanger={false}
            onChange={setPage}
          />
        </>
      )}
    </Space>
  );
}

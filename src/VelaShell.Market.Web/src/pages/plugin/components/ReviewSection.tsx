import { deleteReview, getMyReview, listReviews, upsertReview } from '@/services/reviews';
import { EditOutlined, UserOutlined } from '@ant-design/icons';
import { useModel } from '@umijs/max';
import {
  App, Avatar, Button, Card, Divider, Empty, Form, Input, List, Pagination, Popconfirm, Rate, Space, Tag, Typography,
} from 'antd';
import { useEffect, useState } from 'react';
import { login } from '@/utils/auth';

const SIZE = 10;

/** 应用商店式评分汇总:左边大均分与总数,右边 5→1 星分布条。 */
function RatingSummary({ distribution }: { distribution: Record<string, number> }) {
  const counts = [5, 4, 3, 2, 1].map((star) => ({ star, count: distribution[String(star)] ?? 0 }));
  const total = counts.reduce((sum, c) => sum + c.count, 0);
  if (total === 0) return null;
  const average = counts.reduce((sum, c) => sum + c.star * c.count, 0) / total;
  const max = Math.max(...counts.map((c) => c.count));
  return (
    <div className="review-summary">
      <div className="review-summary-score">
        <div className="review-summary-score-value">{average.toFixed(1)}</div>
        <Rate disabled allowHalf value={Math.round(average * 2) / 2} style={{ fontSize: 14 }} />
        <Typography.Paragraph type="secondary" style={{ fontSize: 12, marginBottom: 0 }}>
          共 {total} 条评价
        </Typography.Paragraph>
      </div>
      <div className="review-summary-bars">
        {counts.map(({ star, count }) => (
          <div className="review-bar-row" key={star}>
            <span>{star} 星</span>
            <div className="review-bar-track">
              <div className="review-bar-fill" style={{ width: max > 0 ? `${(count / max) * 100}%` : 0 }} />
            </div>
            <span className="review-bar-count">{count}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

/**
 * 评价区。几种状态各有各的界面:未登录(提示去登录)、还没评价过(空表单)、
 * 已评价过(**渲染成一张"我的评价"卡片**,点「编辑」才回到表单 ——
 * 把已发表的内容一直泡在输入框里,看起来像"没发出去")。
 *
 * 作者不能评价自己的插件,这条由服务端 403 兜底;这里同样把表单藏掉,
 * 免得让人填完才被拒。
 */
export default function ReviewSection({ pluginId, isOwner }: { pluginId: string; isOwner: boolean }) {
  const { message } = App.useApp();
  const { initialState } = useModel('@@initialState');
  const signedIn = !!initialState?.currentUser;
  const [form] = Form.useForm();
  const [items, setItems] = useState<MarketAPI.Review[]>([]);
  const [total, setTotal] = useState(0);
  const [distribution, setDistribution] = useState<Record<string, number>>({});
  const [page, setPage] = useState(1);
  const [mine, setMine] = useState<MarketAPI.MyReview | null>(null);
  const [editing, setEditing] = useState(false);
  const [saving, setSaving] = useState(false);

  const loadList = () =>
    listReviews(pluginId, { page, size: SIZE }).then((d) => {
      setItems(d.items ?? []);
      setTotal(d.total ?? 0);
      setDistribution(d.distribution ?? {});
    });

  const loadMine = async () => {
    if (!signedIn) return;
    try {
      const data = await getMyReview(pluginId);
      setMine(data);
      if (data) {
        form.setFieldsValue({ rating: data.rating, body: data.body });
      } else {
        form.resetFields();
      }
    } catch {
      setMine(null);
    }
  };

  useEffect(() => {
    loadList();
  }, [pluginId, page]);

  useEffect(() => {
    loadMine();
  }, [pluginId, signedIn]);

  const submit = async (values: { rating: number; body?: string }) => {
    setSaving(true);
    try {
      await upsertReview(pluginId, { rating: values.rating, body: values.body ?? null });
      message.success(mine ? '评价已更新' : '感谢你的评价');
      setEditing(false);
      await Promise.all([loadList(), loadMine()]);
    } catch {
      // 失败信息已由统一错误处理展示。
    } finally {
      setSaving(false);
    }
  };

  const remove = async () => {
    await deleteReview(pluginId);
    message.success('评价已删除');
    setMine(null);
    setEditing(false);
    form.resetFields();
    await loadList();
  };

  const showForm = signedIn && !isOwner && (!mine || editing);

  return (
    <Space direction="vertical" size={16} style={{ width: '100%' }}>
      {total > 0 ? (
        <Card size="small">
          <RatingSummary distribution={distribution} />
        </Card>
      ) : null}

      {isOwner ? (
        <Card size="small">
          <Typography.Text type="secondary">这是你发布的插件,不能自评。</Typography.Text>
        </Card>
      ) : !signedIn ? (
        <Card size="small">
          <Space>
            <Typography.Text type="secondary">登录后可以发表评价。</Typography.Text>
            <Button type="link" style={{ padding: 0 }} onClick={() => login()}>
              去登录
            </Button>
          </Space>
        </Card>
      ) : mine && !editing ? (
        // 已评价:像其他商店一样,把我的评价渲染出来置顶,而不是塞回输入框。
        <Card
          size="small"
          title="我的评价"
          extra={
            <Space>
              <Button type="text" icon={<EditOutlined />} onClick={() => setEditing(true)}>
                编辑
              </Button>
              <Popconfirm title="删除我的评价?" onConfirm={remove}>
                <Button danger type="text">
                  删除
                </Button>
              </Popconfirm>
            </Space>
          }
        >
          <Space direction="vertical" size={2} style={{ width: '100%' }}>
            <Space>
              <Rate disabled value={mine.rating} style={{ fontSize: 14 }} />
              <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                {new Date(mine.updatedAt).toLocaleString()}
              </Typography.Text>
            </Space>
            {mine.body ? <p className="my-review-body">{mine.body}</p> : null}
          </Space>
        </Card>
      ) : null}

      {showForm ? (
        <Card size="small" title={mine ? '修改我的评价' : '写一条评价'}>
          <Form form={form} layout="vertical" onFinish={submit}>
            <Form.Item name="rating" label="评分" rules={[{ required: true, message: '请先打分' }]}>
              <Rate />
            </Form.Item>
            <Form.Item name="body" label="说点什么(支持 Markdown,可留空)">
              <Input.TextArea
                rows={4}
                maxLength={5000}
                showCount
                placeholder="它解决了什么问题?哪里好用、哪里别扭?"
              />
            </Form.Item>
            <Space>
              <Button type="primary" htmlType="submit" loading={saving}>
                {mine ? '更新评价' : '发表评价'}
              </Button>
              {editing ? <Button onClick={() => setEditing(false)}>取消</Button> : null}
            </Space>
          </Form>
        </Card>
      ) : null}

      <Divider style={{ margin: '4px 0' }}>全部评价</Divider>

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
                  avatar={<Avatar icon={<UserOutlined />} style={{ background: '#c7d2fe', color: '#4f46e5' }} />}
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

import { Chip } from '@/components';
import { REVIEW_PAGE_SIZE } from '@/configs';
import { deleteReview, deleteReviewReply, getMyReview, listReviews, replyToReview, upsertReview } from '@/services/reviews';
import { login } from '@/utils/auth';
import { formatDateTime } from '@/utils/format';
import { keepResult } from '@/utils/request';
import { CommentOutlined, DeleteOutlined, EditOutlined, EnterOutlined, UserOutlined } from '@ant-design/icons';
import { useModel, useRequest } from '@umijs/max';
import { App, Avatar, Button, Empty, Input, Pagination, Popconfirm, Rate, Segmented, Space, Typography } from 'antd';
import { useState } from 'react';

const SORTS = [
  { label: '最新', value: 'recent' },
  { label: '低分优先', value: 'lowest' },
  { label: '高分优先', value: 'highest' },
];

/** 应用商店式评分汇总:左边大均分,中间 5→1 星分布条,右边一句"评价来自谁"。 */
function RatingSummary({ distribution }: { distribution: Record<string, number> }) {
  const counts = [5, 4, 3, 2, 1].map((star) => ({ star, count: distribution[String(star)] ?? 0 }));
  const total = counts.reduce((sum, item) => sum + item.count, 0);
  if (total === 0) return null;

  const average = counts.reduce((sum, item) => sum + item.star * item.count, 0) / total;

  return (
    <div className="review-summary">
      <div className="review-summary-score">
        <div className="review-summary-score-value">{average.toFixed(1)}</div>
        <Rate disabled allowHalf value={Math.round(average * 2) / 2} style={{ fontSize: 16 }} />
        <div style={{ fontSize: 12.5, color: 'var(--ink-3)', marginTop: 6 }}>{total} 条评价</div>
      </div>

      <div className="review-summary-rule" />

      <div className="review-summary-bars">
        {counts.map(({ star, count }) => (
          <div className="review-bar-row" key={star}>
            <span className="review-bar-label">{star} 星</span>
            <div className="review-bar-track">
              {/* 按占总数的比例而不是按"最高那根"归一:后者会把 1 条差评画得和 30 条好评一样长。 */}
              <div className="review-bar-fill" style={{ width: `${(count / total) * 100}%` }} />
            </div>
            <span className="review-bar-count">{count}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

/** 一条评价 + 作者回复。作者能回复,但删不掉别人说过的话。 */
function ReviewItem({ review, canReply, onReply, onDeleteReply }: { review: ReviewsAPI.Review; canReply: boolean; onReply: (id: string, body: string) => Promise<void>; onDeleteReply: (id: string) => Promise<void> }) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(review.authorReply ?? '');
  const [busy, setBusy] = useState(false);

  const submit = async () => {
    if (!draft.trim()) return;
    setBusy(true);
    try {
      await onReply(review.id, draft.trim());
      setEditing(false);
    } finally {
      setBusy(false);
    }
  };

  return (
    <article className="review-item">
      <div className="review-item-head">
        <div className="review-item-who">
          <Avatar size={34} icon={<UserOutlined />} style={{ background: 'var(--surface-alt)', color: 'var(--ink-2)' }} />
          <div>
            <div className="review-item-name">
              {review.displayName ?? '匿名用户'}
              {review.pluginVersion ? <Chip>v{review.pluginVersion}</Chip> : null}
            </div>
            <div className="review-item-when">
              <Rate disabled value={review.rating} style={{ fontSize: 12 }} />
              {formatDateTime(review.updatedAt)}
              {review.updatedAt !== review.createdAt ? '(已编辑)' : ''}
            </div>
          </div>
        </div>

        {canReply && !editing ? (
          <Space size={4}>
            <Button type="text" size="small" icon={review.authorReplyHtml ? <EditOutlined /> : <CommentOutlined />} onClick={() => setEditing(true)}>
              {review.authorReplyHtml ? '编辑回复' : '回复'}
            </Button>
            {review.authorReplyHtml ? (
              <Popconfirm title="撤下这条回复?" onConfirm={() => onDeleteReply(review.id)}>
                <Button type="text" size="small" danger icon={<DeleteOutlined />} />
              </Popconfirm>
            ) : null}
          </Space>
        ) : null}
      </div>

      {review.bodyHtml ? <div className="review-body markdown-body" dangerouslySetInnerHTML={{ __html: review.bodyHtml }} /> : null}

      {review.authorReplyHtml && !editing ? (
        <div className="review-reply">
          <EnterOutlined style={{ color: 'var(--ink-3)', transform: 'scaleX(-1)' }} />
          <div style={{ flex: 1, minWidth: 0 }}>
            <div className="review-reply-head">
              作者回复
              <Chip tone="accent">作者</Chip>
              <span className="mono" style={{ fontWeight: 400, fontSize: 11, color: 'var(--ink-3)' }}>
                {formatDateTime(review.authorReplyAt)}
              </span>
            </div>
            <div className="markdown-body" dangerouslySetInnerHTML={{ __html: review.authorReplyHtml }} />
          </div>
        </div>
      ) : null}

      {editing ? (
        <div style={{ marginTop: 12 }}>
          <Input.TextArea rows={3} value={draft} maxLength={2000} showCount onChange={(event) => setDraft(event.target.value)} placeholder="解释一下,或者说说下个版本会怎么改。回复会公开显示在这条评价下面。" />
          <Space style={{ marginTop: 10 }}>
            <Button type="primary" size="small" loading={busy} onClick={submit}>
              发表回复
            </Button>
            <Button size="small" onClick={() => setEditing(false)}>
              取消
            </Button>
          </Space>
        </div>
      ) : null}
    </article>
  );
}

/**
 * 评价区。几种状态各有各的界面:未登录(提示去登录)、还没评价过(输入框)、
 * 已评价过(**渲染成一张"我的评价"**,点「编辑」才回到表单 ——
 * 把已发表的内容一直泡在输入框里,看起来像"没发出去")。
 *
 * 作者不能评价自己的插件(服务端 403 兜底),但**能回复每一条评价** ——
 * 这是作者面对差评时唯一的正当出口;没有它,唯一的出口就是去找审核员要求隐藏。
 */
export default function ReviewSection({ pluginId, isOwner }: { pluginId: string; isOwner: boolean }) {
  const { message } = App.useApp();
  const { initialState } = useModel('@@initialState');
  const signedIn = !!initialState?.currentUser;

  const [page, setPage] = useState(1);
  const [sort, setSort] = useState('recent');
  const [editing, setEditing] = useState(false);
  const [rating, setRating] = useState(0);
  const [body, setBody] = useState('');

  const {
    data: reviews,
    refresh: refreshList,
    loading,
  } = useRequest(() => listReviews(pluginId, { page, size: REVIEW_PAGE_SIZE, sort }), {
    formatResult: keepResult,
    refreshDeps: [pluginId, page, sort],
    onError: () => undefined,
  });

  /**
   * 我的那条评价。匿名访客与插件作者都不必问 —— 前者拿不到,后者根本不能评。
   * 拉回来后顺手把表单填上,点「编辑」时不用再等一次请求。
   */
  const {
    data: mine,
    refresh: refreshMine,
    mutate: setMine,
  } = useRequest(() => getMyReview(pluginId), {
    formatResult: keepResult,
    ready: signedIn && !isOwner,
    refreshDeps: [pluginId],
    onSuccess: (data) => {
      setRating(data?.rating ?? 0);
      setBody(data?.body ?? '');
    },
    onError: () => undefined,
  });

  const { run: submit, loading: saving } = useRequest(() => upsertReview(pluginId, { rating, body: body || null }), {
    manual: true,
    onSuccess: () => {
      message.success(mine ? '评价已更新' : '感谢你的评价');
      setEditing(false);
      refreshList();
      refreshMine();
    },
    // 失败信息已由统一错误处理展示。
    onError: () => undefined,
  });

  const remove = async () => {
    await deleteReview(pluginId);
    message.success('评价已删除');
    setMine(null);
    setEditing(false);
    setRating(0);
    setBody('');
    refreshList();
  };

  const reply = async (reviewId: string, text: string) => {
    await replyToReview(pluginId, reviewId, text);
    message.success('回复已发表');
    refreshList();
  };

  const removeReply = async (reviewId: string) => {
    await deleteReviewReply(pluginId, reviewId);
    message.success('回复已撤下');
    refreshList();
  };

  const total = reviews?.total ?? 0;
  const items = reviews?.items ?? [];
  const showCompose = signedIn && !isOwner && (!mine || editing);

  return (
    <Space orientation="vertical" size={18} style={{ width: '100%' }}>
      <RatingSummary distribution={reviews?.distribution ?? {}} />

      {isOwner ? (
        <div className="review-compose" style={{ borderColor: 'var(--hairline)' }}>
          <Typography.Text type="secondary">这是你发布的插件,不能自评 —— 但可以回复下面每一条评价。</Typography.Text>
        </div>
      ) : !signedIn ? (
        <div className="review-compose" style={{ borderColor: 'var(--hairline)' }}>
          <Space>
            <Typography.Text type="secondary">登录后可以发表评价。浏览和搜索从来不需要登录。</Typography.Text>
            <Button type="link" style={{ padding: 0 }} onClick={() => login()}>
              去登录
            </Button>
          </Space>
        </div>
      ) : mine && !editing ? (
        // 已评价:像其他商店一样,把我的评价渲染出来置顶,而不是塞回输入框。
        <div className="review-compose">
          <div className="review-compose-head">
            <h4>我的评价</h4>
            <Space>
              <Rate disabled value={mine.rating} style={{ fontSize: 16 }} />
              <Button type="text" size="small" icon={<EditOutlined />} onClick={() => setEditing(true)}>
                编辑
              </Button>
              <Popconfirm title="删除我的评价?" onConfirm={remove}>
                <Button type="text" size="small" danger>
                  删除
                </Button>
              </Popconfirm>
            </Space>
          </div>
          {mine.body ? <p className="review-body" style={{ whiteSpace: 'pre-wrap' }}>{mine.body}</p> : null}
          <div style={{ fontSize: 11.5, color: 'var(--ink-3)', marginTop: 8 }}>{formatDateTime(mine.updatedAt)}</div>
        </div>
      ) : null}

      {showCompose ? (
        <div className="review-compose">
          <div className="review-compose-head">
            <h4>{mine ? '修改我的评价' : '写下你的评价'}</h4>
            <Space>
              <Rate value={rating} onChange={setRating} style={{ fontSize: 20 }} />
              <span style={{ fontSize: 12.5, color: 'var(--ink-2)' }}>{rating > 0 ? `${rating} 星` : '先打个分'}</span>
            </Space>
          </div>

          <Input.TextArea rows={3} value={body} maxLength={5000} showCount onChange={(event) => setBody(event.target.value)} placeholder="说说它解决了你什么问题、在什么场景下不好用。别人靠这段话决定要不要装。" />

          <div className="mod-decision-foot" style={{ marginTop: 14 }}>
            <span style={{ fontSize: 11.5, color: 'var(--ink-3)' }}>评价会显示你的显示名。作者能看到并公开回复,但删不掉;违规内容由审核员处理。</span>
            <Space>
              {editing ? <Button onClick={() => setEditing(false)}>取消</Button> : null}
              <Button type="primary" loading={saving} disabled={rating < 1} onClick={() => submit()}>
                {mine ? '更新评价' : '发表评价'}
              </Button>
            </Space>
          </div>
        </div>
      ) : null}

      <div className="mod-section-title" style={{ marginBottom: 0 }}>
        <span style={{ fontSize: 14.5, fontWeight: 600, color: 'var(--ink)' }}>全部评价 ({total})</span>
        <Segmented size="small" value={sort} options={SORTS} onChange={(value) => { setPage(1); setSort(value as string); }} />
      </div>

      {loading && !reviews ? (
        <Skeletonish />
      ) : items.length === 0 ? (
        <Empty description="还没有评价" style={{ padding: '32px 0' }} />
      ) : (
        <>
          <div>
            {items.map((review) => (
              <ReviewItem key={review.id} review={review} canReply={isOwner} onReply={reply} onDeleteReply={removeReply} />
            ))}
          </div>
          {total > REVIEW_PAGE_SIZE ? <Pagination align="center" current={page} pageSize={REVIEW_PAGE_SIZE} total={total} showSizeChanger={false} onChange={setPage} /> : null}
        </>
      )}
    </Space>
  );
}

/** 评价列表的占位。用两条空评价的骨架,比一个转圈更接近最终形状。 */
function Skeletonish() {
  return (
    <div>
      {[0, 1].map((index) => (
        <div className="review-item" key={index}>
          <div className="review-item-who">
            <Avatar size={34} style={{ background: 'var(--surface-alt)' }} />
            <div style={{ flex: 1 }}>
              <div style={{ width: 120, height: 12, borderRadius: 4, background: 'var(--surface-alt)', marginBottom: 8 }} />
              <div style={{ width: 220, height: 10, borderRadius: 4, background: 'var(--surface-alt)' }} />
            </div>
          </div>
        </div>
      ))}
    </div>
  );
}

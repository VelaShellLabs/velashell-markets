import { request } from '@umijs/max';

/** 待人工复核的队列。401/403 由页面自行展示"没有权限",跳过统一错误提示。 */
export async function getQueue() {
  return request<MarketAPI.PendingVersion[]>('/api/moderation/queue', {
    method: 'GET',
    skipErrorHandler: true,
  });
}

/** 放行并发布。 */
export async function approveVersion(id: string) {
  return request(`/api/moderation/versions/${id}/approve`, { method: 'POST' });
}

/** 驳回。原因必填 —— 不给原因的驳回等于让作者盲目重传。 */
export async function rejectVersion(id: string, reason: string) {
  return request(`/api/moderation/versions/${id}/reject`, { method: 'POST', data: { reason } });
}

// ---- 插件治理 --------------------------------------------------------------

/** 插件治理列表。unlisted 留空表示上架/下架的都要。 */
export async function getModeratedPlugins(params: {
  q?: string;
  unlisted?: boolean;
  page?: number;
  size?: number;
}) {
  return request<MarketAPI.ModeratedPluginPage>('/api/moderation/plugins', {
    method: 'GET',
    params,
    skipErrorHandler: true,
  });
}

/** 软下架:从检索里消失,但**包仍可下载**。 */
export async function unlistPlugin(id: string, reason: string) {
  return request(`/api/moderation/plugins/${id}/unlist`, { method: 'POST', data: { reason } });
}

/** 恢复上架。强制下架过的插件恢复出来是空壳,作者需要重新发版。 */
export async function relistPlugin(id: string) {
  return request(`/api/moderation/plugins/${id}/relist`, { method: 'POST' });
}

/** 强制下架:删掉正式桶里的全部已发布版本。不可逆。 */
export async function takedownPlugin(id: string, reason: string) {
  return request<MarketAPI.TakedownResult>(`/api/moderation/plugins/${id}/takedown`, {
    method: 'POST',
    data: { reason },
  });
}

/** 清空违规描述,插件本身继续可用。 */
export async function clearPluginDescription(id: string, reason: string) {
  return request(`/api/moderation/plugins/${id}/clear-description`, {
    method: 'POST',
    data: { reason },
  });
}

// ---- 评价治理 --------------------------------------------------------------

/** 评价治理列表。hidden 留空表示显示与隐藏的都要。 */
export async function getModeratedReviews(params: {
  pluginId?: string;
  hidden?: boolean;
  q?: string;
  page?: number;
  size?: number;
}) {
  return request<MarketAPI.ModeratedReviewPage>('/api/moderation/reviews', {
    method: 'GET',
    params,
    skipErrorHandler: true,
  });
}

/** 隐藏违规评价(可撤销,不计入评分)。日常违规用这个。 */
export async function hideReview(id: string, reason: string) {
  return request(`/api/moderation/reviews/${id}/hide`, { method: 'POST', data: { reason } });
}

/** 取消隐藏。 */
export async function unhideReview(id: string) {
  return request(`/api/moderation/reviews/${id}/unhide`, { method: 'POST' });
}

/** 彻底删除评价:正文从库里消失,不可逆。政治敏感等必须清除的内容才用它。 */
export async function purgeReview(id: string, reason: string) {
  return request(`/api/moderation/reviews/${id}/purge`, { method: 'POST', data: { reason } });
}

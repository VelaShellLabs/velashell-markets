import { request } from '@umijs/max';

/** 待人工复核的队列。401/403 由页面自行展示"没有权限",跳过统一错误提示。 */
export async function getQueue() {
  return request<ModerationAPI.PendingVersion[]>('/api/moderation/queue', { method: 'GET', skipErrorHandler: true });
}

/** 隔离区里那个包的包内清单(文件名、大小、可疑标记)。 */
export async function getPackageEntries(id: string) {
  return request<ModerationAPI.PackageEntries>(`/api/moderation/versions/${id}/entries`, { method: 'GET', skipErrorHandler: true });
}

/**
 * 下载隔离区里的样本。
 *
 * 走 `responseType: 'blob'` 而不是给一个 `<a href>` —— 这个端点是**由 API 转发字节流**的
 * (隔离桶永远不该有对外可访问的地址),每次读取都要带审核员自己的 Bearer 令牌,
 * 而普通超链接发不出这个头。拿到 blob 后在前端造一个临时 URL 触发下载。
 */
export async function downloadSample(id: string, fileName: string) {
  const blob = await request<Blob>(`/api/moderation/versions/${id}/sample`, { method: 'GET', responseType: 'blob' });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  // 立刻回收:blob URL 会一直把整个包钉在内存里,直到页面关掉。
  URL.revokeObjectURL(url);
}

/**
 * 放行并发布。原因可空 —— 放行不是对作者的处置,不强制解释;
 * 但填了就一起记进检测报告与服务端日志:事后要能回答的是"当时为什么判断这个可疑项没问题"。
 */
export async function approveVersion(id: string, reason?: string) {
  return request(`/api/moderation/versions/${id}/approve`, { method: 'POST', data: { reason: reason ?? '' } });
}

/** 驳回。原因必填 —— 不给原因的驳回等于让作者盲目重传。 */
export async function rejectVersion(id: string, reason: string) {
  return request(`/api/moderation/versions/${id}/reject`, { method: 'POST', data: { reason } });
}

// ---- 插件治理 --------------------------------------------------------------

/** 插件治理列表。unlisted 留空表示上架/下架的都要。 */
export async function getModeratedPlugins(params: ModerationAPI.PluginQuery) {
  return request<ModerationAPI.ModeratedPluginPage>('/api/moderation/plugins', { method: 'GET', params, skipErrorHandler: true });
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
  return request<ModerationAPI.TakedownResult>(`/api/moderation/plugins/${id}/takedown`, { method: 'POST', data: { reason } });
}

/** 清空违规描述,插件本身继续可用。 */
export async function clearPluginDescription(id: string, reason: string) {
  return request(`/api/moderation/plugins/${id}/clear-description`, { method: 'POST', data: { reason } });
}

/**
 * 设为 / 取消「编辑推荐」(浏览页首屏那张双宽卡片)。
 *
 * 不要求填原因:推荐是加分动作,作者不会因为被推荐而需要一个解释 ——
 * 「必须填原因」那条约束是给**处置**用的,套到所有动作上只会让人学会随手打个 "ok"。
 */
export async function setPluginFeatured(id: string, featured: boolean) {
  return request(`/api/moderation/plugins/${id}/${featured ? 'feature' : 'unfeature'}`, { method: 'POST' });
}

// ---- 评价治理 --------------------------------------------------------------

/** 评价治理列表。hidden 留空表示显示与隐藏的都要。 */
export async function getModeratedReviews(params: ModerationAPI.ReviewQuery) {
  return request<ModerationAPI.ModeratedReviewPage>('/api/moderation/reviews', { method: 'GET', params, skipErrorHandler: true });
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

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

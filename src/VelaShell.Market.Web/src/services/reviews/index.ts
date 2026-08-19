import { request } from '@umijs/max';

/** 某插件的评价列表(带各星级分布)。 */
export async function listReviews(pluginId: string, params: { page: number; size: number }) {
  return request<MarketAPI.ReviewPage>(`/api/plugins/${pluginId}/reviews`, {
    method: 'GET',
    params,
  });
}

/** 我对该插件的评价;没有评价过时服务端回 204,这里归一成 null。 */
export async function getMyReview(pluginId: string): Promise<MarketAPI.MyReview | null> {
  const { data, response } = await request<MarketAPI.MyReview>(
    `/api/plugins/${pluginId}/reviews/mine`,
    { method: 'GET', getResponse: true, skipErrorHandler: true },
  );
  return response.status === 204 ? null : data;
}

/** 发表或修改我的评价(服务端按人按插件 upsert)。 */
export async function upsertReview(pluginId: string, data: { rating: number; body?: string | null }) {
  return request(`/api/plugins/${pluginId}/reviews`, { method: 'PUT', data });
}

/** 删除我的评价。 */
export async function deleteReview(pluginId: string) {
  return request(`/api/plugins/${pluginId}/reviews`, { method: 'DELETE' });
}

import { request } from '@umijs/max';

/** 某插件的评价列表(带各星级分布)。 */
export async function listReviews(pluginId: string, params: { page: number; size: number }) {
  return request<MarketAPI.ReviewPage>(`/api/plugins/${pluginId}/reviews`, {
    method: 'GET',
    params,
  });
}

/**
 * 我对该插件的评价;没有评价过时服务端回 204,这里归一成 null。
 *
 * `getResponse: true` 拿到的是 **axios 的 AxiosResponse**,状态码在 `status` 上。
 * 别写成 `{ data, response }` —— 那是 umi 3 的 umi-request 语义,在 umi 4 里
 * `response` 恒为 undefined,`response.status` 会直接抛 TypeError,
 * 而调用方一 catch 就变成"永远查不到我的评价",看着像后端没返回。
 */
export async function getMyReview(pluginId: string): Promise<MarketAPI.MyReview | null> {
  const { data, status } = await request<MarketAPI.MyReview>(
    `/api/plugins/${pluginId}/reviews/mine`,
    { method: 'GET', getResponse: true, skipErrorHandler: true },
  );
  return status === 204 ? null : data;
}

/** 发表或修改我的评价(服务端按人按插件 upsert)。 */
export async function upsertReview(pluginId: string, data: { rating: number; body?: string | null }) {
  return request(`/api/plugins/${pluginId}/reviews`, { method: 'PUT', data });
}

/** 删除我的评价。 */
export async function deleteReview(pluginId: string) {
  return request(`/api/plugins/${pluginId}/reviews`, { method: 'DELETE' });
}

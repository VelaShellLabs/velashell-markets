import { request } from '@umijs/max';

/** 插件列表(搜索 + 标签过滤 + 排序 + 分页)。 */
export async function listPlugins(params: MarketAPI.PluginQuery) {
  return request<MarketAPI.PluginPage>('/api/plugins', { method: 'GET', params });
}

/** 全部标签与计数。 */
export async function listTags() {
  return request<MarketAPI.TagCount[]>('/api/plugins/tags', { method: 'GET' });
}

/**
 * 编辑推荐。单独取一次而不是在列表里挑 isFeatured:推荐位与列表的排序、分页、
 * 筛选条件都无关 —— 用户切到第 3 页或者按标签过滤之后,首屏那张卡片不该跟着消失。
 */
export async function listFeatured() {
  return request<MarketAPI.PluginPage>('/api/plugins', { method: 'GET', params: { featured: true, sort: 'updated', page: 1, size: 1 } });
}

/** 站点概览数字(首屏三个数)。 */
export async function getStats() {
  return request<MarketAPI.SiteStats>('/api/stats', { method: 'GET' });
}

/** 插件详情。404 由调用方处理(展示"插件不存在"),所以跳过统一错误提示。 */
export async function getPlugin(id: string) {
  return request<MarketAPI.PluginDetail>(`/api/plugins/${id}`, { method: 'GET', skipErrorHandler: true });
}

/** 相关插件:同一作者的其他插件 + 标签相近的插件。 */
export async function getRelated(id: string) {
  return request<MarketAPI.RelatedPlugins>(`/api/plugins/${id}/related`, { method: 'GET', skipErrorHandler: true });
}

/** 取某版本的预签名下载地址。 */
export async function getDownloadUrl(id: string, version: string) {
  return request<{ url: string; payloadSha256: string; fileSha256: string; packageSize: number }>(`/api/plugins/${id}/versions/${version}/download`, { method: 'GET' });
}

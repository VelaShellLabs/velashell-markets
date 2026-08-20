import { request } from '@umijs/max';

/** 插件列表(搜索 + 标签过滤 + 排序 + 分页)。 */
export async function listPlugins(params: MarketAPI.PluginQuery) {
  return request<MarketAPI.PluginPage>('/api/plugins', { method: 'GET', params });
}

/** 全部标签与计数。 */
export async function listTags() {
  return request<MarketAPI.TagCount[]>('/api/plugins/tags', { method: 'GET' });
}

/** 插件详情。404 由调用方处理(展示"插件不存在"),所以跳过统一错误提示。 */
export async function getPlugin(id: string) {
  return request<MarketAPI.PluginDetail>(`/api/plugins/${id}`, { method: 'GET', skipErrorHandler: true });
}

/** 取某版本的预签名下载地址。 */
export async function getDownloadUrl(id: string, version: string) {
  return request<{ url: string }>(`/api/plugins/${id}/versions/${version}/download`, { method: 'GET' });
}

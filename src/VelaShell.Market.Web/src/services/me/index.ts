import { request } from '@umijs/max';

/** 当前用户画像(名字 + 是否审核员)。未登录/失效时抛错,由调用方决定怎么兜。 */
export async function getProfile() {
  return request<MeAPI.Profile>('/api/me', { method: 'GET', skipErrorHandler: true });
}

/** 我发布的插件。 */
export async function getMyPlugins() {
  return request<MeAPI.MyPlugin[]>('/api/me/plugins', { method: 'GET' });
}

/** 更新我的插件页面(说明/标签/主页)。id、版本、兼容信息取自包内 plugin.json,不可改。 */
export async function updatePlugin(id: string, data: MeAPI.PluginPatch) {
  return request(`/api/plugins/${id}`, { method: 'PUT', data });
}

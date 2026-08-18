import { UserManager, WebStorageStateStore, type User } from 'oidc-client-ts';

/** 登录用户(oidc-client-ts 的 User 别名,免得各处都 import 它)。 */
export type MarketUser = User;

/**
 * 对接 EasilyNET.IdentityServer 的登录。
 *
 * 用**授权码 + PKCE**(对方强制 PKCE,也不实现隐式模式)。浏览器里的公开客户端没有
 * 可保密的地方,所以这里不放任何 client secret —— 真需要机密客户端就得由后端代持。
 *
 * 一次性准备:市场需要在 IdentityServer 上有一个 client。对方的示例客户端是 Host 里
 * 硬编码的内存 store,所以别去改那个仓库 —— 用它已经实现的动态客户端注册
 * (POST /connect/register,RFC 7591)注册一个。详见 docs/identity-integration.md。
 *
 * 两个常量都允许被页面注入的全局量覆盖:同一份构建产物因此能部署到不同环境,
 * 不必为了换个 authority 重新打包。
 */
const authority = (window as any).__MARKET_AUTHORITY__ ?? 'https://localhost:7020';
const clientId = (window as any).__MARKET_CLIENT_ID__ ?? 'velashell-market-web';

export const userManager = new UserManager({
  authority,
  client_id: clientId,
  redirect_uri: `${window.location.origin}/callback`,
  post_logout_redirect_uri: window.location.origin,
  response_type: 'code',
  scope: 'openid profile velashell-market',
  // 令牌只放会话存储:关掉标签页即失效,比 localStorage 少一类被顺走的场景。
  userStore: new WebStorageStateStore({ store: window.sessionStorage }),
  automaticSilentRenew: true,
});

export const login = () => userManager.signinRedirect();
export const logout = () => userManager.signoutRedirect();
export const completeLogin = () => userManager.signinRedirectCallback();
export const getUser = (): Promise<MarketUser | null> => userManager.getUser();

/** 带上 Bearer 令牌的 fetch。没有令牌时照常发 —— 浏览与检索本来就允许匿名。 */
export async function api(path: string, init: RequestInit = {}): Promise<Response> {
  const user = await getUser();
  const headers = new Headers(init.headers);
  if (user?.access_token) {
    headers.set('Authorization', `Bearer ${user.access_token}`);
  }
  return fetch(`/api${path}`, { ...init, headers });
}

/** 取 JSON;失败时抛出后端给的可读消息 —— 直接把 500 甩给用户看没有任何帮助。 */
export async function apiJson<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await api(path, init);
  if (!response.ok) {
    const payload = await response.json().catch(() => ({}));
    throw new Error(payload.error ?? payload.detail ?? `请求失败(${response.status})`);
  }
  return response.status === 204 ? (null as T) : ((await response.json()) as T);
}

/** 发 JSON。 */
export const apiSend = (path: string, method: string, body: unknown) =>
  api(path, { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });

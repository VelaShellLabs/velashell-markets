import { UserManager, WebStorageStateStore, type User } from 'oidc-client-ts';

/** 登录用户(oidc-client-ts 的 User 别名,免得各处都 import 它)。 */
export type MarketUser = User;

/**
 * 对接统一认证服务(src/VelaShell.Market.Identity,OpenIddict + MongoDB)的登录。
 *
 * 用**授权码 + PKCE**:服务端强制 PKCE,也不实现隐式模式。浏览器里的公开客户端没有
 * 可保密的地方,所以这里不放任何 client secret —— 真需要机密客户端就得由后端代持。
 *
 * 客户端(client_id、回跳白名单)由认证服务在启动时按配置注册,不需要手工建,
 * 详见 docs/identity-integration.md。
 *
 * 两个常量都允许被页面注入的全局量覆盖(nginx 在启动时注入):
 * 同一份构建产物因此能部署到不同环境,不必为了换个 authority 重新打包。
 */
const authority = (window as any).__MARKET_AUTHORITY__ ?? 'http://localhost:7020';
const clientId = (window as any).__MARKET_CLIENT_ID__ ?? 'velashell-market-web';

export const userManager = new UserManager({
  authority,
  client_id: clientId,
  redirect_uri: `${window.location.origin}/callback`,
  post_logout_redirect_uri: window.location.origin,
  response_type: 'code',
  // offline_access 换来刷新令牌。没有它就只能靠隐藏 iframe 静默续期,而认证服务与商店
  // 不同源,那个 iframe 里的会话 cookie 属于第三方 cookie —— 现在的浏览器基本都会拦掉。
  scope: 'openid profile email velashell-market offline_access',
  // 令牌只放会话存储:关掉标签页即失效,比 localStorage 少一类被顺走的场景。
  userStore: new WebStorageStateStore({ store: window.sessionStorage }),
  automaticSilentRenew: true,
});

export const login = () => userManager.signinRedirect();
export const logout = () => userManager.signoutRedirect();
export const completeLogin = () => userManager.signinRedirectCallback();
export const getUser = (): Promise<MarketUser | null> => userManager.getUser();

/** 当前用户的 Bearer 令牌,没有则 undefined —— 浏览与检索本来就允许匿名。 */
export async function getAccessToken(): Promise<string | undefined> {
  const user = await getUser();
  return user?.access_token;
}

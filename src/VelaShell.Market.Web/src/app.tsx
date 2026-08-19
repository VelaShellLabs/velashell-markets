import { GithubOutlined, UserOutlined } from '@ant-design/icons';
import { type Settings as LayoutSettings } from '@ant-design/pro-components';
import { history, type RequestConfig, type RunTimeLayoutConfig } from '@umijs/max';
import { Avatar, Button, Dropdown } from 'antd';
import defaultSettings from '../config/defaultSettings';
import { errorConfig } from './requestErrorConfig';
import { getProfile } from './services/me';
import { getUser, login, logout } from './utils/auth';

/**
 * 全局初始状态:当前登录用户。access.ts 用它算权限,布局用它渲染头像区。
 * @see https://umijs.org/docs/api/runtime-config#getinitialstate
 */
export async function getInitialState(): Promise<{
  settings?: Partial<LayoutSettings>;
  currentUser?: MarketAPI.Profile | null;
  fetchUserInfo?: () => Promise<MarketAPI.Profile | null>;
}> {
  const fetchUserInfo = async (): Promise<MarketAPI.Profile | null> => {
    // 先看本地有没有 OIDC 令牌,没有就不必打 /me —— 匿名浏览是常态,不该请求出一个 401。
    const user = await getUser();
    if (!user) return null;
    try {
      return await getProfile();
    } catch {
      // 令牌过期等情况:当匿名处理,导航到需要登录的页面时自然会再触发登录。
      return null;
    }
  };
  return {
    currentUser: await fetchUserInfo(),
    fetchUserInfo,
    settings: defaultSettings as Partial<LayoutSettings>,
  };
}

/**
 * ProLayout 运行时配置。
 * @doc https://procomponents.ant.design/components/layout
 */
export const layout: RunTimeLayoutConfig = ({ initialState }) => {
  const currentUser = (initialState as any)?.currentUser as MarketAPI.Profile | null | undefined;
  return {
    // 右上角:GitHub 入口 + 登录按钮/用户菜单。
    actionsRender: () => [
      <Button
        key="github"
        type="text"
        icon={<GithubOutlined />}
        href="https://github.com/joesdu/VelaShell"
        target="_blank"
      />,
      currentUser ? (
        <Dropdown
          key="user"
          menu={{
            items: [
              { key: 'mine', label: '我的上传', onClick: () => history.push('/mine') },
              { key: 'plugins', label: '我的插件', onClick: () => history.push('/owner') },
              { type: 'divider' as const },
              { key: 'logout', label: '退出登录', danger: true, onClick: () => logout() },
            ],
          }}
        >
          <span style={{ cursor: 'pointer', display: 'inline-flex', alignItems: 'center', gap: 8 }}>
            <Avatar size="small" icon={<UserOutlined />} style={{ background: '#4f46e5' }} />
            <span>{currentUser.name}</span>
          </span>
        </Dropdown>
      ) : (
        <Button key="login" type="primary" onClick={() => login()}>
          登录
        </Button>
      ),
    ],
    footerRender: () => (
      <div className="market-footer">VelaShell 插件市场 · 所有上传的 .vpx 均经隔离检测后才会发布</div>
    ),
    menuHeaderRender: undefined,
    ...(initialState as any)?.settings,
  };
};

/**
 * request 全局配置:Bearer 令牌注入与统一错误呈现,见 requestErrorConfig.ts。
 * @doc https://umijs.org/docs/max/request#配置
 */
export const request: RequestConfig = {
  ...errorConfig,
};

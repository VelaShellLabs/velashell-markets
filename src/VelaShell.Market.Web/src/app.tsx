import { Brand, ThemeProvider, ThemeSwitch, ThemeSync, UserMenu } from '@/components';
import { appName, repositoryUrl, version } from '@/configs';
import { getProfile } from '@/services/me';
import { getUser } from '@/utils/auth';
import { getNavTheme } from '@/utils/theme';
import { GithubOutlined } from '@ant-design/icons';
import { type Settings as LayoutSettings } from '@ant-design/pro-components';
import { type RequestConfig, type RunTimeLayoutConfig } from '@umijs/max';
import { Button } from 'antd';
import type { ReactNode } from 'react';
import defaultSettings from '../config/defaultSettings';
import { errorConfig } from './requestErrorConfig';

/** 全局初始状态的形状。useModel('@@initialState') 的类型由它推出来,页面里不用再 as any。 */
export type InitialState = {
  settings?: Partial<LayoutSettings>;
  currentUser?: MeAPI.Profile | null;
  fetchUserInfo?: () => Promise<MeAPI.Profile | null>;
};

/**
 * 整棵树最外面套一层主题。**必须在这里而不是页面里** —— message / modal / notification
 * 都是从根上挂出去的,包不住它们就会出现"页面暗了、弹窗还是亮的"。
 * @doc https://umijs.org/docs/api/runtime-config#rootcontainer
 */
export function rootContainer(container: ReactNode) {
  return <ThemeProvider>{container}</ThemeProvider>;
}

/**
 * 全局初始状态:当前登录用户。access.ts 用它算权限,布局用它渲染头像区。
 * @see https://umijs.org/docs/api/runtime-config#getinitialstate
 */
export async function getInitialState(): Promise<InitialState> {
  const fetchUserInfo = async (): Promise<MeAPI.Profile | null> => {
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
    settings: {
      ...defaultSettings,
      // 首屏就按已保存的主题算出 navTheme,之后由 ThemeSync 跟着切。
      // 不给初值的话,深色下顶栏会先白一帧再变暗。
      navTheme: getNavTheme(),
    } as Partial<LayoutSettings>,
  };
}

/**
 * ProLayout 运行时配置。
 * @doc https://procomponents.ant.design/components/layout
 */
export const layout: RunTimeLayoutConfig = ({ initialState }) => ({
  // 右上角:外观切换 + 仓库入口 + 登录按钮/用户菜单。
  actionsRender: () => [<ThemeSwitch key="theme" />, <Button key="repo" type="text" icon={<GithubOutlined />} href={repositoryUrl} target="_blank" aria-label="源码仓库" />, <UserMenu key="user" />],
  footerRender: () => (
    <div className="market-footer">
      <span>
        {appName} · web {version}
      </span>
      <span style={{ display: 'inline-flex', gap: 18 }}>
        <a href={`${repositoryUrl}/blob/main/docs/security-pipeline.md`} target="_blank" rel="noreferrer noopener">
          安全流水线
        </a>
        <a href={`${repositoryUrl}#readme`} target="_blank" rel="noreferrer noopener">
          开发者文档
        </a>
        <a href={repositoryUrl} target="_blank" rel="noreferrer noopener">
          GitHub
        </a>
      </span>
    </div>
  ),
  // 自己渲染品牌区:ProLayout 默认那套是 <img> + 文本,而这里的帆船图标要跟着主色走,
  // 外链的 svg 拿不到页面的 CSS 变量,换主题时它不会变。
  menuHeaderRender: () => <Brand />,
  /**
   * ThemeSync 挂在这里(而不是页面里):它要 setInitialState,必须活在 initialState
   * Provider 内部,而 childrenRender 正好在那一层里,又能跟着路由一直存在。
   */
  childrenRender: (children: ReactNode) => (
    <>
      <ThemeSync />
      {children}
    </>
  ),
  // 放在最后展开:navTheme 由 ThemeSync 写在 settings 里,前面的默认值不能盖掉它。
  ...initialState?.settings,
});

/**
 * request 全局配置:Bearer 令牌注入与统一错误呈现,见 requestErrorConfig.ts。
 * @doc https://umijs.org/docs/max/request#配置
 */
export const request: RequestConfig = {
  ...errorConfig,
};

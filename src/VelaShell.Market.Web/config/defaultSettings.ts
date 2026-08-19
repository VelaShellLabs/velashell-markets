import type { ProLayoutProps } from '@ant-design/pro-components';

/**
 * ProLayout 的默认外观。商店是内容型站点,用顶栏导航(top)而不是参考项目的 mix:
 * 它没有多级菜单,侧边栏只会浪费一列宽度。
 */
const Settings: ProLayoutProps & {
  pwa?: boolean;
  logo?: string;
} = {
  navTheme: 'light',
  colorPrimary: '#4f46e5',
  layout: 'top',
  contentWidth: 'Fluid',
  fixedHeader: true,
  fixSiderbar: false,
  colorWeak: false,
  splitMenus: false,
  title: 'VelaShell 插件市场',
  logo: '/favicon.svg',
  pwa: false,
  iconfontUrl: '',
  token: {
    header: {
      colorBgHeader: '#ffffff',
      heightLayoutHeader: 56,
    },
    pageContainer: {
      colorBgPageContainer: 'transparent',
    },
  },
};

export default Settings;

// https://umijs.org/config/
// 与 deeplogic.datacollector.webui 同一套 Umi Max + Ant Design Pro 架构。
import { defineConfig } from '@umijs/max';
import defaultSettings from './defaultSettings';
import proxy from './proxy';
import routes from './routes';

const { UMI_ENV = 'dev' } = process.env;
const isProd = process.env.NODE_ENV === 'production';

export default defineConfig({
  /**
   * @name 路由模式
   * @description 这里刻意用 browser 而不是参考项目的 hash:OIDC 的 redirect_uri 是
   * `${origin}/callback`,已写进认证服务的回跳白名单;换成 hash 路由回调会打不回来。
   * SPA fallback 由 nginx 的 try_files 兜底(见 nginx.conf.template)。
   */
  history: { type: 'browser' },
  hash: isProd,
  publicPath: '/',
  title: 'VelaShell 插件市场',
  favicons: ['/favicon.svg'],
  /**
   * @name 路由的配置,不在路由中引入的文件不会编译
   * @doc https://umijs.org/docs/guides/routes
   */
  routes,
  ignoreMomentLocale: true,
  /**
   * @name 代理配置
   * @description 仅本地开发生效;容器里由 nginx 反代同一个 /api 前缀,前端代码两边一字不改。
   */
  proxy: proxy[UMI_ENV as keyof typeof proxy],
  fastRefresh: true,
  //============== 以下都是 max 的插件配置 ===============
  model: {},
  /**
   * @name 全局初始状态
   * @description 存放当前登录用户,access.ts 与各页面共享。
   */
  initialState: {},
  /**
   * @name layout 插件(ProLayout)
   * @doc https://umijs.org/docs/max/layout-menu
   */
  layout: {
    locale: false,
    ...defaultSettings,
  },
  moment2dayjs: {
    preset: 'antd',
  },
  /**
   * @name antd 插件
   * @description 主题令牌整站只在这里定一次,分散到各页会出现"这一页是亮的、那一页是暗的"。
   */
  antd: {
    configProvider: {
      theme: {
        token: {
          colorPrimary: '#4f46e5',
          borderRadius: 8,
          fontSize: 14,
          colorBgLayout: '#f6f7fb',
        },
      },
    },
    appConfig: {
      message: {
        maxCount: 3,
      },
    },
  },
  /**
   * @name 网络请求配置
   * @doc https://umijs.org/docs/max/request
   */
  request: {},
  /**
   * @name 权限插件
   * @description 基于 initialState,见 src/access.ts。
   */
  access: {},
});

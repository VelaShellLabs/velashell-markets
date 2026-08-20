/**
 * 应用级常量。凡是"改一次要处处生效"的字面量都放这里,
 * 页面里不再散落魔法数字与写死的文案(参考架构的 src/configs/**)。
 */
export * from './market';

export const appName = 'VelaShell 插件市场';

/** 前端版本,与 package.json 手工对齐。露在页脚上 —— 排查"我看到的是哪一版"最省事。 */
export const version = '0.3.0';

/** 顶栏的仓库入口。 */
export const repositoryUrl = 'https://github.com/joesdu/VelaShell';

/** 认证服务地址与客户端 id 的默认值,可被部署期注入的全局量覆盖(见 utils/auth)。 */
export const defaultAuthority = 'http://localhost:7020';
export const defaultClientId = 'velashell-market-web';

/** localStorage / sessionStorage 里用到的键,集中登记防止撞名。 */
export const storageKeys = {
  /** 主题模式。loading.js 会在首屏脚本里读同一个键,改名要两处一起改。 */
  theme: 'app.theme',
} as const;

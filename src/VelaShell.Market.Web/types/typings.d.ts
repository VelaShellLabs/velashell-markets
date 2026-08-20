declare module '*.css';
declare module '*.less';
declare module '*.svg';
declare module '*.png';
declare module '*.jpg';
declare module '*.jpeg';
declare module '*.gif';
declare module '*.webp';

/**
 * 部署期注入的全局量。nginx 启动时用 envsubst 把它们写进 index.html
 * (见 nginx.conf.template 的 sub_filter),同一份构建产物因此能对接不同环境的认证服务。
 */
interface Window {
  __MARKET_AUTHORITY__?: string;
  __MARKET_CLIENT_ID__?: string;
}

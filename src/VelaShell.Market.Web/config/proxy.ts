/**
 * 本地开发代理。生产环境代理不生效 —— 容器里由 nginx 反代同一个 /api 前缀,
 * 前端因此永远同源调用 /api,不需要处理跨域。
 * @doc https://umijs.org/docs/guides/proxy
 */
export default {
  dev: {
    '/api/': {
      target: 'http://localhost:8080',
      changeOrigin: true,
    },
  },
};

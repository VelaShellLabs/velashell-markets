import { defineConfig } from 'umi';

export default defineConfig({
  npmClient: 'npm',
  title: 'VelaShell 插件市场',
  favicons: ['/favicon.svg'],
  // 全站共用 layouts/index.tsx 的骨架:顶栏、主题令牌与中文语言在那里一次定死。
  routes: [
    {
      path: '/',
      component: '@/layouts/index',
      routes: [
        { path: '/', component: 'index' },
        { path: '/plugins/:id', component: 'detail' },
        { path: '/upload', component: 'upload' },
        { path: '/mine', component: 'mine' },
        { path: '/owner', component: 'owner' },
        { path: '/moderation', component: 'moderation' },
        { path: '/callback', component: 'callback' },
      ],
    },
  ],
  // 开发期直连本机 API;容器里由 nginx 反代同一个 /api 前缀,前端代码两边一字不改。
  proxy: {
    '/api': { target: 'http://localhost:8080', changeOrigin: true },
  },
});

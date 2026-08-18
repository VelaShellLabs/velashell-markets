import { defineConfig } from 'umi';

export default defineConfig({
  npmClient: 'npm',
  title: 'VelaShell 插件市场',
  routes: [
    { path: '/', component: 'index' },
    { path: '/plugins/:id', component: 'detail' },
    { path: '/upload', component: 'upload' },
    { path: '/mine', component: 'mine' },
    { path: '/callback', component: 'callback' },
  ],
  // 开发期直连本机 API;容器里由 nginx 反代同一个 /api 前缀,前端代码两边一字不改。
  proxy: {
    '/api': { target: 'http://localhost:8080', changeOrigin: true },
  },
});

import { defineConfig } from 'umi';

export default defineConfig({
  npmClient: 'npm',
  title: 'VelaShell 插件市场',
  favicons: ['/favicon.svg'],
  // 全站骨架(顶栏、主题令牌、中文语言)在 src/layouts/index.tsx 里一次定死。
  // 它**不出现在下面的路由表里**:Umi 约定 src/layouts/index.tsx 就是全局布局,
  // 会自动包在所有路由外层(生成的路由表里那个 @@/global-layout)。
  // 再手写一层 { component: '@/layouts/index', routes: [...] } 会让同一个布局套两遍,
  // 表现为两条顶栏、两条页脚。
  routes: [
    { path: '/', component: 'index' },
    { path: '/plugins/:id', component: 'detail' },
    { path: '/upload', component: 'upload' },
    { path: '/mine', component: 'mine' },
    { path: '/owner', component: 'owner' },
    { path: '/moderation', component: 'moderation' },
    { path: '/callback', component: 'callback' },
  ],
  // 开发期直连本机 API;容器里由 nginx 反代同一个 /api 前缀,前端代码两边一字不改。
  proxy: {
    '/api': { target: 'http://localhost:8080', changeOrigin: true },
  },
});

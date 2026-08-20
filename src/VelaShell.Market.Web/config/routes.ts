/**
 * 路由即菜单(ProLayout 从这里生成顶栏导航)。
 * 只支持 path/component/routes/redirect/wrappers/title/name/icon 等字段。
 * @doc https://umijs.org/docs/guides/routes
 */
export default [
  {
    path: '/',
    name: '浏览',
    icon: 'appstore',
    component: './market',
  },
  {
    path: '/plugins/:id',
    name: '插件详情',
    component: './plugin',
    hideInMenu: true,
  },
  {
    /**
     * 上传/我的上传/我的插件都要登录。用 `wrappers` 而不是 `access`:
     * access 只会把菜单藏掉,直接敲 URL 仍然进得来,那时该看到的是"请先登录",
     * 而不是一张填完才被 401 拒绝的表单。发布入口本身要一直露在菜单里 ——
     * 藏起来的话,没登录的人根本不知道这个站能上传。
     */
    path: '/upload',
    name: '发布插件',
    icon: 'cloudUpload',
    component: './upload',
    wrappers: ['@/components/RequireAuth'],
  },
  {
    path: '/mine',
    name: '我的上传',
    component: './mine',
    hideInMenu: true,
    wrappers: ['@/components/RequireAuth'],
  },
  {
    path: '/owner',
    name: '我的插件',
    component: './owner',
    hideInMenu: true,
    wrappers: ['@/components/RequireAuth'],
  },
  {
    path: '/moderation',
    name: '审核台',
    icon: 'safety',
    component: './moderation',
    access: 'canModerate',
  },
  {
    // OIDC 回调页:不能套布局 —— 它只负责换令牌然后跳走。
    path: '/callback',
    component: './callback',
    layout: false,
  },
  {
    component: './404',
    layout: false,
    path: '/*',
  },
];

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
    path: '/upload',
    name: '发布插件',
    icon: 'cloudUpload',
    component: './upload',
  },
  {
    path: '/mine',
    name: '我的上传',
    component: './mine',
    hideInMenu: true,
  },
  {
    path: '/owner',
    name: '我的插件',
    component: './owner',
    hideInMenu: true,
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

# Agent Guide(Umi Max + Ant Design Pro)

本文件面向在 `src/VelaShell.Market.Web` 里工作的编码工具。架构与约定取自 `deeplogic.datacollector.webui`,除非另有要求,一律沿用既有模式。

## 仓库事实

- 框架:Umi Max(`@umijs/max`)+ Ant Design Pro。
- UI:Ant Design v6 + Pro Components。
- 语言:TypeScript,`strict: true`。
- 包管理:Bun(仓库里只有 `bun.lock`,没有 package-lock.json)。
- 别名:`@/*` -> `src/*`,`@@/*` -> `src/.umi/*`。
- Prettier:`.prettierrc`(printWidth 200、单引号、尾逗号 all)。
- 没有 ESLint 配置;格式一律交给 Prettier。

## 常用命令

```bash
bun install          # 依赖(postinstall 会跑 max setup 刷新 Umi 运行时导出)
bun run dev          # 本地开发,8000 端口,/api 代理到 localhost:8080
bun run build        # 产出 dist/,容器镜像只装这一份
bun run analyze      # ANALYZE=1 的构建产物体积报告
bun run typecheck    # tsc --noEmit
bun run prettier     # 全量格式化
```

没有测试脚本与测试文件。新增测试时,请补一条 `bun run test` 并在这里写明单测命令。

## 目录结构

```
config/           config.ts(Umi 配置)、routes.ts(路由即菜单)、proxy.ts、defaultSettings.ts(ProLayout 外观)
types/            按服务域拆分的全局 .d.ts;命名空间 MarketAPI / MeAPI / ReviewsAPI / UploadsAPI / ModerationAPI
public/scripts/   loading.js —— 首屏占位,跑在打包产物之外
src/app.tsx       rootContainer(主题)、getInitialState(当前用户)、layout(ProLayout 运行期配置)
src/access.ts     权限:signedIn / canModerate,由 initialState.currentUser 推导
src/configs/      应用常量:分页大小、排序项、状态到标签的映射、渐变色、storage key
src/components/   通用组件 + 统一出口 index.ts;页面一律从 `@/components` 取
src/hooks/        跨页面复用的状态机(usePagedTable)
src/services/     按域划分的接口层:market / me / reviews / uploads / moderation
src/utils/        theme(主题存储)、auth(OIDC)、format(展示格式化)、request(useRequest 的坑)
src/pages/        浏览 / 详情 / 发布 / 我的上传 / 我的插件 / 审核台 / 回调 / 404
src/global.less   页面容器、市场特有样式、Markdown 排版;颜色一律走 CSS 变量
```

## 必须知道的几条

### `useRequest` 会多剥一层 `data`

umi 给 ahooks 传的默认 `formatResult` 是 `(result) => result?.data`。本项目接口直接返回负载,再剥一层就成了 `undefined` —— 页面表现为"列表永远是空的"且不报错。

**每个 `useRequest` 都要写 `formatResult: keepResult`**(`@/utils/request`)。顺带的好处是 `data` 能被正确推断类型,不写会退化成 `{}`。

### 主题

- 存储在 `src/utils/theme.ts`,是一个**独立于 React 的外部 store**,不放进 initialState/model —— 主题要作用在包住整棵树的 ConfigProvider 上,而 initialState 的 Provider 包不住从根挂出去的 message/modal。
- 三档:`light` / `dark` / `auto`,键为 `app.theme`(`configs.storageKeys.theme`)。
- 两个接入点:`ThemeProvider`(rootContainer,antd 算法与令牌 + `<html data-theme>`)与 `ThemeSync`(把 navTheme 写进 `initialState.settings`,ProLayout 顶栏靠它跟着切)。
- `public/scripts/loading.js` 也读同一个键 —— 改键名要连它一起改,它拿不到模块导入。
- **新增自定义样式不要写死颜色**,一律用 `global.less` 顶部那组 `--market-*` 变量。

### 路由

- 路由即菜单,定义在 `config/routes.ts`。
- 需要登录的页面用 `wrappers: ['@/components/RequireAuth']`,不要只用 `access` —— 后者只藏菜单,直接敲 URL 仍然进得来。
- 历史模式是 `browser` 而不是参考项目的 `hash`:OIDC 的 redirect_uri 已写进认证服务白名单。

## 代码风格(沿用参考项目)

- 单引号、分号、尾逗号 all、arrow parens avoid、print width 200、2 空格缩进。
- 类型导入用 `import type`;内部导入用 `@/` 别名。
- 函数组件 + hooks;组件 `PascalCase`,hook/局部变量 `camelCase`。
- 组件 props 用 `interface XxxProps` 或就地字面量类型,避免 `any`。
- 取数一律 `useRequest`,不要手写 `useState + useEffect + then/catch`。
- 错误处理集中在 `src/requestErrorConfig.ts`;需要页面自己呈现错误时传 `skipErrorHandler: true`。
- 接口调用只写在 `src/services/**`,页面不直接用 `request`。
- 常量进 `src/configs/**`,不要在页面里散落魔法值。

## 与后端的关系

- API 是 OIDC 资源服务器;令牌由 `src/utils/auth.ts`(oidc-client-ts,授权码 + PKCE)管理,存在 sessionStorage,请求拦截器负责注入 Bearer。
- 认证服务地址与 client id 允许被部署期注入的 `window.__MARKET_AUTHORITY__` / `window.__MARKET_CLIENT_ID__` 覆盖(nginx 启动时 envsubst 注入),同一份产物可对接不同环境。

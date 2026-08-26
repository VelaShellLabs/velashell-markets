# API 一览

匿名可读、登录可写、审核台另需名单。完整定义在开发环境的 `/swagger`。

需登录的接口要在请求头带 `Authorization: Bearer <access_token>`,令牌由统一认证服务签发
(授权码 + PKCE,见 [identity-integration.md](identity-integration.md))。协议端点本身
(`/connect/authorize`、`/connect/token`、`/connect/userinfo`、`/connect/endsession`)
在认证服务上,不在这个 API 里。

## 公开(无需登录)

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/stats` | 站点概览:`plugins` / `versions` / `downloads` / `blockingPublished` |
| GET | `/api/plugins` | 检索。`q` 关键词、`tag` 标签、`apiLevel` 宿主兼容性、`featured` 只看编辑推荐、`sort`(updated/downloads/rating/created)、`page`/`size` |
| GET | `/api/plugins/{id}` | 详情:渲染后的 Markdown、已发布版本列表、每个版本的贡献点与**公开检测结论** |
| GET | `/api/plugins/{id}/related` | 相关插件:`byAuthor`(同一作者)+ `byTags`(标签重合) |
| GET | `/api/plugins/{id}/versions/{version}/download` | 换一个短时效下载 URL(**只签正式桶**),同时返回两个 SHA-256 |
| GET | `/api/plugins/tags` | 标签云 |
| GET | `/api/plugins/{id}/reviews` | 评价列表(分页)。`sort`:`recent`(默认)/ `lowest` / `highest` |
| GET | `/health` | 健康检查 |

## 需登录

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/me` | 当前用户,含 `subject` / `email` / `isModerator` |
| GET | `/api/me/plugins` | 我拥有的插件 |
| POST | `/api/uploads/inspect` | **只读预检**:读出包内清单与签名状态,不落盘、不入库、不排队。顺带回答"这个 id 是不是已被别人认领""这个版本是不是已发布" |
| POST | `/api/uploads` | 上传 `.vpx`(multipart:`file` + `description` / `releaseNotes` / `tags`)。**同步只做认领与落隔离桶**,检测在后台 |
| POST | `/api/uploads/preview` | 把 Markdown 渲染成插件页上最终会显示的 HTML(与发布走同一个渲染器) |
| GET | `/api/uploads/mine` | 我的上传与完整检测报告 |
| PUT | `/api/plugins/{id}` | 改插件页面(描述 / 标签 / 主页)。仅拥有者 |
| POST | `/api/plugins/{id}/versions/{version}/withdraw` | 撤回版本(**从正式桶物理删除**)。拥有者或审核员 |
| GET | `/api/plugins/{id}/reviews/mine` | 我对该插件的评价(没有则 204) |
| PUT | `/api/plugins/{id}/reviews` | 发表或修改我的评价(每人每插件一条) |
| DELETE | `/api/plugins/{id}/reviews` | 删除我的评价 |
| PUT | `/api/plugins/{id}/reviews/{reviewId}/reply` | 作者公开回复一条评价,`{ "body": "…" }`。仅拥有者 |
| DELETE | `/api/plugins/{id}/reviews/{reviewId}/reply` | 撤下作者回复。仅拥有者 |

## 审核员(`Auth:ModeratorSubjects`)

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/moderation/queue` | 待人工复核的版本(带完整检测报告、上传者与该插件的历史发布数) |
| GET | `/api/moderation/versions/{id}/entries` | 包内清单(文件名 / 大小 / 可疑标记)。**只对仍在隔离区的版本开放** |
| GET | `/api/moderation/versions/{id}/sample` | 下载隔离区里的样本包。**由 API 转发字节流**,不签发对外 URL |
| POST | `/api/moderation/versions/{id}/approve` | 放行并发布(与自动放行走同一条 `PublishAsync`)。`{ "reason": "…" }` 可选,填了会记进报告 |
| POST | `/api/moderation/versions/{id}/reject` | 驳回,`{ "reason": "…" }` 必填 |
| GET | `/api/moderation/plugins` | 插件治理列表(含已下架) |
| POST | `/api/moderation/plugins/{pluginId}/unlist` | 软下架,`{ "reason": "…" }` |
| POST | `/api/moderation/plugins/{pluginId}/relist` | 恢复上架 |
| POST | `/api/moderation/plugins/{pluginId}/takedown` | 强制下架(物理删除正式桶里的包),`{ "reason": "…" }` |
| POST | `/api/moderation/plugins/{pluginId}/clear-description` | 清空违规描述,`{ "reason": "…" }` |
| POST | `/api/moderation/plugins/{pluginId}/feature` | 设为「编辑推荐」(浏览页首屏那张双宽卡片) |
| POST | `/api/moderation/plugins/{pluginId}/unfeature` | 取消编辑推荐 |
| GET | `/api/moderation/reviews` | 评价治理列表(含已隐藏) |
| POST | `/api/moderation/reviews/{id}/hide` / `unhide` / `purge` | 隐藏 / 取消隐藏 / 彻底删除,`hide` 与 `purge` 要 `{ "reason": "…" }` |

## 几个刻意的约定

- **插件 id、版本、apiLevel、minHostVersion、hostMode 一律取自包内的 `plugin.json`**,任何接口都不接受手填 —— 否则展示的和实际装的能对不上。
- 上传是 `202 Accepted` 而不是 `201`:那一刻包只是进了隔离区,还不是"已发布"。
- 下载每次签 URL 即 `$inc` 计数(原子),目前**不做去重**。
- 评价的 Markdown 与插件描述一样,**只存原文**,渲染在读取时做并经白名单清洗。
- 详情页上的检测结论是**删过的**:只给判定、起止时间与引擎版本,**不下发 findings 原文** ——
  那些带包内路径的诊断信息只给上传者(`/api/uploads/mine`)和审核员(`/api/moderation/queue`)。
  公开出去等于把"这个包哪里值得注意"整理成一份现成的清单挂在详情页上。
- 隔离桶**永远不签发对外可访问的 URL**。审核员看样本走 `/sample`,每次读取都带自己的令牌,
  过期即失效,也留得下日志。`PackageStorage.CreateDownloadUrl` 刻意只认正式桶,没有隔离桶的重载。
- 「编辑推荐」是人工开关,不按下载量自动算:自动榜单只会把已经很火的插件推得更火,
  而首屏那个位置的价值恰恰在于能顶起一个没人知道的好插件。
- `contributes` 是清单里的**声明式贡献点**(命令、协议、工作台),不是权限清单 ——
  VelaShell 的清单格式目前没有能力/权限声明字段,界面上不要把它说成"该插件申请的权限"。

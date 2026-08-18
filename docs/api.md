# API 一览

匿名可读、登录可写、审核台另需名单。完整定义在开发环境的 `/swagger`。

## 公开(无需登录)

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/plugins` | 检索。`q` 关键词、`tag` 标签、`apiLevel` 宿主兼容性、`sort`(updated/downloads/rating/created)、`page`/`size` |
| GET | `/api/plugins/{id}` | 详情:渲染后的 Markdown + 已发布版本列表 |
| GET | `/api/plugins/{id}/versions/{version}/download` | 换一个短时效下载 URL(**只签正式桶**),同时返回两个 SHA-256 |
| GET | `/api/plugins/tags` | 标签云 |
| GET | `/api/plugins/{id}/reviews` | 评价列表(分页) |
| GET | `/health` | 健康检查 |

## 需登录

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/me` | 当前用户,含 `isModerator` |
| GET | `/api/me/plugins` | 我拥有的插件 |
| POST | `/api/uploads` | 上传 `.vpx`(multipart:`file` + `description` / `releaseNotes` / `tags`)。**同步只做认领与落隔离桶**,检测在后台 |
| GET | `/api/uploads/mine` | 我的上传与完整检测报告 |
| PUT | `/api/plugins/{id}` | 改插件页面(描述 / 标签 / 主页)。仅拥有者 |
| POST | `/api/plugins/{id}/versions/{version}/withdraw` | 撤回版本(**从正式桶物理删除**)。拥有者或审核员 |
| GET | `/api/plugins/{id}/reviews/mine` | 我对该插件的评价(没有则 204) |
| PUT | `/api/plugins/{id}/reviews` | 发表或修改我的评价(每人每插件一条) |
| DELETE | `/api/plugins/{id}/reviews` | 删除我的评价 |

## 审核员(`Auth:ModeratorSubjects`)

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/moderation/queue` | 待人工复核的版本(带完整检测报告) |
| POST | `/api/moderation/versions/{id}/approve` | 放行并发布(与自动放行走同一条 `PublishAsync`) |
| POST | `/api/moderation/versions/{id}/reject` | 驳回,`{ "reason": "…" }` 必填 |
| POST | `/api/moderation/plugins/{pluginId}/unlist` | 下架插件,`{ "reason": "…" }` |

## 几个刻意的约定

- **插件 id、版本、apiLevel、minHostVersion、hostMode 一律取自包内的 `plugin.json`**,任何接口都不接受手填 —— 否则展示的和实际装的能对不上。
- 上传是 `202 Accepted` 而不是 `201`:那一刻包只是进了隔离区,还不是"已发布"。
- 下载每次签 URL 即 `$inc` 计数(原子),目前**不做去重**。
- 评价的 Markdown 与插件描述一样,**只存原文**,渲染在读取时做并经白名单清洗。

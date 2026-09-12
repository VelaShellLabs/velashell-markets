# 镜像:怎么出、怎么搬、怎么推仓库

本仓库自建两个镜像,来路**不一样**,这是本文存在的理由。

| 镜像 | 来路 | 定义在哪 |
| --- | --- | --- |
| `velashell/market-api` | .NET SDK 容器发布(`dotnet publish -t:PublishContainer`),**没有 Dockerfile** | `src/VelaShell.Market.Api/VelaShell.Market.Api.csproj` 的「容器」段 |
| `velashell/market-web` | `docker build` | `build/web.Dockerfile` |

名字里的第一段 `velashell` 是 **Harbor 上的项目名**,不是仓库名 ——
认证等别的服务也推进同一个项目,所以第二段带 `market-` 前缀把自己认出来。

统一认证的镜像在 [velashell-identity](https://github.com/joesdu/velashell-identity) 仓库,
用法与本文一致(那边的脚本叫 `build/Publish-Image.ps1`,单数)。

入口只有一个:

```powershell
pwsh ./build/Publish-Images.ps1            # 两个镜像 → 本机 Docker
pwsh ./build/Publish-Images.ps1 -Service api   # 只重出 api,日常改后端就用这条
```

`docker compose up -d` **不会**替你构建 api —— 它没有 `build:` 节,因为它根本没有
Dockerfile。改了后端代码不重跑脚本,起来的还是旧镜像。

## 为什么 api 没有 Dockerfile

早先它有,并且为了迁就镜像踩过一个坑:`global.json` 钉的 SDK 比 `mcr.microsoft.com/dotnet/nightly/sdk`
镜像里的新,而 `rollForward` 只能往前滚不能往回,于是 `docker compose build` 直接失败。
当时的权宜之计是在 Dockerfile 里把指定版本的 SDK 与运行时叠加进镜像。

改用 SDK 容器发布之后这个问题自然消失:**编译发生在宿主机上,用的就是 `global.json` 指定的那个 SDK**,
镜像只需要提供运行时。所以现在只剩一条约束:

> ⚠️ `ContainerBaseImage` 里的运行时**不能比编译用的 SDK 旧**。
> 运行时只能向前滚补丁,拿旧运行时跑新 SDK 编出来的程序集会直接"找不到框架"。
> 目前 `mcr.microsoft.com/dotnet/aspnet:11.0` 里是 `11.0.0-rc.1.26425.128`,与 `global.json` 同版。
> 换基础镜像前先 `docker run --rm <镜像> ls /usr/share/dotnet/shared/Microsoft.AspNetCore.App` 确认一眼。

## 两个容易踩的默认值

SDK 容器发布有两处默认行为,在本仓库里都被显式改掉了,改回去会静默出问题:

**一、`LocalRegistry` 不能留空。**
留空是"自动探测本机容器运行时"。装了 WSL 容器支持的 Windows 上,它可能挑中 WSL 的容器存储
(`wslc`)而不是 Docker Desktop —— 发布会报成功,但 `docker images` 里查无此人,
compose 起不来。csproj 里钉死了 `<LocalRegistry>Docker</LocalRegistry>`。

**二、`ContainerUser` 必须显式写。**
只要显式指定了 `ContainerBaseImage`,SDK 就认定"这不是微软的默认镜像",于是**不再自动设置非 root 用户**。
不写这一行,镜像就是 root 跑的 —— 而 api 这个进程要解开陌生人上传的压缩包,
这一层权限不能丢。csproj 里写的是 `<ContainerUser>app</ContainerUser>`(aspnet 镜像预置的 uid/gid 1654)。

> 由此还引出一件事:SDK 容器发布**没有 `RUN`,也就没法 `chown`**。
> 服务如果要往命名卷里写东西,卷首次挂载会被 Docker 建成 root 所有,非 root 进程写不进去。
> api 不受影响(它不挂卷,文件都在 MinIO),但认证服务要写签名密钥 ——
> 那边是在 compose 里用**同一个镜像**以 root 跑一次 `chown` 解决的,见 velashell-identity 的 compose。

## 搬到另一台机器(离线)

```powershell
pwsh ./build/Publish-Images.ps1 -Archive
# 或者直接落到已挂载的共享目录
pwsh ./build/Publish-Images.ps1 -Archive -OutputDir Z:\velashell-market
```

产物:

| 文件 | 怎么来的 |
| --- | --- |
| `velashell-market-api.tar.gz` | SDK 直接写的归档(**不是** `docker save`) |
| `velashell-market-web.tar` | `docker save` |

扩展名不一样不是笔误:api 走的是 SDK 的 `ContainerArchiveOutputPath`,它出的就是 gzip 过的;
web 是 `docker save`,containerd 存储下导出的层本来就压过了,再套一层没收益。
`docker load -i` 两种都吃。

目标机上:

```bash
docker load -i velashell-market-api.tar.gz
docker load -i velashell-market-web.tar
docker compose up -d
```

> SDK 按镜像名建目录 —— `velashell/market-api` 会落成 `<dir>/velashell/market-api.tar.gz`。
> 脚本会把它搬平成上表里的扁平文件名,免得部署目录里多出一层。

## 推到自建 Harbor

```powershell
docker login harbor.easilynet.top
pwsh ./build/Publish-Images.ps1 -Push -Tag 2026-09-12
```

推上去是 `harbor.easilynet.top/velashell/market-api:2026-09-12` 与
`harbor.easilynet.top/velashell/market-web:2026-09-12`。

`-Registry` 默认就是 `harbor.easilynet.top`,推别处才需要传;**光有默认值不会推,
必须给 `-Push`** —— 否则日常的本机构建会顺手推一趟远端。

几件要先知道的:

- **Harbor 里要先建好 `velashell` 这个项目。** Harbor 不会自动创建项目,
  没建就是推的时候被拒。镜像名的第一段就是项目名,所以三个镜像
  (`velashell/market-api`、`velashell/market-web`、认证的 `velashell/identity`)
  共用这一个项目。
- **凭据只认 `docker login`。** api 走 SDK 推送、web 走 `docker push`,
  两者读的是同一份 `~/.docker/config.json`,所以登录一次就够。
- **Harbor 用自签证书的话**,要先把 CA 装进本机信任库 —— SDK 那条路不认
  `~/.docker/daemon.json` 里的 `insecure-registries`(那是 Docker 守护进程的设置,
  而 SDK 是自己发 HTTPS 请求的)。
- **别用 `:latest` 往 Harbor 推。** 带上 `-Tag`,否则回滚时无从选起。

推完之后,目标机上**不用改 compose** —— 两个 `image:` 都读环境变量,在 `.env` 里写全名:

```ini
MARKET_API_IMAGE=harbor.easilynet.top/velashell/market-api:2026-09-12
MARKET_WEB_IMAGE=harbor.easilynet.top/velashell/market-web:2026-09-12
```

然后 `docker compose pull && docker compose up -d`。
不写这两行时用的是本机构建出来的 `velashell/market-api:latest` / `velashell/market-web:latest`。

## 前端的额外一步

`build/web.Dockerfile` 只把 `dist/` 装进 nginx,**不在容器里打包**。
脚本默认会先跑一遍 `bun run build`;前端没改动时加 `-SkipWebBundle` 省几十秒,
但那台机器上得先有 `dist/`,否则会装出一个空站点(脚本会先拦住你)。

# 前端(Umi Max + Ant Design Pro):这个镜像只做**分发**,不在容器里构建。
# dist/ 由本机先跑 `bun run build` 产出 —— VS 构建解决方案时会自动跑,
# 见 VelaShell.Market.Web.esproj 的 ShouldRunBuildScript / BuildCommand。
#
# 为什么放弃"多阶段在容器里打包":
# 1. 那需要 node:22-slim,而 docker.io 的 metadata 解析在这里是坏的 ——
#    auth.docker.io 的 DNS 被污染到 Facebook 段,主机和 BuildKit 都连不上,
#    整套 compose 构建会跟着挂(mcr.microsoft.com 正常,所以 api/identity 不受影响)。
#    nginx:alpine 已在本地且 metadata 有缓存,这个单阶段镜像离线也能出。
# 2. F5 的内循环快得多:省掉容器里整整一遍依赖安装 + 打包。
# 代价:镜像不再自带前端工具链,在没装 bun 的机器上构建前必须先有 dist/。
# 想换回自包含的多阶段构建,`git log build/web.Dockerfile` 里有那一版。
FROM nginx:alpine
COPY dist/ /usr/share/nginx/html/
# 放进 templates/ 而不是 conf.d/:nginx 镜像启动时会对 templates 里的文件跑一遍 envsubst,
# 把下面这两个变量替换进去,再输出到 /etc/nginx/conf.d/default.conf。
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
# 认证服务的地址与客户端 id。compose 会覆盖,这里的默认值保证单独 docker run 也能起。
# 只替换 MARKET_ 开头的变量,免得 nginx 自己的 $uri、$host 之类被误伤。
ENV MARKET_AUTHORITY=http://localhost:7020 \
    MARKET_CLIENT_ID=velashell-market-web \
    MARKET_PUBLIC_BUCKET=vpx-public \
    NGINX_ENVSUBST_FILTER=^MARKET_
EXPOSE 80

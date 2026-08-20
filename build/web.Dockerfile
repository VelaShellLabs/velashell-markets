# 前端(Umi Max + Ant Design Pro),产物由 nginx 托管,/api 反代到 api 容器。
#
# 构建阶段用 slim(glibc)而不是 alpine(musl):esbuild / mako 这类工具的原生包按 libc 分发,
# 在 alpine 下 npm 会把 glibc 版裁掉,构建会死在 "@esbuild/linux-x64 could not be found"。
# 这只是构建阶段,最终镜像仍是下面的 nginx:alpine,体积不受影响。
FROM node:22-slim AS build
WORKDIR /app

# 依赖层:只拷清单。改前端源码时这一层命中缓存,省掉整整一遍 npm install ——
# VS 里 F5 会对整套 compose 跑 build,这个差别就是"秒过"与"等几分钟"。
# --ignore-scripts 跳过 package.json 的 postinstall(`max setup`):
# 它要读 config/ 与 src/ 才能生成类型,而那时源码还没拷进来。
# npm ci:严格按 package-lock.json 装,每次构建拿到的依赖树完全一致。
# 锁文件必须在与这里相同的 libc 环境(node:22-slim)下生成,否则平台原生包会对不上。
COPY package*.json ./
RUN npm ci --ignore-scripts --no-audit --no-fund

# 源码层:只有这里的改动会让下面两步重跑。
COPY . .
RUN npm run postinstall && npm run build

FROM nginx:alpine
COPY --from=build /app/dist /usr/share/nginx/html
# 放进 templates/ 而不是 conf.d/:nginx 镜像启动时会对 templates 里的文件跑一遍 envsubst,
# 把下面这两个变量替换进去,再输出到 /etc/nginx/conf.d/default.conf。
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
# 认证服务的地址与客户端 id。compose 会覆盖,这里的默认值保证单独 docker run 也能起。
# 只替换 MARKET_ 开头的变量,免得 nginx 自己的 $uri、$host 之类被误伤。
ENV MARKET_AUTHORITY=http://localhost:7020 \
    MARKET_CLIENT_ID=velashell-market-web \
    NGINX_ENVSUBST_FILTER=^MARKET_
EXPOSE 80

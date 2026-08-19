# 前端(React + Umi + antd),产物由 nginx 托管,/api 反代到 api 容器。
FROM node:22-alpine AS build
WORKDIR /app
# 先拷全部源码再 install:package.json 的 postinstall 是 `umi setup`,
# 它需要 .umirc.ts 与 src/ 才能生成类型 —— 只拷 package.json 就装,这一步会失败。
# 代价是源码改动会让依赖层失效,对这个体量的前端可以接受。
COPY . .
RUN npm install --no-audit --no-fund
RUN npm run build

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

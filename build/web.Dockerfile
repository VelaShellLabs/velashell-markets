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
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80

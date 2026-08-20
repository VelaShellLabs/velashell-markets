# 统一认证服务(OpenIddict + MongoDB)的镜像。构建上下文是仓库根目录 ——
# 它要用到根上的 Directory.Build.props / Directory.Packages.props / global.json。
#
# 阶段划分照 VS 容器工具的约定来:运行期环境单独成一个叫 base 的阶段。
# VS 的 Fast 模式(F5 调试)只构建到 --target base,再把宿主机编译好的程序集挂进去跑 vsdbg;
# 没有 base 阶段就起不了调试容器。Regular 模式与 `docker compose build` 照常构建到 final。
FROM mcr.microsoft.com/dotnet/nightly/aspnet:11.0-preview AS base
WORKDIR /app
# 不用 root 跑。/app/keys 要先建好并归属这个用户:命名卷第一次挂载会继承镜像里
# 该目录的属主,不然容器起来就写不进密钥。
RUN useradd --system --user-group --no-create-home identity \
    && mkdir -p /app/keys \
    && chown -R identity:identity /app
USER identity
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/nightly/sdk:11.0-preview AS build
WORKDIR /src

# 先只拷构建描述文件再 restore:源码改动不会让依赖还原的缓存层失效。
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/VelaShell.Market.Identity/*.csproj ./src/VelaShell.Market.Identity/
RUN dotnet restore src/VelaShell.Market.Identity/VelaShell.Market.Identity.csproj

COPY src/VelaShell.Market.Identity/ ./src/VelaShell.Market.Identity/
RUN dotnet publish src/VelaShell.Market.Identity/VelaShell.Market.Identity.csproj \
    -c Release -o /app --no-restore --nologo

FROM base AS final
WORKDIR /app
COPY --from=build --chown=identity:identity /app .
ENTRYPOINT ["dotnet", "VelaShell.Market.Identity.dll"]

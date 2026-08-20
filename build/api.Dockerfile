# 市场 API 的镜像。构建上下文是仓库根目录 —— 它要用到根上的
# Directory.Build.props / Directory.Packages.props / global.json。
# VelaShell.PluginSdk 已发到 nuget.org(1.0.1),本地源与 nuget.config 因此都已退休。
#
# 阶段划分照 VS 容器工具的约定来:运行期环境单独成一个叫 base 的阶段。
# VS 的 Fast 模式(DockerDevelopmentMode=Fast,F5 调试走的就是它)只构建到 --target base,
# 然后把宿主机上编译好的程序集挂进容器、在里面拉起 vsdbg;没有 base 阶段它就没法起调试容器。
# Regular 模式与 `docker compose build` 则照常一路构建到 final,产物完全一致。
FROM mcr.microsoft.com/dotnet/nightly/aspnet:11.0-preview AS base
WORKDIR /app
# 不用 root 跑:这个进程要解开陌生人上传的压缩包,能少一层权限就少一层。
# 建在 base 里而不是 final:Fast 模式只有 base,调试时也得是同一个非 root 身份,
# 否则"调试跑得通、正式镜像跑不通"这种差异要到上线才暴露。
RUN useradd --system --user-group --no-create-home market && chown -R market:market /app
USER market
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/nightly/sdk:11.0-preview AS build
WORKDIR /src

# 先只拷构建描述文件再 restore:源码改动不会让依赖还原的缓存层失效。
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/VelaShell.Market.Domain/*.csproj ./src/VelaShell.Market.Domain/
COPY src/VelaShell.Market.Infrastructure/*.csproj ./src/VelaShell.Market.Infrastructure/
COPY src/VelaShell.Market.Api/*.csproj ./src/VelaShell.Market.Api/
RUN dotnet restore src/VelaShell.Market.Api/VelaShell.Market.Api.csproj

COPY src/ ./src/
RUN dotnet publish src/VelaShell.Market.Api/VelaShell.Market.Api.csproj \
    -c Release -o /app --no-restore --nologo

FROM base AS final
WORKDIR /app
# --chown 不能省:COPY 不受上面的 USER 影响,默认把文件落成 root 所有,
# 而 base 里那次 chown 发生在拷贝之前,盖不到这批程序集。
COPY --from=build --chown=market:market /app .
ENTRYPOINT ["dotnet", "VelaShell.Market.Api.dll"]

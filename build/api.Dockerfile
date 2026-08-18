# 市场 API 的镜像。构建上下文是仓库根目录 —— 因为 ./packages 里有 VelaShell SDK 的本地包
# (SDK 发到 nuget.org 之后,packages 与 nuget.config 里那条本地源都可以删掉)。
FROM mcr.microsoft.com/dotnet/nightly/sdk:11.0-preview AS build
WORKDIR /src

# 先只拷构建描述文件再 restore:源码改动不会让依赖还原的缓存层失效。
COPY Directory.Build.props Directory.Packages.props global.json nuget.config ./
COPY packages/ ./packages/
COPY src/VelaShell.Market.Domain/*.csproj ./src/VelaShell.Market.Domain/
COPY src/VelaShell.Market.Infrastructure/*.csproj ./src/VelaShell.Market.Infrastructure/
COPY src/VelaShell.Market.Api/*.csproj ./src/VelaShell.Market.Api/
# 容器里没有同级的 VelaShell 仓库,所以走 NuGet 包那条分支(见根 Directory.Build.props)。
RUN dotnet restore src/VelaShell.Market.Api/VelaShell.Market.Api.csproj -p:UseLocalVelaShellSdk=false

COPY src/ ./src/
RUN dotnet publish src/VelaShell.Market.Api/VelaShell.Market.Api.csproj \
    -c Release -o /app --no-restore -p:UseLocalVelaShellSdk=false --nologo

FROM mcr.microsoft.com/dotnet/nightly/aspnet:11.0-preview
WORKDIR /app
# 不用 root 跑:这个进程要解开陌生人上传的压缩包,能少一层权限就少一层。
RUN useradd --system --user-group --no-create-home market && chown -R market:market /app
USER market
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1
EXPOSE 8080
ENTRYPOINT ["dotnet", "VelaShell.Market.Api.dll"]

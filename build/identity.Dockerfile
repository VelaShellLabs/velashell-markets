# 统一认证服务(OpenIddict + MongoDB)的镜像。构建上下文是仓库根目录 ——
# 它要用到根上的 Directory.Build.props / Directory.Packages.props / global.json。
FROM mcr.microsoft.com/dotnet/nightly/sdk:11.0-preview AS build
WORKDIR /src

# 先只拷构建描述文件再 restore:源码改动不会让依赖还原的缓存层失效。
COPY Directory.Build.props Directory.Packages.props global.json nuget.config ./
# nuget.config 里有一条指向 ./packages 的本地源(给 VelaShell SDK 用的)。
# 认证服务不需要那个包,但源路径不存在会让 restore 直接报错,所以建个空目录。
RUN mkdir -p packages
COPY src/VelaShell.Market.Identity/*.csproj ./src/VelaShell.Market.Identity/
RUN dotnet restore src/VelaShell.Market.Identity/VelaShell.Market.Identity.csproj

COPY src/VelaShell.Market.Identity/ ./src/VelaShell.Market.Identity/
RUN dotnet publish src/VelaShell.Market.Identity/VelaShell.Market.Identity.csproj \
    -c Release -o /app --no-restore --nologo

FROM mcr.microsoft.com/dotnet/nightly/aspnet:11.0-preview
WORKDIR /app
# 不用 root 跑。/app/keys 要先建好并归属这个用户:命名卷第一次挂载会继承镜像里
# 该目录的属主,不然容器起来就写不进密钥。
RUN useradd --system --user-group --no-create-home identity \
    && mkdir -p /app/keys \
    && chown -R identity:identity /app
COPY --from=build --chown=identity:identity /app .
USER identity
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "VelaShell.Market.Identity.dll"]

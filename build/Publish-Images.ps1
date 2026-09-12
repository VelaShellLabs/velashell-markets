#!/usr/bin/env pwsh
<#
.SYNOPSIS
    产出插件市场的两个自建镜像(api / web),按需导出成 tar 或推到镜像仓库。

.DESCRIPTION
    两个镜像的来路**不一样**,这是本脚本存在的主要理由:

      api   .NET 工程 → 由 SDK 的容器发布直接产出(`dotnet publish -t:PublishContainer`),
            仓库里没有 api 的 Dockerfile。镜像名、基础镜像、非 root 用户、暴露端口
            全写在 src/VelaShell.Market.Api/VelaShell.Market.Api.csproj 的「容器」段里。
      web   nginx + dist,不是 .NET 工程,SDK 容器发布管不着 → 仍走 build/web.Dockerfile。
            而且 dist/ 由**本机** `bun run build` 产出、镜像只做分发,所以 web 要先打包。

    compose 里两个服务都只写 `image:`、没有 `build:` —— 构建入口只有这一个脚本,
    不会出现"脚本打的镜像"与"compose 顺手打的镜像"两份对不上的产物。

    三种去向,可以任意组合:

      (默认)      推本机 Docker → `docker compose up -d` 直接可用
      -Archive     写成 tar 放进 -OutputDir → 目标机 `docker load -i`
      -Push        推到 -Registry(默认 harbor.easilynet.top),推之前自己先 `docker login`

.PARAMETER Service
    要重建的服务,可多选:api / web。默认两个都做。
    只改了前端时 -Service web,省掉一遍 dotnet publish。

.PARAMETER Tag
    镜像标签,默认 latest。想并存多个版本时才传,例如 -Tag 2026-09-11。

.PARAMETER OutputDir
    -Archive 的产物目录。默认就是本脚本所在的 build/ —— 不写死任何盘符,换台机器照样跑。

.PARAMETER Registry
    镜像仓库地址,默认 harbor.easilynet.top。它只是个默认值 —— **不给 -Push 就不会推**。
    推之前先 `docker login harbor.easilynet.top`:api 走 SDK、web 走 docker push,
    两者都读同一份 ~/.docker/config.json 里的凭据。

.PARAMETER Push
    推到 -Registry。最终地址是 <Registry>/<镜像名>:<Tag>,即默认
    harbor.easilynet.top/velashell/market-api:latest 与 .../velashell/market-web:latest。
    ⚠️ 镜像名的第一段 velashell 是 **Harbor 上的项目名**,Harbor 只认已经建好的项目、
    也不会自动创建;项目不存在或者名字是单段的,推送会被直接拒。

.PARAMETER Archive
    导出 tar 到 -OutputDir。

.PARAMETER SkipLocal
    不推本机镜像库。只对 api 有意义 —— web 的导出与推送都建立在本机镜像上。

.PARAMETER SkipWebBundle
    跳过 `bun run build`,直接用现有的 dist/。前端没改动时省几十秒。

.PARAMETER Pull
    构建 web 前拉一遍 nginx:alpine。默认不拉:BuildKit 在这台机器上解析不了 docker.io 的
    metadata(auth.docker.io 连不上),加了 --pull 反而必挂。
    基础镜像缺了的话,先手动 `docker pull nginx:alpine` 再回来构建。
    (api 不受影响 —— 它的基础镜像在 mcr.microsoft.com,那边是通的。)

.PARAMETER NoCache
    web 的构建不用缓存。对 api 无效:SDK 容器发布的增量由 MSBuild 自己管。

.PARAMETER Configuration
    api 的编译配置,默认 Release。

.EXAMPLE
    pwsh ./build/Publish-Images.ps1
    两个镜像进本机 Docker,接着 docker compose up -d 就行。

.EXAMPLE
    pwsh ./build/Publish-Images.ps1 -Service api
    只重出 api。改了后端代码的日常循环。

.EXAMPLE
    pwsh ./build/Publish-Images.ps1 -Archive -OutputDir Z:\velashell-market
    本机镜像照出,另外把两个 tar 直接落到已挂载的共享目录。目标机上:
        docker load -i velashell-market-api.tar.gz
        docker load -i velashell-market-web.tar
        docker compose up -d

.EXAMPLE
    pwsh ./build/Publish-Images.ps1 -Push
    两个镜像构建并推到 harbor.easilynet.top/velashell/market-api:latest
    与 .../velashell/market-web:latest,同时在本机留一份。
    先 `docker login harbor.easilynet.top`。

.EXAMPLE
    pwsh ./build/Publish-Images.ps1 -Service web -Push -Tag 2026-09-12
    只改了前端时:重打 web 并推一个带日期的版本上去。
#>
[CmdletBinding()]
param(
    # 不用 ValidateSet:`pwsh ./build/Publish-Images.ps1 -Service api,web` 走的是 -File,
    # 逗号列表会原样当成一个字符串塞进来,ValidateSet 直接判死。下面自己拆自己校。
    [string[]]$Service = @('api', 'web'),

    [string]$Tag = 'latest',

    # 默认导到 build/ 自己身上,不写死任何盘符 —— 换台机器直接就能跑。
    [string]$OutputDir = $PSScriptRoot,

    [string]$Registry = 'harbor.easilynet.top',

    [switch]$Push,

    [switch]$Archive,

    [switch]$SkipLocal,

    [switch]$SkipWebBundle,

    [switch]$Pull,

    [switch]$NoCache,

    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# 各版本 pwsh 对"原生命令写了 stderr 算不算错"的默认值不一样,统一关掉,
# 下面一律以退出码为准 —— docker build 与 dotnet restore 的进度本来就走 stderr,不关会假报错。
$PSNativeCommandUseErrorActionPreference = $false

$RepoRoot = Split-Path -Parent $PSScriptRoot
$WebProject = Join-Path $RepoRoot 'src/VelaShell.Market.Web'

if ($SkipLocal -and -not ($Archive -or $Push)) {
    throw '-SkipLocal 又不 -Archive、也不 -Push,那就什么都不会产出。'
}

# Registry 有默认值,所以"推不推"只看 -Push —— 否则每次本机构建都会顺手推一趟 Harbor。
if ($Push -and -not $Registry) { throw '-Push 了但 -Registry 是空的。' }

# ---------------------------------------------------------------- 小工具

function Invoke-Native {
    <# 跑外部命令,退出码非 0 就抛。输出直接透到当前控制台,不做缓冲。 #>
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$Arguments,
        [string]$WorkingDirectory = $RepoRoot
    )
    Push-Location $WorkingDirectory
    try {
        Write-Verbose "> $FilePath $($Arguments -join ' ')"
        & $FilePath @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "$FilePath $($Arguments -join ' ') 失败(退出码 $LASTEXITCODE)"
        }
    }
    finally {
        Pop-Location
    }
}

function Format-Size {
    param([long]$Bytes)
    if ($Bytes -ge 1GB) { return '{0:N2} GB' -f ($Bytes / 1GB) }
    if ($Bytes -ge 1MB) { return '{0:N1} MB' -f ($Bytes / 1MB) }
    return '{0:N0} KB' -f ($Bytes / 1KB)
}

function Write-Step {
    param([string]$Message)
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

# ---------------------------------------------------------------- 目录

# Repository 要与 compose 的 image: 以及目标机上的部署文件对得上。
# api 的那一份同时也写在 csproj 的 ContainerRepository 里 —— 这里传参覆盖它,
# 好让 -Tag / -Registry 这些开关只在一处生效;两边的默认值要保持一致。
#
# 第一段 velashell 是 **Harbor 上的项目名**(harbor.easilynet.top/velashell/...),
# 认证等别的服务也在同一个项目里,所以第二段带 market- 前缀把自己认出来。
# 项目名变了就得三处一起改:这里、csproj、compose。
$Catalog = [ordered]@{
    api = @{
        Kind       = 'dotnet'
        Repository = 'velashell/market-api'
        TarName    = 'velashell-market-api.tar.gz'
        Project    = 'src/VelaShell.Market.Api/VelaShell.Market.Api.csproj'
    }
    web = @{
        Kind       = 'docker'
        Repository = 'velashell/market-web'
        TarName    = 'velashell-market-web.tar'
        Context    = 'src/VelaShell.Market.Web'
        Dockerfile = 'build/web.Dockerfile'
    }
}

# -Service 可能是数组,也可能是 -File 传进来的 "api,web" 这种整串,两种都拆开。
$requested = @($Service |
        ForEach-Object { $_ -split '[,;]' } |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ })
$unknown = @($requested | Where-Object { $_ -notin $Catalog.Keys })
if ($unknown) {
    throw "不认识的服务:$($unknown -join ', ')。可选:$(@($Catalog.Keys) -join ', ')。"
}
# 按 Catalog 的顺序处理,不受 -Service 传参顺序影响。
$targets = @($Catalog.Keys | Where-Object { $_ -in $requested })
if (-not $targets) { throw '-Service 没给出任何服务。' }

if ($SkipLocal -and 'web' -in $targets) {
    throw 'web 的导出与推送都基于本机镜像(docker save / docker tag),不能 -SkipLocal。只出 api 的话加 -Service api。'
}

# ---------------------------------------------------------------- 前置检查

if ('api' -in $targets -and -not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '找不到 dotnet。api 的镜像是 SDK 直接出的,没有 Dockerfile 可以退而求其次。'
}

# web 无论去哪都要 docker(它是 docker build 出来的);api 只有在要推本机时才需要。
if (('web' -in $targets) -or (-not $SkipLocal)) {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw '找不到 docker,先装 Docker Desktop 或把 docker 加进 PATH。'
    }
    docker info --format '{{.ServerVersion}}' *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'docker 守护进程没响应,先把 Docker Desktop 起起来。'
    }
}

# 镜像里留个来路标记,目标机上 `docker inspect` 能查到是哪次构建的。
# api 那边 created 标签 SDK 自动会打;source / revision 两条要 PublishRepositoryUrl=true
# 才放行 —— SDK 把它当作"作者同意把仓库信息写进产物"的显式开关,不给就静默不打。
$stamp = Get-Date -Format 'yyyy-MM-ddTHH:mm:ssK'
$commit = ''
if (Get-Command git -ErrorAction SilentlyContinue) {
    $commit = (git -C $RepoRoot rev-parse --short HEAD 2>$null)
    if ($LASTEXITCODE -ne 0) { $commit = '' }
    elseif ((git -C $RepoRoot status --porcelain 2>$null)) { $commit = "$commit-dirty" }
}

if ($Archive) {
    if (-not (Test-Path $OutputDir)) {
        New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    }
    $OutputDir = (Resolve-Path $OutputDir).Path
}

$destinations = @()
if (-not $SkipLocal) { $destinations += '本机 Docker' }
if ($Archive) { $destinations += "tar -> $OutputDir" }
if ($Push) { $destinations += "仓库 -> $Registry" }

Write-Host ''
Write-Host 'VelaShell 插件市场 · 镜像构建与发布' -ForegroundColor Green
Write-Host "  仓库    $RepoRoot$(if ($commit) { "  ($commit)" })"
Write-Host "  服务    $($targets -join ', ')"
Write-Host "  标签    $Tag"
Write-Host "  去向    $($destinations -join ' / ')"

# ---------------------------------------------------------------- 一、前端打包

if ('web' -in $targets -and -not $SkipWebBundle) {
    Write-Step '前端打包(bun run build)'
    if (-not (Get-Command bun -ErrorAction SilentlyContinue)) {
        throw @'
找不到 bun,而 web 镜像只装 dist/、不在容器里构建(见 build/web.Dockerfile)。
要么装 bun,要么先在别处产出 dist/ 后加 -SkipWebBundle 重跑。
'@
    }
    if (-not (Test-Path (Join-Path $WebProject 'node_modules'))) {
        Write-Host '   node_modules 不在,先 bun install' -ForegroundColor DarkGray
        Invoke-Native -FilePath 'bun' -Arguments @('install') -WorkingDirectory $WebProject
    }
    Invoke-Native -FilePath 'bun' -Arguments @('run', 'build') -WorkingDirectory $WebProject
}

if ('web' -in $targets) {
    $distIndex = Join-Path $WebProject 'dist/index.html'
    if (-not (Test-Path $distIndex)) {
        throw "$distIndex 不存在 —— web 镜像会装出一个空站点。先跑一遍 bun run build(去掉 -SkipWebBundle)。"
    }
}

# ---------------------------------------------------------------- 二、构建

function Publish-DotnetImage {
    <#
        SDK 容器发布。三个去向各要跑一趟:给了 ContainerArchiveOutputPath 就只写归档、
        给了 ContainerRegistry 就只推远端,都不给才推本机镜像库 —— 三者互斥。
        编译只发生在第一趟,后面两趟是增量命中,只多花打层/传输的时间。
    #>
    param(
        [Parameter(Mandatory)][hashtable]$Svc,
        [string]$ExtraProperty
    )
    $publishArgs = @(
        'publish', $Svc.Project,
        '-c', $Configuration,
        '-t:PublishContainer',
        '--nologo',
        "-p:ContainerRepository=$($Svc.Repository)",
        "-p:ContainerImageTag=$Tag"
    )
    if ($commit) { $publishArgs += @('-p:PublishRepositoryUrl=true', "-p:SourceRevisionId=$commit") }
    if ($ExtraProperty) { $publishArgs += $ExtraProperty }
    Invoke-Native -FilePath 'dotnet' -Arguments $publishArgs
}

$built = [System.Collections.Generic.List[object]]::new()

foreach ($name in $targets) {
    $svc = $Catalog[$name]
    $image = "$($svc.Repository):$Tag"

    if (-not $SkipLocal) {
        Write-Step "构建 $image"
        if ($svc.Kind -eq 'dotnet') {
            # LocalRegistry=Docker 钉在 csproj 里:这台机器上除了 Docker Desktop 还有 WSL 的
            # 容器存储,让 SDK 自己探测会挑中后者,镜像就进了 `docker images` 看不见的地方。
            Publish-DotnetImage -Svc $svc
        }
        else {
            $buildArgs = @(
                'build',
                '--file', $svc.Dockerfile,
                '--tag', $image,
                '--label', "org.opencontainers.image.created=$stamp"
            )
            if ($commit) { $buildArgs += @('--label', "org.opencontainers.image.revision=$commit") }
            if ($Pull) { $buildArgs += '--pull' }
            if ($NoCache) { $buildArgs += '--no-cache' }
            $buildArgs += $svc.Context
            Invoke-Native -FilePath 'docker' -Arguments $buildArgs
        }
    }

    $built.Add([pscustomobject]@{
            Service  = $name
            Image    = $image
            TarName  = $svc.TarName
            FileSize = 0L
            Pushed   = ''
        })
}

# ---------------------------------------------------------------- 三、导出

if ($Archive) {
    foreach ($item in $built) {
        $svc = $Catalog[$item.Service]
        $tarPath = Join-Path $OutputDir $item.TarName
        Write-Step "导出 $($item.Image) -> $($item.TarName)"

        if ($svc.Kind -eq 'dotnet') {
            # SDK 按镜像名建目录:velashell/market-api 会落成 <dir>/velashell/market-api.tar.gz。
            # 目标机上的部署目录要的是扁平文件名,所以先导到临时目录再搬平。
            $staging = Join-Path ([System.IO.Path]::GetTempPath()) ('vela-img-' + [guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $staging -Force | Out-Null
            try {
                Publish-DotnetImage -Svc $svc -ExtraProperty "-p:ContainerArchiveOutputPath=$staging"
                $produced = Get-ChildItem -Path $staging -Recurse -File -Filter '*.tar.gz' | Select-Object -First 1
                if (-not $produced) { throw "SDK 说导出成功,但 $staging 下没有 .tar.gz。" }
                Move-Item -LiteralPath $produced.FullName -Destination $tarPath -Force
            }
            finally {
                Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
            }
        }
        else {
            Invoke-Native -FilePath 'docker' -Arguments @('save', '--output', $tarPath, $item.Image)
        }

        # -Force 不能省:SMB 共享会给新建的文件打上隐藏属性,不加就是 "Could not find item",
        # 而文件其实好好地在那儿。
        $item.FileSize = (Get-Item -LiteralPath $tarPath -Force).Length
        Write-Host "   $(Format-Size $item.FileSize)" -ForegroundColor DarkGray
    }
}

# ---------------------------------------------------------------- 四、推仓库

if ($Push) {
    $registryHost = $Registry.TrimEnd('/')
    foreach ($item in $built) {
        $svc = $Catalog[$item.Service]
        $remote = "$registryHost/$($svc.Repository):$Tag"
        Write-Step "推送 -> $remote"

        if ($svc.Kind -eq 'dotnet') {
            Publish-DotnetImage -Svc $svc -ExtraProperty "-p:ContainerRegistry=$registryHost"
        }
        else {
            Invoke-Native -FilePath 'docker' -Arguments @('tag', $item.Image, $remote)
            Invoke-Native -FilePath 'docker' -Arguments @('push', $remote)
        }
        $item.Pushed = $remote
    }
}

# ---------------------------------------------------------------- 收尾

Write-Host ''
Write-Host '完成' -ForegroundColor Green
$built | Format-Table -AutoSize @(
    @{ Label = '服务'; Expression = { $_.Service } },
    @{ Label = '镜像'; Expression = { $_.Image } },
    @{ Label = '产物'; Expression = { if ($_.FileSize) { "$($_.TarName)  $(Format-Size $_.FileSize)" } else { '(未导出)' } } },
    @{ Label = '仓库'; Expression = { if ($_.Pushed) { $_.Pushed } else { '-' } } }
)

if (-not $SkipLocal) {
    Write-Host '本机起服务:'
    Write-Host "  docker compose up -d $($targets -join ' ')"
}
if ($Archive) {
    # 没重建的那几个 tar 还是上一次的,提一句免得以为整套都是新的。
    $stale = @($Catalog.Keys | Where-Object { $_ -notin $targets })
    if ($stale) {
        Write-Host "注意:$($stale -join ', ') 的 tar 还是上一次导出的,没有重建。" -ForegroundColor Yellow
    }
    Write-Host "产物目录:$OutputDir" -ForegroundColor Yellow
    Write-Host '复制到目标机之后逐个 docker load -i,再 docker compose up -d。'
}
if ($Push) {
    Write-Host '目标机上(.env 里写这两行,compose 的默认值是本机镜像名):' -ForegroundColor Yellow
    foreach ($item in $built) {
        if (-not $item.Pushed) { continue }
        $var = if ($item.Service -eq 'api') { 'MARKET_API_IMAGE' } else { 'MARKET_WEB_IMAGE' }
        Write-Host "  $var=$($item.Pushed)"
    }
    Write-Host '  docker compose pull && docker compose up -d'
}

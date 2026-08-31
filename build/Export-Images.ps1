#!/usr/bin/env pwsh
<#
.SYNOPSIS
    构建市场的三个自建镜像(api / identity / web)并导出到 NAS 的部署目录。

.DESCRIPTION
    做两件事:

      1. 前端先在本机打包。build/web.Dockerfile 只做分发、不在容器里构建,
         镜像里的 dist/ 来自宿主机 —— 所以 web 必须先跑一遍 `bun run build`。
      2. 按 docker-compose.yml 里同一套上下文/Dockerfile 构建镜像,打上
         velashell-market/<服务>:latest,再 `docker save` 到 -OutputDir。

    产物文件名与镜像名照 NAS 上那份部署目录的既有约定:

        velashell-market-api.tar        →  velashell-market/api:latest
        velashell-market-web.tar        →  velashell-market/web:latest

    那边的 docker-compose.yml 里三个服务已经是 `image: velashell-market/xxx:latest`、
    没有 build:,load-images.sh 也已经在。所以这个脚本只管把 tar 出出来,
    人工复制到部署目录之后,NAS 上照旧两条命令:

        bash load-images.sh
        docker compose up -d

    mongo / minio / clamav 是公开镜像,不导出,目标机自己 pull。

    `docker save` 在 containerd 镜像存储下导出的已经是压缩过的层(每个约 110MB),
    再套一层 gzip 基本没收益,所以不压。

.PARAMETER Service
    要重建的服务,可多选:api / identity / web。默认三个全做。
    只改了前端时 -Service web,省掉两遍 dotnet publish。

.PARAMETER OutputDir
    产物目录。默认就是本脚本所在的 build/ 目录 —— 换台机器也照样能跑,
    不依赖任何盘符映射。导出完自己复制到 NAS 的部署目录去。

.PARAMETER SkipWebBundle
    跳过 `bun run build`,直接用现有的 dist/。前端没改动时省几十秒。

.PARAMETER Pull
    构建前拉一遍基础镜像。默认不拉:BuildKit 在这台机器上解析不了 docker.io 的
    metadata(auth.docker.io 连不上),加了 --pull 反而必挂。
    基础镜像缺了的话,先手动 `docker pull nginx:alpine` 再回来构建。

.PARAMETER NoCache
    不用构建缓存,整个重来一遍。

.PARAMETER SkipSave
    只构建不导出,本机验证时用。

.EXAMPLE
    pwsh ./build/Export-Images.ps1
    三个镜像全量重建,tar 覆盖到 build/ 下。

.EXAMPLE
    pwsh ./build/Export-Images.ps1 -OutputDir Z:\velashell-market
    直接导到已挂载的 NAS 共享,省掉复制那一步(共享没挂就别用这个)。

.EXAMPLE
    pwsh ./build/Export-Images.ps1 -Service web
    只重出前端。

.EXAMPLE
    pwsh ./build/Export-Images.ps1 -Service api,identity -SkipWebBundle
    后端两个,不碰前端产物。
#>
[CmdletBinding()]
param(
    # 不用 ValidateSet:`pwsh ./build/Export-Images.ps1 -Service api,identity` 走的是 -File,
    # 逗号列表会原样当成一个字符串塞进来,ValidateSet 直接判死。下面自己拆自己校。
    [string[]]$Service = @('api', 'web'),

    # 默认导到 build/ 自己身上,不写死任何盘符 —— 换台机器直接就能跑。
    # 复制到 NAS 那一步人工做。
    [string]$OutputDir = $PSScriptRoot,

    [switch]$SkipWebBundle,

    [switch]$Pull,

    [switch]$NoCache,

    [switch]$SkipSave
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# 各版本 pwsh 对"原生命令写了 stderr 算不算错"的默认值不一样,统一关掉,
# 下面一律以退出码为准 —— docker build 的进度本来就走 stderr,不关会假报错。
$PSNativeCommandUseErrorActionPreference = $false

$RepoRoot = Split-Path -Parent $PSScriptRoot
$WebProject = Join-Path $RepoRoot 'src/VelaShell.Market.Web'

# ---------------------------------------------------------------- 小工具

function Invoke-Native {
    <# 跑外部命令,退出码非 0 就抛。docker 的输出直接透到当前控制台,不做缓冲。 #>
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

# ---------------------------------------------------------------- 前置检查

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw '找不到 docker,先装 Docker Desktop 或把 docker 加进 PATH。'
}
docker info --format '{{.ServerVersion}}' *> $null
if ($LASTEXITCODE -ne 0) {
    throw 'docker 守护进程没响应,先把 Docker Desktop 起起来。'
}

# ---------------------------------------------------------------- 构建目录

# 上下文与 Dockerfile 与 docker-compose.yml 保持一致:
# api / identity 的上下文是仓库根(要用根上的 Directory.*.props 和 global.json),
# web 的上下文是前端项目目录(镜像只 COPY dist/ 和 nginx.conf.template)。
# Image / TarName 则要与 NAS 部署目录里的 docker-compose.yml 和 load-images.sh 对上。
$Catalog = [ordered]@{
    api      = @{
        Image      = 'velashell-market/api:latest'
        TarName    = 'velashell-market-api.tar'
        Context    = '.'
        Dockerfile = 'build/api.Dockerfile'
    }
    web      = @{
        Image      = 'velashell-market/web:latest'
        TarName    = 'velashell-market-web.tar'
        Context    = 'src/VelaShell.Market.Web'
        Dockerfile = 'build/web.Dockerfile'
    }
}

# -Service 可能是数组,也可能是 -File 传进来的 "api,identity" 这种整串,两种都拆开。
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

# 镜像里留个来路标记,NAS 上 `docker inspect` 能查到是哪次构建的。
$stamp = Get-Date -Format 'yyyy-MM-ddTHH:mm:ssK'
$commit = ''
if (Get-Command git -ErrorAction SilentlyContinue) {
    $commit = (git -C $RepoRoot rev-parse --short HEAD 2>$null)
    if ($LASTEXITCODE -ne 0) { $commit = '' }
    elseif ((git -C $RepoRoot status --porcelain 2>$null)) { $commit = "$commit-dirty" }
}

Write-Host ''
Write-Host 'VelaShell 插件市场 · 镜像构建与导出' -ForegroundColor Green
Write-Host "  仓库    $RepoRoot$(if ($commit) { "  ($commit)" })"
Write-Host "  服务    $($targets -join ', ')"
Write-Host "  输出    $(if ($SkipSave) { '(跳过导出)' } else { $OutputDir })"

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

# ---------------------------------------------------------------- 二、构建镜像

$built = [System.Collections.Generic.List[object]]::new()

foreach ($name in $targets) {
    $svc = $Catalog[$name]
    Write-Step "构建 $($svc.Image)"

    $buildArgs = @(
        'build',
        '--file', $svc.Dockerfile,
        '--tag', $svc.Image,
        '--label', "org.opencontainers.image.created=$stamp",
        '--label', "org.opencontainers.image.revision=$commit"
    )
    if ($Pull) { $buildArgs += '--pull' }
    if ($NoCache) { $buildArgs += '--no-cache' }
    $buildArgs += $svc.Context

    Invoke-Native -FilePath 'docker' -Arguments $buildArgs

    $built.Add([pscustomobject]@{
            Service  = $name
            Image    = $svc.Image
            TarName  = $svc.TarName
            Size     = [long](docker image inspect $svc.Image --format '{{.Size}}')
            FileSize = 0L
        })
}

# ---------------------------------------------------------------- 三、导出

if (-not $SkipSave) {
    if (-not (Test-Path $OutputDir)) {
        New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    }

    foreach ($item in $built) {
        $tarPath = Join-Path $OutputDir $item.TarName
        Write-Step "导出 $($item.Image) → $($item.TarName)"
        Invoke-Native -FilePath 'docker' -Arguments @('save', '--output', $tarPath, $item.Image)
        # -Force 不能省:NAS 那个 SMB 共享会给新建的文件打上隐藏属性,
        # 不加就是 "Could not find item",而文件其实好好地在那儿。
        $item.FileSize = (Get-Item -LiteralPath $tarPath -Force).Length
        Write-Host "   $(Format-Size $item.FileSize)" -ForegroundColor DarkGray
    }
}

# ---------------------------------------------------------------- 收尾

Write-Host ''
Write-Host '完成' -ForegroundColor Green
$built | Format-Table -AutoSize @(
    @{ Label = '服务'; Expression = { $_.Service } },
    @{ Label = '镜像'; Expression = { $_.Image } },
    @{ Label = '产物'; Expression = { if ($_.FileSize) { "$($_.TarName)  $(Format-Size $_.FileSize)" } else { '(未导出)' } } }
)

if (-not $SkipSave) {
    # 没重建的那几个 tar 还是上一次的,提一句免得以为整套都是新的。
    $stale = @($Catalog.Keys | Where-Object { $_ -notin $targets })
    if ($stale) {
        Write-Host "注意:$($stale -join ', ') 的 tar 还是上一次导出的,没有重建。" -ForegroundColor Yellow
    }
    Write-Host "产物目录:$OutputDir" -ForegroundColor Yellow
    Write-Host '把上面这几个 tar 复制到 NAS 的部署目录,然后在那边:'
    Write-Host '  bash load-images.sh'
    Write-Host "  docker compose up -d $($targets -join ' ')"
}

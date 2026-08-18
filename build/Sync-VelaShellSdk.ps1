<#
.SYNOPSIS
    把同级 VelaShell 仓库里的插件 SDK 打成 NuGet 包,放进本仓库的 ./packages 本地源。

.DESCRIPTION
    市场解析 .vpx 用的是宿主那一份 VelaShell.PluginSdk —— 两边必须是同一份实现,
    否则"市场收得下、宿主装不上"是迟早的事。

    本机开发时根 Directory.Build.props 会直接引同级仓库的工程,不需要跑这个脚本;
    **docker 构建**看不到同级仓库,只能走 NuGet,因此镜像构建前需要先执行一次本脚本。
    等 SDK 发布到 nuget.org 之后,这个脚本与 nuget.config 里那条本地源都可以删掉。
#>
[CmdletBinding()]
param(
    [string]$VelaShellRepo = (Join-Path $PSScriptRoot '../../VelaShell'),
    [string]$Output = (Join-Path $PSScriptRoot '../packages')
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $VelaShellRepo 'plugin-sdk/VelaShell.PluginSdk/VelaShell.PluginSdk.csproj'
if (-not (Test-Path $project)) {
    throw "找不到 VelaShell 插件 SDK 工程:$project。用 -VelaShellRepo 指定仓库位置。"
}
New-Item -ItemType Directory -Force $Output | Out-Null
# 用 Debug 打包:Release 会要求强名称密钥(src/VelaShell.snk),那把钥匙只在 CI 里有。
# 本地源只服务于容器构建,签不签名不影响功能。
dotnet pack $project -c Debug -o $Output --nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet pack 失败。' }
Get-ChildItem $Output -Filter '*.nupkg' | Select-Object -ExpandProperty Name

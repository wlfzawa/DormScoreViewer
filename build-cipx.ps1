<#
.SYNOPSIS
    编译并打包「寝室扣分查看」ClassIsland 插件为 .cipx 插件包。

.DESCRIPTION
    优先使用插件 SDK 的一键打包（CreateCipx=true，SDK 1.6.0.5+）；
    若当前 SDK 不支持，则自动回退为手动压缩 publish 输出目录。
    产物输出在项目目录下的 cipx 文件夹中。

.EXAMPLE
    .\build-cipx.ps1
    .\build-cipx.ps1 -Configuration Debug
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$OutputDir = "cipx"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

function Fail($msg) {
    Write-Host "`n[失败] $msg" -ForegroundColor Red
    exit 1
}

# ---- 读取目标框架（兼容 net10.0 / net8.0 两种配置）----
$csproj = Get-ChildItem -Path $root -Filter "*.csproj" | Select-Object -First 1
if (-not $csproj) { Fail "未找到 .csproj 项目文件" }

$tfm = ([xml](Get-Content $csproj.FullName)).Project.PropertyGroup.TargetFramework
if (-not $tfm) { $tfm = "net10.0-windows" }
Write-Host "项目: $($csproj.Name)    目标框架: $tfm" -ForegroundColor Gray

# ---- 1. 还原 ----
Write-Host "`n[1/3] 还原 NuGet 依赖..." -ForegroundColor Cyan
dotnet restore
if ($LASTEXITCODE -ne 0) { Fail "还原依赖失败，请检查能否访问 NuGet（或已配置镜像源）" }

# ---- 2. 编译发布（顺带尝试一键打包）----
Write-Host "[2/3] 编译发布（$Configuration）..." -ForegroundColor Cyan
dotnet publish -c $Configuration -p:CreateCipx=true
if ($LASTEXITCODE -ne 0) { Fail "编译失败，请查看上方错误信息" }

# ---- 3. 定位 / 生成插件包 ----
Write-Host "[3/3] 生成插件包..." -ForegroundColor Cyan

if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir | Out-Null }

$existing = Get-ChildItem -Path $OutputDir -Filter "*.cipx" -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1

if ($existing) {
    Write-Host "`n[完成] 插件包: $($existing.FullName)" -ForegroundColor Green
    Write-Host "       大小: $([math]::Round($existing.Length / 1KB, 1)) KB" -ForegroundColor Gray
    Write-Host "`n在 ClassIsland 中打开【应用设置】->【插件】安装此包即可。" -ForegroundColor Yellow
    exit 0
}

# ---- 回退：手动压缩 publish 输出目录 ----
Write-Host "未检测到一键打包产物，改用手动打包..." -ForegroundColor Yellow

$publishDir = Join-Path $root "bin\$Configuration\$tfm\publish"
if (-not (Test-Path $publishDir)) {
    $publishDir = Get-ChildItem -Path (Join-Path $root "bin") -Directory -Recurse -Filter "publish" -ErrorAction SilentlyContinue |
                  Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($publishDir) { $publishDir = $publishDir.FullName }
}

if (-not $publishDir -or -not (Test-Path $publishDir)) { Fail "未找到 publish 输出目录" }

$manifest = Join-Path $publishDir "manifest.yml"
if (-not (Test-Path $manifest)) { Fail "publish 输出目录中缺少 manifest.yml" }

$pkgName = "DormScoreViewer.cipx"
$pkgPath = Join-Path $root (Join-Path $OutputDir $pkgName)
if (Test-Path $pkgPath) { Remove-Item $pkgPath -Force }

# cipx 本质是 zip，且 manifest.yml 必须位于压缩包根目录
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $pkgPath -Force

$pkg = Get-Item $pkgPath
Write-Host "`n[完成] 插件包: $($pkg.FullName)" -ForegroundColor Green
Write-Host "       大小: $([math]::Round($pkg.Length / 1KB, 1)) KB" -ForegroundColor Gray
Write-Host "`n在 ClassIsland 中打开【应用设置】->【插件】安装此包即可。" -ForegroundColor Yellow

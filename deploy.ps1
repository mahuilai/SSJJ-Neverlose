param(
    [string]$GameRoot = "",
    [ValidateSet("Debug","Release")][string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$Package = Join-Path $Root "bin\package\$Configuration"
$PluginDll = Join-Path $Package "plugins\SingleSkinMod.dll"
$AssetsDir = Join-Path $Package "SingleSkinMod"

# 自动推导游戏目录
if (-not $GameRoot) {
    if ($env:SSJJ_GAME_DIR -and (Test-Path $env:SSJJ_GAME_DIR)) {
        $GameRoot = $env:SSJJ_GAME_DIR
    } elseif (Test-Path "D:\SSJJ-4399\battle\10_64") {
        $GameRoot = "D:\SSJJ-4399\battle\10_64"
    } elseif (Test-Path "D:\SSJJ-4399\battle\16_64") {
        $GameRoot = "D:\SSJJ-4399\battle\16_64"
    } else {
        throw "未指定且无法自动推导游戏目录！请指定 -GameRoot 参数，例如: .\deploy.ps1 -GameRoot 'D:\YourGamePath'"
    }
}

if (-not (Test-Path $PluginDll)) {
    throw "未找到编译产物: $PluginDll。请先运行 .\build.ps1 进行编译！"
}

if (-not (Test-Path $GameRoot)) {
    throw "未找到游戏目标目录: $GameRoot"
}

$proc = Get-Process -Name "SSJJ_BattleClient_Unity" -ErrorAction SilentlyContinue
if ($proc) {
    Write-Warning "检测到生死狙击游戏正在运行中 (PID=$($proc.Id))。为了防止文件占用，建议退出游戏后再同步。"
}

$targetPlugins = Join-Path $GameRoot "BepInEx\plugins"
if (-not (Test-Path $targetPlugins)) {
    New-Item -ItemType Directory -Force -Path $targetPlugins | Out-Null
}

$destDll = Join-Path $targetPlugins "SingleSkinMod.dll"
if (Test-Path $destDll) {
    $backupDir = Join-Path $GameRoot "SingleSkinMod_Backups"
    if (-not (Test-Path $backupDir)) { New-Item -ItemType Directory -Force -Path $backupDir | Out-Null }
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    Copy-Item -LiteralPath $destDll -Destination (Join-Path $backupDir "SingleSkinMod_$stamp.dll") -Force
}

Copy-Item -LiteralPath $PluginDll -Destination $destDll -Force
Write-Host "[√] 插件主文件已同步: $destDll" -ForegroundColor Green

# 清理可能锁定旧版窗口尺寸的 imgui.ini
$oldIni = Join-Path $GameRoot "imgui.ini"
if (Test-Path $oldIni) {
    Remove-Item -LiteralPath $oldIni -Force -ErrorAction SilentlyContinue
    Write-Host "[√] 已清理旧版窗口尺寸缓存: $oldIni" -ForegroundColor Green
}

$destAssets = Join-Path $GameRoot "SingleSkinMod"
if (-not (Test-Path $destAssets)) {
    New-Item -ItemType Directory -Force -Path $destAssets | Out-Null
}
if (Test-Path $AssetsDir) {
    Copy-Item -Path "$AssetsDir\*" -Destination $destAssets -Recurse -Force
    Write-Host "[√] 模组素材已同步至: $destAssets" -ForegroundColor Green
}

# 部署原生反截图免检标志
$avoidFile = Join-Path $GameRoot "AvoidCapture.txt"
if (-not (Test-Path $avoidFile)) {
    New-Item -ItemType File -Force -Path $avoidFile | Out-Null
    Write-Host "[√] 已激活原生反截图免检标志: $avoidFile" -ForegroundColor Green
}

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "生死狙击 单美化模组 (SingleSkinMod) 部署成功！" -ForegroundColor Green
Write-Host "游戏目录: $GameRoot" -ForegroundColor Yellow
Write-Host "操作快捷键:" -ForegroundColor White
Write-Host "  [Home] / [F12] : 呼出 / 隐藏 C++ DXGI 硬件加速中文菜单" -ForegroundColor Yellow
Write-Host "  [F3]           : 快捷切换第三人称 (TPS) 视角" -ForegroundColor Yellow
Write-Host "  [F5]           : 局内一键应用换模与皮肤" -ForegroundColor Yellow
Write-Host "  [F6]           : 一键还原玩家原始官方模型" -ForegroundColor Yellow
Write-Host "==========================================" -ForegroundColor Cyan

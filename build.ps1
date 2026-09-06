param(
    [ValidateSet("Debug","Release")][string]$Configuration = "Release",
    [string]$GameDir = "",
    [switch]$SkipNative,
    [switch]$SkipManaged
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$NativeDir = Join-Path $Root "src\SingleSkinMod.Native"
$CoreDir = Join-Path $Root "src\SingleSkinMod.Core"
$NativeOut = Join-Path $Root "bin\native\$Configuration\SingleSkinMod.Native.dll"
$ManagedOut = Join-Path $Root "bin\managed\$Configuration\SingleSkinMod.dll"
$Package = Join-Path $Root "bin\package\$Configuration"

function Invoke-NativeBuild {
    Write-Host "[1/2] 正在编译 Native C++ 核心 (DXGI Hook + ImGui)..." -ForegroundColor Cyan
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) { throw "未找到 Visual Studio Installer/vswhere.exe，请先安装 VS2022 C++ 编译工具链" }
    $vs = & $vswhere -all -products "*" -property installationPath | Select-Object -First 1
    if (-not $vs) { throw "未找到 Visual Studio C++ 工具链" }
    $vsDevCmd = Join-Path $vs "Common7\Tools\VsDevCmd.bat"

    $outDir = Split-Path -Parent $NativeOut
    $objDir = Join-Path $Root "obj\native\$Configuration"
    New-Item -ItemType Directory -Force -Path $outDir,$objDir | Out-Null

    $srcNames = @(
        "native.cpp", "gui.cpp", "third_party\imgui\imgui.cpp", "third_party\imgui\imgui_draw.cpp",
        "third_party\imgui\imgui_tables.cpp", "third_party\imgui\imgui_widgets.cpp",
        "third_party\imgui\imgui_impl_dx11.cpp", "third_party\imgui\imgui_impl_win32.cpp",
        "third_party\minhook\src\buffer.c", "third_party\minhook\src\hook.c",
        "third_party\minhook\src\trampoline.c", "third_party\minhook\src\hde\hde64.c"
    )
    $srcList = ($srcNames | ForEach-Object { '"' + $_ + '"' }) -join " "

    $cmd = @(
        "@echo off",
        "chcp 65001 >nul",
        "call `"$vsDevCmd`" -arch=x64 >nul",
        "cd /d `"$NativeDir`"",
        "cl /nologo /LD /std:c++17 /EHsc /utf-8 /MT /O2 /DWIN32 /D_WINDOWS /I`".`" /I`"third_party\imgui`" /I`"third_party\minhook\include`" /I`"third_party\minhook\src`" /Fo`"$objDir\\`" $srcList /link /OUT:`"$NativeOut`" d3d11.lib dxgi.lib d3dcompiler.lib user32.lib"
    )
    $cmdFile = Join-Path $env:TEMP "singleskin-native-build.cmd"
    [System.IO.File]::WriteAllLines($cmdFile, $cmd, [System.Text.Encoding]::UTF8)
    cmd.exe /c "`"$cmdFile`""
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $NativeOut)) {
        throw "Native C++ 编译失败！"
    }
    Write-Host "Native C++ 编译成功: $NativeOut" -ForegroundColor Green
}

function Invoke-ManagedBuild {
    Write-Host "[2/2] 正在编译 Managed C# 插件 (内嵌 Native 核心)..." -ForegroundColor Cyan
    if (-not (Test-Path $NativeOut)) { throw "缺少 Native DLL，请先编译 Native: $NativeOut" }

    $csproj = Join-Path $CoreDir "SingleSkinMod.Core.csproj"
    $extraArgs = @()
    if ($GameDir -and (Test-Path $GameDir)) {
        $extraArgs += "-p:GameDir=`"$GameDir`""
    }
    dotnet build $csproj -c $Configuration -p:Platform=x64 @extraArgs
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $ManagedOut)) {
        throw "Managed C# 编译失败！"
    }
    Write-Host "Managed C# 编译成功: $ManagedOut" -ForegroundColor Green
}

function Build-Package {
    Write-Host "正在打包发布包到: $Package" -ForegroundColor Cyan
    if (Test-Path $Package) { Remove-Item -LiteralPath $Package -Recurse -Force }
    New-Item -ItemType Directory -Force -Path "$Package\plugins", "$Package\SingleSkinMod" | Out-Null

    # 1. Main Plugin DLL
    Copy-Item -LiteralPath $ManagedOut -Destination "$Package\plugins\SingleSkinMod.dll" -Force

    # 2. Assets
    $assets = Join-Path $Root "assets"
    if (Test-Path $assets) {
        Copy-Item -Path "$assets\*" -Destination "$Package\SingleSkinMod" -Recurse -Force
    }

    Write-Host "==========================================" -ForegroundColor Green
    Write-Host "全部生成完成！" -ForegroundColor Green
    Write-Host "插件主文件: $Package\plugins\SingleSkinMod.dll" -ForegroundColor Yellow
    Write-Host "素材目录:   $Package\SingleSkinMod" -ForegroundColor Yellow
    Write-Host "运行 .\deploy.ps1 可一键同步至游戏目录！" -ForegroundColor Cyan
}

if (-not $SkipNative) { Invoke-NativeBuild }
if (-not $SkipManaged) { Invoke-ManagedBuild }
Build-Package

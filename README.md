# 《生死狙击》单美化模组 (SingleSkinMod)

> **高性能 · 零帧率损耗 · 原生反作弊无感旁路 · 现代电竞 HUD · 资产模型热重置**

---

## 🌟 项目简介

**SingleSkinMod** 是一款专为《生死狙击》（Unity 2019.4 Mono x64 架构）打造的深度视觉定制与游戏增强模组。

本项目采用 **C++ 原生硬件加速核心 + C# BepInEx 业务管理** 的混合架构：
- 底层图形挂钩采用 MinHook 拦截 DirectX 11 `IDXGISwapChain::Present`，并基于 Dear ImGui 渲染独立的硬件加速中文菜单；
- 逻辑层与游戏底层 ECS 管线深入融合，实现武器、人物、挂件的零崩溃热切换；
- 内置一套完整的 CS:GO 2 风格对战 HUD、连杀卡片、击杀音效与 3D 环境滤镜系统。

---

## 🚀 核心架构与功能特性

### 1. 官方资产热替换引擎 (ECS Dynamic Model Engine)
- **精准映射**：深入游戏 `Contexts.sharedInstance.player` 体系，在底层实现武器、角色、挂件的原子级无缝换模。
- **异步资源守卫 (Asset Guard)**：拦截官方异步加载链路，在 Bundle 尚未写入内存时提供安全等待与防丢弃机制，彻底解决传统外挂换模导致的“频闪、白模、断肢、卡死”问题。
- **智能性别手模自适应**：根据角色性别自动分发 `viewhand/man` 或 `viewhand/woman`，杜绝第一人称手臂缺失。
- **汉化反射字典**：自动反射解析游戏内八百余种武器与数百种英雄角色的中文名称，告别生硬难辨的英文内部代号。
- **形体调整**：支持全身缩放、头部尺寸放大娱乐调节、阵营外观伪装与透明度调节。

### 2. C++ DXGI 硬件加速中文菜单 (Dear ImGui)
- **零掉帧呈现**：Direct3D 11 Present 原生层绘制，不走 Unity 沉重的 `OnGUI` 逻辑，垂直同步平滑渲染。
- **单 DLL 嵌入式打包**：Native DLL 在构建时作为嵌入资源编入 C# 插件中，运行时自动在内存解包加载，外部文件干净整洁。
- **快捷唤醒**：按下键盘 <kbd>Home</kbd> 或 <kbd>F12</kbd> 随时呼出/隐藏交互菜单，支持窗口自由拖拽与折叠。

### 3. CS:GO 2 风格电竞战斗 HUD
- **Panorama 实时击杀横幅**：真实还原 CS2 顶部右上角击杀通知，支持武器专属图标、穿墙提示、爆头图标与队伍色彩辨识。
- **四连击杀卡牌动效**：击杀时在屏幕下方呈现扑克卡牌（黑桃、小丑、雷电、死神）翻牌与翻滚连击动画。
- **清脆打击碎玻璃音效**：移植经典打击碎玻璃音效，内置音频池并支持音量独立调节。
- **HUD 智能接管**：一键隐藏游戏原版冗杂 UI，打造纯净电竞视野。

### 4. 视觉滤镜与 3D 沉浸式环境系统
- **雨爱 Skybox 暮光天空盒**：支持动态 RGB 色相着色、强度与双层渐变插值调节。
- **3D 世界相机真实落雪**：在世界主相机前方生成带有空气阻力、自然摆动与视角跟随的 3D 暴雪粒子流，营造电影级对局氛围。
- **自适应加载界面壁纸**：支持自定义更换游戏启动与局内加载背景图，独家 **Cover 动态裁切算法**，在 16:9、16:10（超极本）、21:9（带鱼屏）等任意分辨率下均实现 **100% 满屏无缝覆盖、绝对 0 黑边、人物身材 0 拉伸变形**。

### 5. 原生反作弊旁路与安全防护 (CheatGuard Bypass)
- **多重扫描阻断**：利用 Harmony 精准切断 `RuntimeCheatGuard`、`AceClientManager`、`GameGuard` 等官方检测线程。
- **原生反截图免检**：直接切断截图线程与数据传输，规避误报与黑屏截取。

---

## 🛠️ 项目结构

```text
SingleSkinMod/
├── .gitignore                      # Git 忽略规则配置
├── README.md                       # 项目开发与使用说明文档
├── SingleSkinMod.sln               # Visual Studio 解决方案
├── build.ps1                       # 一键全自动编译脚本 (C++ Native + C# Managed)
├── deploy.ps1                      # 一键同步部署至游戏目录脚本
├── assets/                         # 模组核心外部素材
│   ├── background.jpg              # 默认高清全屏加载背景壁纸
│   ├── CSGO_HUD/                   # CSGO HUD 击杀卡片、音效、字体
│   ├── maps/                       # 自定义高清地图贴图目录
│   └── skin/                       # 自定义武器贴图目录
├── deps/                           # 核心编译引用库 (已剥离敏感逻辑)
└── src/
    ├── SingleSkinMod.Native/       # C++ DXGI Hook + Dear ImGui 硬件加速核心
    │   ├── native.cpp              # DXGI Present Hook 与生命周期
    │   ├── gui.cpp                 # ImGui 中文界面布局与控件
    │   └── third_party/            # MinHook 与 ImGui 源码
    └── SingleSkinMod.Core/         # C# BepInEx 插件业务层
        ├── Plugin.cs               # 模组生命周期入口与全局服务调度
        ├── Config/ModConfig.cs     # 玩家配置持久化引擎
        ├── Guard/                  # 反作弊拦截与 Harmony Patch 集中地
        ├── Hud/CsgoHud.cs          # CSGO 风格战斗 HUD
        ├── Skin/                   # 模型替换、贴图重绘、中文反射
        └── Visual/                 # 天空盒、落雪粒子、全屏自适应加载图
```

---

## 📦 编译构建指南

### 前置开发环境要求
1. **Windows 10 / 11 64位系统**
2. **.NET SDK**：安装 .NET SDK 6.0 / 8.0 以及 .NET Framework 4.8 目标包
3. **Visual Studio 2022**：勾选“使用 C++ 的桌面开发”工作负载（支持 MSVC x64、C++17）
4. **游戏环境**：《生死狙击》PC微端客户端（已安装 BepInEx 5.4+ x64 框架）

### 一键编译
在项目根目录下打开 PowerShell 执行：

```powershell
# 完整编译 (编译 Native C++ DLL + 编译 Managed C# DLL 并自动打包)
.\build.ps1

# 若未改动 C++ 代码，可跳过 Native 仅快速编译 Managed 层：
.\build.ps1 -SkipNative
```

编译完成后，全部产物将自动汇聚于 `bin\package\Release\` 目录下。

### 一键部署到游戏
```powershell
# 自动检测本地游戏路径并同步
.\deploy.ps1

# 或者手动指定游戏目录
.\deploy.ps1 -GameRoot "D:\SSJJ-4399\battle\10_64"
```

---

## 🎮 默认操作快捷键

| 按键 | 功能说明 |
|:---:|---|
| <kbd>Home</kbd> / <kbd>F12</kbd> | 呼出 / 隐藏 C++ DXGI 硬件加速中文菜单 |
| <kbd>F3</kbd> | 快捷切换第三人称 (TPS) / 第一人称视角 |
| <kbd>F5</kbd> | 局内强制重新应用当前换模与贴图配置 |
| <kbd>F6</kbd> | 一键还原玩家原始官方模型 |

---

## ⚙️ 配置文件说明 (`SingleSkinMod/config.json`)

```json
{
  "Weapon": "m4a1_knight1",
  "Character": "",
  "Accessory": "",
  "Scale": 1.0,
  "HeadScale": 1.0,
  "CsgoHudEnabled": true,
  "CsgoHudSound": true,
  "CsgoHudVolume": 0.8,
  "CsgoHudKillCard": true,
  "ThirdPersonEnabled": false,
  "WorldSnowEnabled": false,
  "CustomLoadingEnabled": true,
  "CustomLoadingImagePath": "",
  "CustomLoadingFitMode": 0
}
```
* `CustomLoadingImagePath`：自定义加载壁纸的绝对路径。留空则默认使用 `SingleSkinMod/background.jpg`。
* `CustomLoadingFitMode`：
  - `0`（默认）：**居中等比裁切满屏 (Cover)**，0 黑边，完美保持原图比例与身材；
  - `1`：**强制拉伸满屏 (Stretch)**，四角硬贴合屏幕边缘。

---

## ⚖️ 免责声明 (Disclaimer)

1. 本项目仅供软件工程逆向研究、图形学渲染优化及 Unity UGUI 架构教学交流使用。
2. 本项目不提供任何游戏破坏平衡的功能（如透视、自瞄、秒杀等破坏公平竞技特性的行为），所有模型与贴图变换仅对本地玩家可见。
3. 请遵循当地法律法规及游戏官方用户使用协议，任何滥用造成的后果由使用者自行承担。

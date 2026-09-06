# CSGO_HUD 资源包

本项目 CSGO 风格战斗 HUD、击杀卡片动画、打击碎玻璃音效及自定义字体所需的媒体资源目录。

---

## 目录结构

```text
CSGO_HUD/
├── kill_card_1_spade.png      # 连杀卡片 - 黑桃
├── kill_card_2_joker.png      # 连杀卡片 - 小丑
├── kill_card_3_thunder.png    # 连杀卡片 - 雷电
├── kill_card_4_death.png      # 连杀卡片 - 死亡
├── kill_spade_skull.png       # 爆头击杀骷髅图标
├── kill_glass.wav             # 击杀玻璃碎裂音效 (主格式)
├── kill_glass.ogg             # 击杀玻璃碎裂音效 (备用格式)
├── player_avatar_1.png        # 计分板预设头像 1
├── player_avatar_2.png        # 计分板预设头像 2
├── player_avatar_3.png        # 计分板预设头像 3
├── profile_avatar.jpg         # 本地个人资料头像
├── menu_font.ttf              # 菜单中文字体
└── ProggyTiny.ttf             # 像素等宽英文/数字 HUD 字体
```

---

## 资源检索机制

模组在运行时会按以下优先级自动定位素材目录：

1. **游戏目录（首选，打包自动放置）**:
   `<游戏根目录>\SingleSkinMod\CSGO_HUD\`
2. **当前用户桌面目录（开发调试备用）**:
   `C:\Users\<当前用户名>\Desktop\CSGO_HUD\`

* **部署方式**: 运行项目根目录的 `.\deploy.ps1` 脚本，会自动将本目录全量同步至游戏目录的 `SingleSkinMod\CSGO_HUD\` 中，无需手动拷贝到桌面。
* **容错降级**: 若素材缺失，HUD 逻辑会自动平滑降级为 Unity 内置矢量/纯色绘制，绝不报空指针或崩溃。

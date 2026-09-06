using System;
using System.Collections.Generic;
using BepInEx;
using UnityEngine;
using SingleSkinMod.Common;
using SingleSkinMod.Config;
using SingleSkinMod.Guard;
using SingleSkinMod.Hud;
using SingleSkinMod.Native;
using SingleSkinMod.Network;
using SingleSkinMod.Skin;

namespace SingleSkinMod
{
    [BepInPlugin("com.ssjj.singleskinmod", "SSJJ Single Skin Mod", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ModConfig ConfigInstance { get; private set; }
        public static Plugin Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            ModLog.Info(">>> 生死狙击 单美化模组 (SingleSkinMod v1.0.0) 正在初始化 <<<");

            // 1. First block CheatGuard
            CheatGuardBypass.Initialize();

            // 2. Load Config
            ConfigInstance = ModConfig.Load();

            // 3. Initialize Native Bridge (DXGI Present + ImGui)
            if (NativeBridge.LoadAndInitialize())
            {
                ModLog.Info("C++ Native 硬件加速 ImGui 菜单已启动 (按 Home / F12 呼出)");
            }
            else
            {
                ModLog.Warn("C++ Native 菜单未能成功启动，将降级为 TCP + 纯内存模式");
            }

            // 4. Create Persistent Helper GameObject
            GameObject helper = new GameObject("SingleSkinMod_Helper");
            helper.AddComponent<SingleSkinModRuntime>();
            helper.AddComponent<SingleSkinMod.Entity.PlayerUpdate>();
            helper.AddComponent<CsgoHud>();
            // TcpBridgeServer is disabled by default to eliminate TCP listener footprint (avoiding CheckCurrentProcessTcpListeners)
            // helper.AddComponent<TcpBridgeServer>();
            helper.AddComponent<Visual.MapColorController>();
            helper.AddComponent<Visual.SnowEffect>();
            helper.AddComponent<Visual.WorldSnowController>();
            helper.AddComponent<Visual.LoadingScreenController>();
            DontDestroyOnLoad(helper);

            ModLog.Info(">>> 单美化模组核心服务已全部挂载就绪！<<<");
        }
    }

    public class SingleSkinModRuntime : MonoBehaviour
    {
        private bool _catalogsFullyLoaded = false;
        private float _nextCatalogScan = 0f;
        private float _nextPlayerScan = 0f;
        private int _catalogScanCount = 0;

        private void Start()
        {
            PublishConfigToNative();
        }

        private void Update()
        {
            // 1. Poll incoming commands from C++ DXGI ImGui menu
            NativeBridge.PollCommands();

            // F3: 快捷键切换第三人称视角
            if (Input.GetKeyDown(KeyCode.F3))
            {
                var cfg = Plugin.ConfigInstance;
                if (cfg != null)
                {
                    cfg.ThirdPersonEnabled = !cfg.ThirdPersonEnabled;
                    cfg.Save();
                    ModLog.Info("第三人称视角切换为: " + (cfg.ThirdPersonEnabled ? "开启" : "关闭"));
                }
            }

            // 2. Scan in-game catalogs: publish fallbacks early, full dynamic reload once game tables load
            if (!_catalogsFullyLoaded && Time.unscaledTime >= _nextCatalogScan)
            {
                _nextCatalogScan = Time.unscaledTime + 2.0f;
                EnsureCatalogs();
            }

            // 3. Refresh player entity & auto-apply skin if configured
            if (Time.unscaledTime >= _nextPlayerScan)
            {
                _nextPlayerScan = Time.unscaledTime + 0.3f;
                CheckPlayerState();
            }
        }

        private void EnsureCatalogs()
        {
            try
            {
                bool tablesLoaded = ChineseNameResolver.AreGameTablesLoaded();

                var weapons = CatalogScanner.Scan(
                    "Assets.Sources.Constant.Weapon.FreeWeaponConstant",
                    "Assets.Sources.Constant.Weapon.WeaponConstant"
                );
                var characters = CatalogScanner.Scan("share.constant.PlayerCareerConstant");
                var accessories = CatalogScanner.Scan("share.constant.BackAccessoryConstant");
                if (accessories.Count == 0) accessories.AddRange(new[] { "jetpack", "wing_zhandouopen", "chibang1open" });

                if (weapons.Count > 0 || characters.Count > 0)
                {
                    NativeBridge.ResetCatalog(0);
                    foreach (var w in weapons) NativeBridge.AddCatalogItem(0, ChineseNameResolver.ResolveWeaponName(w));

                    NativeBridge.ResetCatalog(1);
                    foreach (var c in characters) NativeBridge.AddCatalogItem(1, ChineseNameResolver.ResolveCareerName(c));

                    NativeBridge.ResetCatalog(2);
                    foreach (var a in accessories) NativeBridge.AddCatalogItem(2, ChineseNameResolver.ResolveAccessoryName(a));

                    PublishConfigToNative(); // 立即让 Native 选中状态与中文名称完全同步

                    _catalogScanCount++;

                    if (tablesLoaded)
                    {
                        _catalogsFullyLoaded = true;
                        ModLog.Info($"游戏配置表已完全载入，资产全量中文反射解析完毕！武器={weapons.Count}, 角色={characters.Count}, 挂件={accessories.Count}");
                    }
                    else
                    {
                        ModLog.Info($"游戏配置表尚未载入 (尝试 {_catalogScanCount})，已推送预置中文字典，持续等待配置表加载...");
                        if (_catalogScanCount >= 10)
                        {
                            _catalogsFullyLoaded = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Warn("EnsureCatalogs 异常: " + ex.Message);
            }
        }

        private void CheckPlayerState()
        {
            var cfg = Plugin.ConfigInstance;
            if (cfg == null) return;

            if (ModelReplacer.TryGetLocalPlayer(out object entity, out object basicInfo, out object info))
            {
                if (!string.IsNullOrEmpty(cfg.Weapon) || !string.IsNullOrEmpty(cfg.Character) || !string.IsNullOrEmpty(cfg.Accessory) ||
                    Mathf.Abs(cfg.Scale - 1f) > 0.01f || Mathf.Abs(cfg.HeadScale - 1f) > 0.01f || cfg.Team > 0)
                {
                    ModelReplacer.ApplyAll(cfg, quiet: true);
                }
            }
        }

        private void PublishConfigToNative()
        {
            var cfg = Plugin.ConfigInstance;
            if (cfg == null) return;

            string weaponDisplay = string.IsNullOrEmpty(cfg.Weapon) ? "" : ChineseNameResolver.ResolveWeaponName(cfg.Weapon);
            string careerDisplay = string.IsNullOrEmpty(cfg.Character) ? "" : ChineseNameResolver.ResolveCareerName(cfg.Character);
            string accDisplay = string.IsNullOrEmpty(cfg.Accessory) ? "" : ChineseNameResolver.ResolveAccessoryName(cfg.Accessory);

            NativeBridge.SetSelection(0, weaponDisplay);
            NativeBridge.SetSelection(1, careerDisplay);
            NativeBridge.SetSelection(2, accDisplay);
            NativeBridge.SetNumeric(0, cfg.Scale);
            NativeBridge.SetNumeric(1, cfg.HeadScale);
            NativeBridge.SetNumeric(2, cfg.Team);
            NativeBridge.SetNumeric(3, cfg.Alpha);
            NativeBridge.SetNumeric(4, cfg.SelfAlpha);
            NativeBridge.SetCsgoHudState(cfg.CsgoHudEnabled, cfg.CsgoHudSound, cfg.CsgoHudVolume, cfg.CsgoHudKillCard, cfg.CsgoHudSuppress);

            // Sync 雨爱 Skybox and Snow settings
            Visual.MapColorController.Enabled = cfg.SkyboxEnabled;
            Visual.MapColorController.CurrentR = cfg.SkyboxR;
            Visual.MapColorController.CurrentG = cfg.SkyboxG;
            Visual.MapColorController.CurrentB = cfg.SkyboxB;
            Visual.MapColorController.CurrentIntensity = cfg.SkyboxIntensity;
            Visual.MapColorController.CurrentLerp = cfg.SkyboxLerp;
            Visual.MapColorController.CurrentLerp2 = cfg.SkyboxLerp2;

            Visual.SnowEffect.Enabled = cfg.SnowEnabled;
            Visual.SnowEffect.SnowCount = cfg.SnowCount;
            Visual.SnowEffect.SnowSpeed = cfg.SnowSpeed;
            Visual.SnowEffect.SnowSize = cfg.SnowSize;

            NativeBridge.SetSkyboxState(
                cfg.SkyboxEnabled, cfg.SkyboxR, cfg.SkyboxG, cfg.SkyboxB,
                cfg.SkyboxIntensity, cfg.SkyboxLerp, cfg.SkyboxLerp2,
                cfg.SnowEnabled ? cfg.SnowCount : 0,
                cfg.SnowSpeed, cfg.SnowSize, cfg.FilterPreset
            );

            NativeBridge.SetThirdPersonState(cfg.ThirdPersonEnabled, cfg.ThirdPersonDistance, cfg.ThirdPersonFov);
            NativeBridge.SetWorldSnowState(cfg.WorldSnowEnabled, cfg.WorldSnowDensity, cfg.WorldSnowSpeed, cfg.WorldSnowSize);
        }
    }
}

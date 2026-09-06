using System;
using System.IO;
using UnityEngine;
using SingleSkinMod.Common;

namespace SingleSkinMod.Config
{
    [Serializable]
    public class ModConfig
    {
        public string Weapon = "";
        public string Character = "";
        public string Accessory = "";
        public float Scale = 1.0f;
        public float HeadScale = 1.0f;
        public int Team = 0;
        public int Alpha = 100;
        public int SelfAlpha = 100;

        // CSGO HUD
        public bool CsgoHudEnabled = true;
        public bool CsgoHudSound = true;
        public float CsgoHudVolume = 0.8f;
        public bool CsgoHudKillCard = true;
        public bool CsgoHudSuppress = false;

        // Visual Environment
        public bool VisualEnabled = false;
        public float VisualExposure = 0.0f;
        public float VisualBloom = 0.0f;
        public float VisualFog = 0.0f;
        public float VisualVignette = 0.0f;

        // 雨爱 Skybox & Snow Filter
        public bool SkyboxEnabled = false;
        public float SkyboxR = 0.8f;
        public float SkyboxG = 0.4f;
        public float SkyboxB = 1.0f;
        public float SkyboxIntensity = 1.0f;
        public float SkyboxLerp = 1.0f;
        public float SkyboxLerp2 = 0.5f;
        public int FilterPreset = 0;
        public bool SnowEnabled = false;
        public int SnowCount = 80;
        public float SnowSpeed = 60.0f;
        public float SnowSize = 4.0f;

        // 第三人称视角 (TPS)
        public bool ThirdPersonEnabled = false;
        public float ThirdPersonDistance = 220f;
        public float ThirdPersonFov = 90f;

        // 3D 世界相机降雪
        public bool WorldSnowEnabled = false;
        public float WorldSnowDensity = 200f;
        public float WorldSnowSpeed = 3.5f;
        public float WorldSnowSize = 0.12f;

        // 自定义加载界面背景 (留空默认使用 SingleSkinMod/background.jpg 或 SingleSkinMod/CustomLoading/background.jpg)
        public bool CustomLoadingEnabled = true;
        public string CustomLoadingImagePath = "";
        public int CustomLoadingFitMode = 0; // 0 = 居中等比裁切满屏(Cover零黑边超清), 1 = 强制四角拉伸铺满(Stretch零黑边)

        private static string GetConfigPath()
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SingleSkinMod");
            if (!Directory.Exists(root))
            {
                try { Directory.CreateDirectory(root); } catch { }
            }
            return Path.Combine(root, "config.json");
        }

        public static ModConfig Load()
        {
            string path = GetConfigPath();
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var cfg = JsonUtility.FromJson<ModConfig>(json);
                    if (cfg != null) return cfg;
                }
            }
            catch (Exception ex)
            {
                ModLog.Warn("读取 config.json 异常: " + ex.Message);
            }
            var def = new ModConfig();
            def.Save();
            return def;
        }

        public void Save()
        {
            string path = GetConfigPath();
            try
            {
                string json = JsonUtility.ToJson(this, true);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                ModLog.Error("保存 config.json 失败: " + ex.Message);
            }
        }
    }
}

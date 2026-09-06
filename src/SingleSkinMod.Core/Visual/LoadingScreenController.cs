using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using SingleSkinMod.Common;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SingleSkinMod.Visual
{
    public class LoadingScreenController : MonoBehaviour
    {
        private static Sprite _cachedSprite;
        private static Texture2D _cachedTexture;
        private static string _cachedPath;
        private static DateTime _lastLoadedTime;
        private static readonly object _lock = new object();

        private float _nextScanTime = 0f;

        public static Sprite GetCustomSprite()
        {
            var cfg = Plugin.ConfigInstance;
            if (cfg != null && !cfg.CustomLoadingEnabled)
            {
                return null;
            }

            lock (_lock)
            {
                string targetPath = cfg?.CustomLoadingImagePath;
                if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
                {
                    // Fallback 1: 游戏主目录 SingleSkinMod/CustomLoading/background.jpg
                    string gameSubDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SingleSkinMod", "CustomLoading", "background.jpg");
                    if (File.Exists(gameSubDir))
                    {
                        targetPath = gameSubDir;
                    }
                    else
                    {
                        // Fallback 2: 游戏主目录 SingleSkinMod/background.jpg
                        string gameRootImg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SingleSkinMod", "background.jpg");
                        if (File.Exists(gameRootImg))
                        {
                            targetPath = gameRootImg;
                        }
                    }
                }

                if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
                {
                    return null;
                }

                try
                {
                    DateTime curTime = File.GetLastWriteTimeUtc(targetPath);
                    if (_cachedSprite != null && _cachedPath == targetPath && curTime == _lastLoadedTime)
                    {
                        return _cachedSprite;
                    }

                    byte[] bytes = File.ReadAllBytes(targetPath);
                    if (bytes == null || bytes.Length == 0) return _cachedSprite;

                    if (_cachedTexture == null)
                    {
                        _cachedTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        _cachedTexture.wrapMode = TextureWrapMode.Clamp;
                        _cachedTexture.filterMode = FilterMode.Bilinear;
                        _cachedTexture.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    }

                    if (_cachedTexture.LoadImage(bytes))
                    {
                        _cachedSprite = Sprite.Create(
                            _cachedTexture,
                            new Rect(0f, 0f, _cachedTexture.width, _cachedTexture.height),
                            new Vector2(0.5f, 0.5f),
                            100f
                        );
                        _cachedSprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
                        _cachedPath = targetPath;
                        _lastLoadedTime = curTime;

                        ModLog.Info($"[LoadingScreen] 成功载入自定义加载背景: {targetPath} ({_cachedTexture.width}x{_cachedTexture.height})");

                        // 备用备份至游戏目录
                        EnsureBackup(targetPath, bytes);
                    }
                }
                catch (Exception ex)
                {
                    ModLog.Warn("[LoadingScreen] 载入自定义图片异常: " + ex.Message);
                }

                return _cachedSprite;
            }
        }

        private static void EnsureBackup(string srcPath, byte[] bytes)
        {
            try
            {
                string targetDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SingleSkinMod", "CustomLoading");
                string targetFile = Path.Combine(targetDir, "background.jpg");
                if (string.Equals(srcPath, targetFile, StringComparison.OrdinalIgnoreCase)) return;

                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
                if (!File.Exists(targetFile) || new FileInfo(targetFile).Length != bytes.Length)
                {
                    File.WriteAllBytes(targetFile, bytes);
                    ModLog.Info($"[LoadingScreen] 已将背景图备份至: {targetFile}");
                }
            }
            catch { }
        }

        public static void ApplyToViewModel(object vmObj)
        {
            if (vmObj == null) return;
            try
            {
                Sprite custom = GetCustomSprite();
                if (custom == null) return;

                Type vmType = vmObj.GetType();
                FieldInfo backNormalField = AccessTools.Field(vmType, "BackNormal");
                FieldInfo backRpgField = AccessTools.Field(vmType, "BackRpg");
                FieldInfo backField = AccessTools.Field(vmType, "Back");
                FieldInfo rpgBackField = AccessTools.Field(vmType, "RpgBack");

                GameObject backNormal = backNormalField?.GetValue(vmObj) as GameObject;
                GameObject backRpg = backRpgField?.GetValue(vmObj) as GameObject;
                Image backImg = backField?.GetValue(vmObj) as Image;
                Image rpgBackImg = rpgBackField?.GetValue(vmObj) as Image;

                CleanAndStretchContainer(backNormal);
                CleanAndStretchContainer(backRpg);

                if (backImg != null) ApplyToImage(backImg, custom);
                if (rpgBackImg != null) ApplyToImage(rpgBackImg, custom);

                if (backNormal != null)
                {
                    Image bnImg = backNormal.GetComponent<Image>();
                    if (bnImg != null) ApplyToImage(bnImg, custom);
                }
                if (backRpg != null)
                {
                    Image brImg = backRpg.GetComponent<Image>();
                    if (brImg != null) ApplyToImage(brImg, custom);
                }
            }
            catch (Exception ex)
            {
                ModLog.Warn("[LoadingScreen] ApplyToViewModel 异常: " + ex.Message);
            }
        }

        private static void CleanAndStretchContainer(GameObject go)
        {
            if (go == null) return;
            try
            {
                // 彻底销毁任何可能限宽限高或限制比例的布局与遮罩组件
                var fitters = go.GetComponents<AspectRatioFitter>();
                foreach (var f in fitters) { if (f != null) { f.enabled = false; UnityEngine.Object.Destroy(f); } }

                var csfs = go.GetComponents<ContentSizeFitter>();
                foreach (var c in csfs) { if (c != null) { c.enabled = false; UnityEngine.Object.Destroy(c); } }

                var masks = go.GetComponents<Mask>();
                foreach (var m in masks) { if (m != null) { m.enabled = false; UnityEngine.Object.Destroy(m); } }

                var rectMasks = go.GetComponents<RectMask2D>();
                foreach (var rm in rectMasks) { if (rm != null) { rm.enabled = false; UnityEngine.Object.Destroy(rm); } }

                RectTransform rt = go.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                    rt.sizeDelta = Vector2.zero;
                    rt.anchoredPosition = Vector2.zero;
                    rt.localScale = Vector3.one;
                }
            }
            catch { }
        }

        public static void ApplyToImage(Image img, Sprite custom)
        {
            if (img == null || custom == null) return;
            try
            {
                // 移除 Image 自身上的 AspectRatioFitter / ContentSizeFitter
                var fitters = img.GetComponents<AspectRatioFitter>();
                foreach (var f in fitters) { if (f != null) { f.enabled = false; UnityEngine.Object.Destroy(f); } }

                var csfs = img.GetComponents<ContentSizeFitter>();
                foreach (var c in csfs) { if (c != null) { c.enabled = false; UnityEngine.Object.Destroy(c); } }

                img.sprite = custom;
                img.overrideSprite = custom;
                img.color = Color.white;
                img.type = Image.Type.Simple;
                img.preserveAspect = false;

                RectTransform rt = img.rectTransform;
                if (rt == null) return;

                // 获取真实 Canvas 与屏幕像素尺寸
                Canvas canvas = img.canvas ?? img.GetComponentInParent<Canvas>();
                float canvasW = 1920f;
                float canvasH = 1080f;
                if (canvas != null)
                {
                    RectTransform crt = canvas.GetComponent<RectTransform>();
                    if (crt != null && crt.rect.width > 50f && crt.rect.height > 50f)
                    {
                        canvasW = crt.rect.width;
                        canvasH = crt.rect.height;
                    }
                }
                float screenW = Mathf.Max((float)Screen.width, 1920f);
                float screenH = Mathf.Max((float)Screen.height, 1080f);

                float targetW = Mathf.Max(canvasW, screenW);
                float targetH = Mathf.Max(canvasH, screenH);

                var cfg = Plugin.ConfigInstance;
                float imgAspect = (custom.texture != null && custom.texture.height > 0)
                    ? ((float)custom.texture.width / (float)custom.texture.height)
                    : (1200f / 655f);

                float screenAspect = targetW / targetH;
                float finalW, finalH;

                if (cfg != null && cfg.CustomLoadingFitMode == 1)
                {
                    // 模式 1: 强制四角满屏拉伸
                    finalW = targetW;
                    finalH = targetH;
                }
                else
                {
                    // 模式 0: 等比居中裁切充满全屏 (Cover) - 零黑边、超清、人物不被压扁变形
                    if (screenAspect > imgAspect)
                    {
                        finalW = targetW;
                        finalH = targetW / imgAspect;
                    }
                    else
                    {
                        finalH = targetH;
                        finalW = targetH * imgAspect;
                    }
                }

                // 附加 10% 冗余边距，彻底消灭一切亚像素缝隙与边缘黑线
                finalW = Mathf.Ceil(finalW * 1.1f);
                finalH = Mathf.Ceil(finalH * 1.1f);

                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.localPosition = Vector3.zero;
                rt.localScale = Vector3.one;
                rt.sizeDelta = new Vector2(finalW, finalH);
            }
            catch { }
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            try
            {
                if (scene.name == "BattleLoading" || scene.name.IndexOf("load", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ScanAndApply();
                }
            }
            catch { }
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextScanTime) return;
            _nextScanTime = Time.unscaledTime + 0.1f;

            try
            {
                Scene battleLoadingScene = SceneManager.GetSceneByName("BattleLoading");
                if (battleLoadingScene.isLoaded)
                {
                    ScanAndApply();
                }
            }
            catch { }
        }

        private static void ScanAndApply()
        {
            Sprite custom = GetCustomSprite();
            if (custom == null) return;

            // 1. 优先扫描 BattleLoadingViewModel
            Type vmType = AccessTools.TypeByName("SSJJ.BattleLoading.BattleLoadingViewModel");
            if (vmType != null)
            {
                UnityEngine.Object[] vms = FindObjectsOfType(vmType);
                if (vms != null && vms.Length > 0)
                {
                    foreach (var vm in vms)
                    {
                        ApplyToViewModel(vm);
                    }
                    return;
                }
            }

            // 2. 备选：查找名为 Back / RpgBack 的全屏 Image
            Image[] allImages = FindObjectsOfType<Image>();
            if (allImages != null)
            {
                foreach (var img in allImages)
                {
                    if (img != null && (img.name == "Back" || img.name == "RpgBack" || img.name == "BackNormal"))
                    {
                        ApplyToImage(img, custom);
                    }
                }
            }
        }
    }
}

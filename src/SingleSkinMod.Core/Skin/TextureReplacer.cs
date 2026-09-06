using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using SingleSkinMod.Common;

namespace SingleSkinMod.Skin
{
    public static class TextureReplacer
    {
        private static string GetSkinRoot()
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SingleSkinMod", "skin");
            if (!Directory.Exists(root))
            {
                try { Directory.CreateDirectory(root); } catch { }
            }
            return root;
        }

        private static string GetMapsRoot()
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SingleSkinMod", "maps");
            if (!Directory.Exists(root))
            {
                try { Directory.CreateDirectory(root); } catch { }
            }
            return root;
        }

        public static int DoSwap(string keyword, string fileName)
        {
            string fullPath = Path.Combine(GetSkinRoot(), fileName);
            if (!File.Exists(fullPath))
            {
                // Also check classic skin/ folder for backward compatibility
                string fallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "skin", fileName);
                if (File.Exists(fallback)) fullPath = fallback;
                else
                {
                    ModLog.Error($"[TextureReplacer] 材质替换失败：未找到图片文件: {fullPath}");
                    return 0;
                }
            }

            byte[] bytes = File.ReadAllBytes(fullPath);
            Texture2D newTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            newTex.LoadImage(bytes);
            newTex.Apply();

            int affectedCount = 0;
            Material[] allMaterials = Resources.FindObjectsOfTypeAll<Material>();

            foreach (Material m in allMaterials)
            {
                if (m != null && !string.IsNullOrEmpty(m.name) && m.name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    m.mainTexture = newTex;
                    if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", newTex);
                    if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", newTex);
                    affectedCount++;
                }
            }

            if (affectedCount == 0)
            {
                ModLog.Warn($"[TextureReplacer] 未匹配到包含关键字 [{keyword}] 的材质。");
            }
            else
            {
                ModLog.Info($"[TextureReplacer] 成功为关键字 [{keyword}] 替换了 {affectedCount} 个材质的贴图！");
            }

            return affectedCount;
        }

        public static int DoMapInject(string folderName)
        {
            string mapTargetDir = Path.Combine(GetMapsRoot(), folderName);
            if (!Directory.Exists(mapTargetDir))
            {
                string fallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "maps", folderName);
                if (Directory.Exists(fallback)) mapTargetDir = fallback;
                else
                {
                    ModLog.Error($"[TextureReplacer] 地图贴图注入失败：未找到目标文件夹: {mapTargetDir}");
                    return 0;
                }
            }

            string[] files = Directory.GetFiles(mapTargetDir, "*.png");
            if (files.Length == 0)
            {
                ModLog.Error($"[TextureReplacer] 目标文件夹内没有任何 .png 贴图: {mapTargetDir}");
                return 0;
            }

            var localFileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string filePath in files)
            {
                string nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
                localFileMap[nameWithoutExt] = filePath;
            }

            int successCount = 0;
            Texture2D[] allTextures = Resources.FindObjectsOfTypeAll<Texture2D>();

            foreach (Texture2D tex in allTextures)
            {
                if (tex == null || string.IsNullOrEmpty(tex.name)) continue;

                if (localFileMap.TryGetValue(tex.name, out string matchedPngPath))
                {
                    byte[] fileBytes = File.ReadAllBytes(matchedPngPath);
                    try
                    {
                        int width = tex.width;
                        int height = tex.height;
                        bool hasMipmaps = (tex.mipmapCount > 1);

                        Texture2D tempTex = new Texture2D(width, height, TextureFormat.RGBA32, hasMipmaps);
                        tempTex.LoadImage(fileBytes);
                        tempTex.Apply();

                        Graphics.CopyTexture(tempTex, tex);
                        UnityEngine.Object.Destroy(tempTex);

                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        ModLog.Warn($"[TextureReplacer] 覆盖纹理 {tex.name} 异常: {ex.Message}");
                    }

                    // Fallback for HighlightMap materials
                    try
                    {
                        Texture2D backupTex = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, tex.mipmapCount > 1);
                        backupTex.name = tex.name + "_mod_backup";
                        backupTex.LoadImage(fileBytes);
                        backupTex.Apply();

                        Material[] allMaterials = Resources.FindObjectsOfTypeAll<Material>();
                        foreach (Material mat in allMaterials)
                        {
                            if (mat != null && mat.name.IndexOf("HighlightMap", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                string[] props = mat.GetTexturePropertyNames();
                                foreach (string prop in props)
                                {
                                    Texture currentTex = mat.GetTexture(prop);
                                    if (currentTex != null && currentTex.name.Equals(tex.name, StringComparison.OrdinalIgnoreCase))
                                    {
                                        mat.SetTexture(prop, backupTex);
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            ModLog.Info($"[TextureReplacer] 地图贴图包 [{folderName}] 注入完成，成功覆盖 {successCount} 处纹理。");
            return successCount;
        }

        public static List<string> ScanWeapons()
        {
            var results = new List<string>();
            Material[] mats = Resources.FindObjectsOfTypeAll<Material>();
            foreach (Material m in mats)
            {
                if (m == null || string.IsNullOrEmpty(m.name)) continue;
                string n = m.name.ToLower();
                if (n.Contains("weapon") || n.Contains("gun") || n.Contains("knife") || n.Contains("arm"))
                {
                    results.Add(m.name);
                }
            }
            return results;
        }
    }
}

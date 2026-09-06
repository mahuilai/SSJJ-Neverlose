using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using SingleSkinMod.Common;
using SingleSkinMod.Config;
using SingleSkinMod.Skin;

namespace SingleSkinMod.Native
{
    internal static class NativeBridge
    {
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string path);

        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitializeFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IsHookReadyFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetMenuVisibleFn(int visible);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IsMenuVisibleFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetManagedTickFn(IntPtr fn);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ShutdownFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ResetCatalogFn(int category);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)] private delegate void AddCatalogFn(int category, [MarshalAs(UnmanagedType.LPWStr)] string item);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)] private delegate void SetSelectionFn(int category, [MarshalAs(UnmanagedType.LPWStr)] string item);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetNumericFn(int field, float value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)] private delegate void SetStatusFn([MarshalAs(UnmanagedType.LPWStr)] string value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetVisualStateFn(int enabled, float exposure, float bloom, float fog, float shafts, float vignette);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetCsgoHudStateFn(int enabled, int sound, float vol, int card, int suppress);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetSkyboxStateFn(int enabled, float r, float g, float b, float intensity, float lerp, float lerp2, int snowCount, float snowSpeed, float snowSize, int preset);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetThirdPersonStateFn(int enabled, float distance, float fov);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetWorldSnowStateFn(int enabled, float density, float speed, float size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)] private delegate int PollFn(out int type, out float value, [Out] StringBuilder text, int capacity);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void ManagedTickCallback();

        private static InitializeFn _init;
        private static IsHookReadyFn _isHookReady;
        private static SetMenuVisibleFn _setMenuVisible;
        private static IsMenuVisibleFn _isMenuVisible;
        private static SetManagedTickFn _setManagedTick;
        private static ShutdownFn _shutdown;
        private static ResetCatalogFn _resetCatalog;
        private static AddCatalogFn _addCatalog;
        private static SetSelectionFn _setSelection;
        private static SetNumericFn _setNumeric;
        private static SetStatusFn _setStatus;
        private static SetVisualStateFn _setVisualState;
        private static SetCsgoHudStateFn _setCsgoHudState;
        private static SetSkyboxStateFn _setSkyboxState;
        private static SetThirdPersonStateFn _setThirdPersonState;
        private static SetWorldSnowStateFn _setWorldSnowState;
        private static PollFn _poll;

        private static IntPtr _module;
        private static bool _initialized;

        internal static bool LoadAndInitialize()
        {
            if (_initialized) return true;

            try
            {
                string dllPath = ExtractNativeDll();
                if (!File.Exists(dllPath))
                {
                    ModLog.Error("Native DLL 提取失败: " + dllPath);
                    return false;
                }

                _module = LoadLibraryW(dllPath);
                if (_module == IntPtr.Zero)
                {
                    int err = Marshal.GetLastWin32Error();
                    ModLog.Error($"LoadLibraryW(SingleSkinMod.Native.dll) 失败: 错误码={err}");
                    return false;
                }

                _init = Bind<InitializeFn>("SSJJUI_Initialize");
                _isHookReady = Bind<IsHookReadyFn>("SSJJUI_IsHookReady");
                _setMenuVisible = Bind<SetMenuVisibleFn>("SSJJUI_SetMenuVisible");
                _isMenuVisible = Bind<IsMenuVisibleFn>("SSJJUI_IsMenuVisible");
                _setManagedTick = Bind<SetManagedTickFn>("SSJJUI_SetManagedTick");
                _shutdown = Bind<ShutdownFn>("SSJJUI_Shutdown");
                _resetCatalog = Bind<ResetCatalogFn>("SSJJUI_ResetCatalog");
                _addCatalog = Bind<AddCatalogFn>("SSJJUI_AddCatalogItem");
                _setSelection = Bind<SetSelectionFn>("SSJJUI_SetSelection");
                _setNumeric = Bind<SetNumericFn>("SSJJUI_SetNumericState");
                _setStatus = Bind<SetStatusFn>("SSJJUI_SetStatus");
                _setVisualState = Bind<SetVisualStateFn>("SSJJUI_SetVisualState");
                _setCsgoHudState = Bind<SetCsgoHudStateFn>("SSJJUI_SetCsgoHudState");
                _setSkyboxState = Bind<SetSkyboxStateFn>("SSJJUI_SetSkyboxState");
                _setThirdPersonState = Bind<SetThirdPersonStateFn>("SSJJUI_SetThirdPersonState");
                _setWorldSnowState = Bind<SetWorldSnowStateFn>("SSJJUI_SetWorldSnowState");
                _poll = Bind<PollFn>("SSJJUI_PollUiCommand");

                if (_init == null || _init() == 0)
                {
                    ModLog.Error("SSJJUI_Initialize() 返回失败");
                    return false;
                }

                _initialized = true;
                ModLog.Info("SingleSkinMod.Native (DXGI Present + ImGui) 桥接绑定成功！");
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error("NativeBridge 初始化异常: " + ex.Message);
                return false;
            }
        }

        private static T Bind<T>(string name) where T : class
        {
            IntPtr proc = GetProcAddress(_module, name);
            if (proc == IntPtr.Zero)
            {
                ModLog.Warn("未找到导出函数: " + name);
                return null;
            }
            return Marshal.GetDelegateForFunctionPointer(proc, typeof(T)) as T;
        }

        private static string ExtractNativeDll()
        {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string baseDir = Path.Combine(localApp, "SingleSkinMod", "native");

            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream("SingleSkinMod.Native.dll"))
            {
                if (s == null)
                {
                    // Fallback to searching nearby directory
                    string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SingleSkinMod.Native.dll");
                    if (File.Exists(local)) return local;
                    return "";
                }

                byte[] bytes = new byte[s.Length];
                s.Read(bytes, 0, bytes.Length);

                string hash;
                using (SHA256 sha = SHA256.Create())
                {
                    hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").Substring(0, 12);
                }

                string targetDir = Path.Combine(baseDir, hash);
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                string targetPath = Path.Combine(targetDir, "SingleSkinMod.Native.dll");
                if (!File.Exists(targetPath))
                {
                    File.WriteAllBytes(targetPath, bytes);
                }
                return targetPath;
            }
        }

        private static void OnNativeTick()
        {
            // Pumped from Present hook
            try
            {
                PollCommands();
            }
            catch { }
        }

        internal static void PollCommands()
        {
            if (_poll == null) return;

            StringBuilder sb = new StringBuilder(512);
            for (int i = 0; i < 64 && _poll(out int type, out float val, sb, 512) != 0; i++)
            {
                HandleCommand(type, val, sb.ToString());
            }
        }

        private static void HandleCommand(int type, float val, string text)
        {
            var config = SingleSkinMod.Plugin.ConfigInstance;
            if (config == null) return;

            switch (type)
            {
                case 1: // CmdWeapon
                    config.Weapon = Skin.ChineseNameResolver.ExtractRawId(text);
                    SetSelection(0, text);
                    config.Save();
                    ModelReplacer.ApplyAll(config);
                    break;
                case 2: // CmdCharacter
                    config.Character = Skin.ChineseNameResolver.ExtractRawId(text);
                    SetSelection(1, text);
                    config.Save();
                    ModelReplacer.ApplyAll(config);
                    break;
                case 3: // CmdAccessory
                    config.Accessory = Skin.ChineseNameResolver.ExtractRawId(text);
                    SetSelection(2, text);
                    config.Save();
                    ModelReplacer.ApplyAll(config);
                    break;
                case 4: // CmdScale
                    config.Scale = val;
                    SetNumeric(0, val);
                    config.Save();
                    ModelReplacer.ApplyAll(config, quiet: true);
                    break;
                case 5: // CmdHead
                    config.HeadScale = val;
                    SetNumeric(1, val);
                    config.Save();
                    ModelReplacer.ApplyAll(config, quiet: true);
                    break;
                case 6: // CmdTeam
                    config.Team = (int)val;
                    SetNumeric(2, val);
                    config.Save();
                    ModelReplacer.ApplyAll(config, quiet: true);
                    break;
                case 7: // CmdAlpha
                    config.Alpha = (int)val;
                    SetNumeric(3, val);
                    config.Save();
                    ModelReplacer.ApplyAll(config, quiet: true);
                    break;
                case 8: // CmdSelfAlpha
                    config.SelfAlpha = (int)val;
                    SetNumeric(4, val);
                    config.Save();
                    ModelReplacer.ApplyAll(config, quiet: true);
                    break;
                case 9: // CmdApplyAll
                    ModelReplacer.ApplyAll(config);
                    config.Save();
                    SetStatus("已执行一键应用");
                    break;
                case 10: // CmdRestore
                    ModelReplacer.RestoreOriginal();
                    SetStatus("已还原初始状态");
                    break;
                case 30: // CmdSwapPng: "keyword|filename.png"
                    string[] p = text.Split('|');
                    if (p.Length >= 2)
                    {
                        int c = TextureReplacer.DoSwap(p[0].Trim(), p[1].Trim());
                        SetStatus($"材质替换完成: 影响 {c} 处材质");
                    }
                    break;
                case 31: // CmdMapInject: folderName
                    int mc = TextureReplacer.DoMapInject(text.Trim());
                    SetStatus($"地图贴图注入完成: 覆盖 {mc} 处纹理");
                    break;
                case 32: // CmdCsgoHudToggle
                    config.CsgoHudEnabled = val > 0.5f;
                    config.Save();
                    break;
                case 33: // CmdCsgoHudSound
                    config.CsgoHudSound = val > 0.5f;
                    config.Save();
                    break;
                case 34: // CmdCsgoHudVolume
                    config.CsgoHudVolume = val;
                    config.Save();
                    break;
                case 35: // CmdCsgoHudKillCard
                    config.CsgoHudKillCard = val > 0.5f;
                    config.Save();
                    break;
                case 36: // CmdCsgoHudSuppress
                    config.CsgoHudSuppress = val > 0.5f;
                    config.Save();
                    break;
                case 37: // CmdCsgoHudTestKill
                    Hud.CsgoHud.Instance?.TriggerTestKill();
                    break;
                case 40: // CmdSkyboxToggle
                    config.SkyboxEnabled = val > 0.5f;
                    Visual.MapColorController.Enabled = config.SkyboxEnabled;
                    config.Save();
                    break;
                case 41: // CmdSkyboxR
                    config.SkyboxR = val;
                    Visual.MapColorController.CurrentR = val;
                    config.Save();
                    break;
                case 42: // CmdSkyboxG
                    config.SkyboxG = val;
                    Visual.MapColorController.CurrentG = val;
                    config.Save();
                    break;
                case 43: // CmdSkyboxB
                    config.SkyboxB = val;
                    Visual.MapColorController.CurrentB = val;
                    config.Save();
                    break;
                case 44: // CmdSkyboxIntensity
                    config.SkyboxIntensity = val;
                    Visual.MapColorController.CurrentIntensity = val;
                    config.Save();
                    break;
                case 45: // CmdSkyboxLerp
                    config.SkyboxLerp = val;
                    Visual.MapColorController.CurrentLerp = val;
                    config.Save();
                    break;
                case 46: // CmdSkyboxLerp2
                    config.SkyboxLerp2 = val;
                    Visual.MapColorController.CurrentLerp2 = val;
                    config.Save();
                    break;
                case 47: // CmdSnowCount
                    int sc = (int)val;
                    config.SnowEnabled = sc > 0;
                    if (sc > 0) config.SnowCount = sc;
                    Visual.SnowEffect.Enabled = config.SnowEnabled;
                    Visual.SnowEffect.SnowCount = config.SnowCount;
                    config.Save();
                    break;
                case 48: // CmdSnowSpeed
                    config.SnowSpeed = val;
                    Visual.SnowEffect.SnowSpeed = val;
                    config.Save();
                    break;
                case 49: // CmdSnowSize
                    config.SnowSize = val;
                    Visual.SnowEffect.SnowSize = val;
                    config.Save();
                    break;
                case 50: // CmdFilterPreset
                    int preset = (int)val;
                    config.FilterPreset = preset;
                    Visual.MapColorController.ApplyPreset(preset);
                    if (preset > 0)
                    {
                        config.SkyboxEnabled = true;
                        config.SkyboxR = Visual.MapColorController.CurrentR;
                        config.SkyboxG = Visual.MapColorController.CurrentG;
                        config.SkyboxB = Visual.MapColorController.CurrentB;
                        config.SkyboxIntensity = Visual.MapColorController.CurrentIntensity;
                        config.SkyboxLerp = Visual.MapColorController.CurrentLerp;
                        config.SkyboxLerp2 = Visual.MapColorController.CurrentLerp2;
                    }
                    else
                    {
                        config.SkyboxEnabled = false;
                    }
                    config.Save();
                    break;
                case 51: // CmdThirdPerson
                    config.ThirdPersonEnabled = val > 0.5f;
                    config.Save();
                    break;
                case 52: // CmdThirdPersonDistance
                    config.ThirdPersonDistance = val;
                    config.Save();
                    break;
                case 53: // CmdThirdPersonFov
                    config.ThirdPersonFov = val;
                    config.Save();
                    break;
                case 54: // CmdWorldSnowToggle
                    config.WorldSnowEnabled = val > 0.5f;
                    config.Save();
                    break;
                case 55: // CmdWorldSnowDensity
                    config.WorldSnowDensity = val;
                    config.Save();
                    break;
                case 56: // CmdWorldSnowSpeed
                    config.WorldSnowSpeed = val;
                    config.Save();
                    break;
                case 57: // CmdWorldSnowSize
                    config.WorldSnowSize = val;
                    config.Save();
                    break;
            }
        }

        internal static void ResetCatalog(int cat) => _resetCatalog?.Invoke(cat);
        internal static void AddCatalogItem(int cat, string item) => _addCatalog?.Invoke(cat, item);
        internal static void SetSelection(int cat, string item) => _setSelection?.Invoke(cat, item);
        internal static void SetNumeric(int field, float val) => _setNumeric?.Invoke(field, val);
        internal static void SetStatus(string status) => _setStatus?.Invoke(status);
        internal static void SetCsgoHudState(bool enabled, bool sound, float vol, bool card, bool suppress)
        {
            _setCsgoHudState?.Invoke(enabled ? 1 : 0, sound ? 1 : 0, vol, card ? 1 : 0, suppress ? 1 : 0);
        }
        internal static void SetSkyboxState(bool enabled, float r, float g, float b, float intensity, float lerp, float lerp2, int snowCount, float snowSpeed, float snowSize, int preset)
        {
            _setSkyboxState?.Invoke(enabled ? 1 : 0, r, g, b, intensity, lerp, lerp2, snowCount, snowSpeed, snowSize, preset);
        }
        internal static void SetThirdPersonState(bool enabled, float distance, float fov)
        {
            _setThirdPersonState?.Invoke(enabled ? 1 : 0, distance, fov);
        }
        internal static void SetWorldSnowState(bool enabled, float density, float speed, float size)
        {
            _setWorldSnowState?.Invoke(enabled ? 1 : 0, density, speed, size);
        }
    }
}

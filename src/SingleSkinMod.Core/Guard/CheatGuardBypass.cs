using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SingleSkinMod.Common;

namespace SingleSkinMod.Guard
{
    internal static class CheatGuardBypass
    {
        private static bool _initialized = false;
        private static Harmony _harmony;
        private static readonly HashSet<string> _patchedKeys = new HashSet<string>();

        internal static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                _harmony = new Harmony("com.ssjj.cheatguard.bypass");

                // 每一步独立 try-catch，绝不让一个 patch 的失败影响其他的
                SafeRun("PatchRuntimeCheatGuard", () => PatchRuntimeCheatGuard(_harmony));
                SafeRun("PatchAceClientManager",  () => PatchAceClientManager(_harmony));
                SafeRun("PatchAntiScreenshot",     () => PatchAntiScreenshot(_harmony));
                SafeRun("PatchMasterSwitches",     () => PatchMasterSwitches());
                SafeRun("PatchAnimShields",        () => PatchAnimShields(_harmony));
                SafeRun("PatchGameTerminators",    () => PatchGameTerminators(_harmony));
                SafeRun("PatchTpsCamera",          () => PatchTpsCamera(_harmony));
                SafeRun("PatchModelLoading",        () => PatchModelLoading(_harmony));
                SafeRun("PatchLoadingScreen",       () => PatchLoadingScreen(_harmony));
                SafeRun("StartGameGuardTerminator", () => StartGameGuardTerminator());

                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoaded;
                ModLog.Info("=== CheatGuardBypass 初始化完成 ===");
            }
            catch (Exception ex)
            {
                ModLog.Warn("CheatGuardBypass.Initialize 顶层异常(不影响游戏): " + ex.Message);
            }
        }

        private static void SafeRun(string name, Action action)
        {
            try { action(); }
            catch (Exception ex) { ModLog.Warn($"[SafeRun] {name} 失败(不影响游戏): {ex.GetType().Name}: {ex.Message}"); }
        }

        private static void OnAssemblyLoaded(object sender, AssemblyLoadEventArgs args)
        {
            try
            {
                string name = args.LoadedAssembly?.FullName ?? "";
                if (name.Contains("Assembly-CSharp") || name.Contains("5EPro") || name.Contains("HipsMainpro"))
                {
                    ModLog.Info($"新程序集: {name.Split(',')[0]}, 尝试补 patch");
                    SafeRun("Late-PatchRuntimeCheatGuard", () => PatchRuntimeCheatGuard(_harmony));
                    SafeRun("Late-PatchAceClientManager",  () => PatchAceClientManager(_harmony));
                    SafeRun("Late-PatchMasterSwitches",    () => PatchMasterSwitches());
                    SafeRun("Late-PatchGameTerminators",   () => PatchGameTerminators(_harmony));
                    SafeRun("Late-PatchTpsCamera",         () => PatchTpsCamera(_harmony));
                    SafeRun("Late-PatchModelLoading",      () => PatchModelLoading(_harmony));
                    SafeRun("Late-PatchLoadingScreen",     () => PatchLoadingScreen(_harmony));
                }
            }
            catch { }
        }

        // ─────────────────────────────────────────────
        // 1. RuntimeCheatGuard — 精确方法名，动态查找类
        // ─────────────────────────────────────────────
        private static void PatchRuntimeCheatGuard(Harmony harmony)
        {
            Type guardType = FindGuardType();
            if (guardType == null)
            {
                ModLog.Warn("RuntimeCheatGuard 类未找到");
                return;
            }

            ModLog.Info($"找到 RuntimeCheatGuard: {guardType.FullName}");

            // 1. 反射切断其后台线程和事件
            try
            {
                FieldInfo startedField = AccessTools.Field(guardType, "_started");
                if (startedField != null) startedField.SetValue(null, true);

                FieldInfo threadField = AccessTools.Field(guardType, "_thread");
                if (threadField != null)
                {
                    System.Threading.Thread th = threadField.GetValue(null) as System.Threading.Thread;
                    if (th != null && th.IsAlive)
                    {
                        try { th.Abort(); } catch { }
                    }
                    threadField.SetValue(null, null);
                }

                FieldInfo detectedField = AccessTools.Field(guardType, "Detected");
                if (detectedField != null) detectedField.SetValue(null, null);
                ModLog.Info("RuntimeCheatGuard 后台扫描线程与回调已被彻底斩断！");
            }
            catch { }

            MethodInfo block = AccessTools.Method(typeof(CheatGuardBypass), nameof(BlockPrefix));

            // 2. 精确拦截检测方法
            string[] methods = {
                "Start", "CheckLoop", "CheckGameRootSideLoadedFiles",
                "CheckUnityDataArtifacts", "CheckLoadedModules",
                "CheckLoadedManagedAssemblies", "CheckCurrentProcessTcpListeners",
                "ReportDetected", "Report", "ReportDetectedOnce"
            };

            foreach (string m in methods)
                PatchSafe(harmony, guardType, m, block);
        }

        private static Type FindGuardType()
        {
            // 先试已知名
            Type t = AccessTools.TypeByName("Assets.Sources.Utils.RuntimeCheatGuard")
                  ?? AccessTools.TypeByName("RuntimeCheatGuard");
            if (t != null) return t;

            // 扫描：找含 ReportDetected 或 CheckLoadedModules 的类
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (Type type in asm.GetTypes())
                    {
                        try
                        {
                            var flags = BindingFlags.Public | BindingFlags.NonPublic
                                      | BindingFlags.Instance | BindingFlags.Static;
                            if (type.GetMethod("ReportDetected", flags) != null ||
                                type.GetMethod("CheckLoadedModules", flags) != null)
                            {
                                ModLog.Info($"[扫描] 疑似 Guard: {type.FullName}");
                                return type;
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }
            return null;
        }

        // ─────────────────────────────────────────────
        // 2. AceClientManager — 只 patch 精确的检测/上报方法
        // ─────────────────────────────────────────────
        private static void PatchAceClientManager(Harmony harmony)
        {
            Type aceType = AccessTools.TypeByName("Assets.Libs.ForAce.AceClientManager")
                        ?? FindTypeBySimpleName("AceClientManager");
            if (aceType == null)
            {
                ModLog.Warn("AceClientManager 未找到");
                return;
            }

            ModLog.Info($"找到 AceClientManager: {aceType.FullName}");
            MethodInfo block = AccessTools.Method(typeof(CheatGuardBypass), nameof(BlockPrefix));

            // 精确方法列表（不暴力扫描，避免 crash）
            string[] methods = {
                "AceSwitch", "OnAceSwitch", "HandleAceSwitch", "SetAceSwitch",
                "CheckAceOpen", "StartAce", "Init", "Start", "Update",
                "CheckPlugin", "DoCheckPlugin", "RunPluginCheck",
                "ReportPlugin", "SendReport", "OnReport", "CheckGame",
                "HandlePlugin", "PluginCheck", "ReportCheck"
            };

            foreach (string m in methods)
                PatchSafe(harmony, aceType, m, block);

            ModLog.Info("AceClientManager 关键方法已拦截");
        }

        // ─────────────────────────────────────────────
        // 3. 截图防护链 — 精确方法名
        // ─────────────────────────────────────────────
        private static void PatchAntiScreenshot(Harmony harmony)
        {
            MethodInfo block = AccessTools.Method(typeof(CheatGuardBypass), nameof(BlockPrefix));

            // SpecailDebug.AvoidCapture（字段直接赋值，不 patch）
            try
            {
                Type sd = AccessTools.TypeByName("Assets.Sources.Utils.SpecailDebug");
                if (sd != null)
                {
                    object inst = AccessTools.Property(sd, "Instance")?.GetValue(null, null);
                    if (inst != null)
                    {
                        AccessTools.Field(sd, "AvoidCapture")?.SetValue(inst, true);
                        ModLog.Info("SpecailDebug.AvoidCapture = true");
                    }
                }
            }
            catch { }

            // ScreenShotManager
            Type ssm = AccessTools.TypeByName("Assets.Sources.Utils.ScreenShotManager");
            if (ssm != null)
            {
                foreach (string m in new[] { "CreateScreenThread", "Start", "Update", "Init", "TakeScreen", "SendScreen" })
                    PatchSafe(harmony, ssm, m, block);
            }

            // AbstractCaptureSnapshot
            Type abs = AccessTools.TypeByName("Assets.Sources.Utils.AbstractCaptureSnapshot");
            if (abs != null)
            {
                foreach (string m in new[] { "CheckCanCaptrueSnapshot", "InitScreenThread", "WaitScreen", "ScreenNow", "UpdateScreen" })
                    PatchSafe(harmony, abs, m, block);
            }

            // 三个具体 Snapshot 实现
            foreach (string typeName in new[] {
                "Assets.Sources.Utils.WindowHdcCaptureSnapshot",
                "Assets.Sources.Utils.WindowCaptureSnapshot",
                "Assets.Sources.Utils.ExclusiveCaptureSnapshot" })
            {
                Type t = AccessTools.TypeByName(typeName);
                if (t != null)
                    PatchSafe(harmony, t, "UpdateScreen", block);
            }

            // CheckGamePlusHandler (Packet 272)
            Type cgp = AccessTools.TypeByName("Assets.Sources.Systems.PacketHandle.Handlers.CheckGamePlusHandler");
            if (cgp != null)
            {
                PatchSafe(harmony, cgp, "Handle", block);
                PatchSafe(harmony, cgp, "Execute", block);
                ModLog.Info("CheckGamePlusHandler [Packet 272] 已拦截");
            }

            // 扫描 Handlers 命名空间下含 Check 的类，只 patch Handle/Execute
            PatchCheckHandlersSafe(harmony, block);
        }

        private static void PatchCheckHandlersSafe(Harmony harmony, MethodInfo block)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (Type type in asm.GetTypes())
                    {
                        try
                        {
                            string fn = type.FullName ?? "";
                            if (!fn.Contains("Handlers") || !fn.Contains("Check")) continue;
                            // 只 patch Handle / Execute，不暴力扫
                            PatchSafe(harmony, type, "Handle", block);
                            PatchSafe(harmony, type, "Execute", block);
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        // ─────────────────────────────────────────────
        // 4. 主开关 — 动态扫描混淆类
        // ─────────────────────────────────────────────
        private static void PatchMasterSwitches()
        {
            // 硬编码名（万一还用旧名）
            TrySetBoolFields("dje_zKUQ4SGRT4EMVPD9NDAF299RKMHK69D6S4X_ejd",
                new[] { "dje_zWJYJB5N3_ejd", "dje_zEYLY4ZSTH8DMYB2_ejd" });

            // 扫描混淆类名特征
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (Type type in asm.GetTypes())
                    {
                        try
                        {
                            string n = type.Name ?? "";
                            if (!n.Contains("_ejd") && !n.Contains("dje_z") &&
                                !n.Contains("SGRT4") && !n.Contains("RKMHK")) continue;

                            foreach (FieldInfo fi in type.GetFields(
                                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                            {
                                try
                                {
                                    if (fi.FieldType == typeof(bool))
                                    {
                                        fi.SetValue(null, true);
                                        ModLog.Info($"[主开关] {type.Name}.{fi.Name} = true");
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        private static void TrySetBoolFields(string typeName, string[] fields)
        {
            try
            {
                Type type = AccessTools.TypeByName(typeName);
                if (type == null) return;
                foreach (string f in fields)
                {
                    try
                    {
                        FieldInfo fi = AccessTools.Field(type, f);
                        if (fi != null) { fi.SetValue(null, true); ModLog.Info($"主开关 {f} = true"); }
                    }
                    catch { }
                }
            }
            catch { }
        }

        // ─────────────────────────────────────────────
        // 5. 动画 NRE 防护
        // ─────────────────────────────────────────────
        private static void PatchAnimShields(Harmony harmony)
        {
            MethodInfo safeAnimPrefix = AccessTools.Method(typeof(CheatGuardBypass), nameof(SafeWeaponAnimInfoPrefix));
            MethodInfo safeViewPrefix = AccessTools.Method(typeof(CheatGuardBypass), nameof(SafeViewAnimPrefix));
            MethodInfo safeFinalizer  = AccessTools.Method(typeof(CheatGuardBypass), nameof(SafeFinalizer));

            // WeaponAnimInfoFactory.GetWeaponAnimInfo
            Type factory = AccessTools.TypeByName("Assets.Sources.Info.WeaponAnim.WeaponAnimInfoFactory");
            if (factory != null)
            {
                MethodInfo mi = AccessTools.Method(factory, "GetWeaponAnimInfo");
                if (mi != null && TryAddKey(factory.FullName + "::GetWeaponAnimInfo"))
                {
                    try { harmony.Patch(mi, new HarmonyMethod(safeAnimPrefix)); ModLog.Info("WeaponAnimInfoFactory.GetWeaponAnimInfo 防护注入"); }
                    catch { }
                }
            }

            // ViewAnimSystem.SetPlayViewAnimation
            Type viewAnim = AccessTools.TypeByName("Assets.Sources.Modules.Player.Anim.FirstPerson.ViewAnimSystem");
            if (viewAnim != null)
            {
                MethodInfo mi = AccessTools.Method(viewAnim, "SetPlayViewAnimation");
                if (mi != null && TryAddKey(viewAnim.FullName + "::SetPlayViewAnimation"))
                {
                    try { harmony.Patch(mi, new HarmonyMethod(safeViewPrefix)); ModLog.Info("ViewAnimSystem.SetPlayViewAnimation 防护注入"); }
                    catch { }
                }
            }

            // FirstPersonCleanupSystem.OnLoadResources — finalizer
            Type cleanup = AccessTools.TypeByName("Assets.Sources.Modules.Player.Anim.FirstPerson.FirstPersonCleanupSystem");
            if (cleanup != null)
            {
                MethodInfo mi = AccessTools.Method(cleanup, "OnLoadResources");
                if (mi != null && TryAddKey(cleanup.FullName + "::OnLoadResources"))
                {
                    try { harmony.Patch(mi, finalizer: new HarmonyMethod(safeFinalizer)); ModLog.Info("FirstPersonCleanupSystem.OnLoadResources 防护注入"); }
                    catch { }
                }
            }

            // LoadFirstHandSystem — finalizer 所有方法
            Type loadHand = AccessTools.TypeByName("Assets.Sources.Modules.Player.Anim.FirstPerson.LoadFirstHandSystem");
            if (loadHand != null)
            {
                foreach (MethodInfo m in loadHand.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (m.DeclaringType != loadHand || m.IsSpecialName || m.IsAbstract || m.ContainsGenericParameters) continue;
                    if (!TryAddKey(loadHand.FullName + "::" + m.Name)) continue;
                    try { harmony.Patch(m, finalizer: new HarmonyMethod(safeFinalizer)); }
                    catch { }
                }
                ModLog.Info("LoadFirstHandSystem 全部方法防护注入");
            }
        }

        // ─────────────────────────────────────────────
        // 6. 游戏强退与检测上报终结者防护
        // ─────────────────────────────────────────────
        private static void PatchGameTerminators(Harmony harmony)
        {
            MethodInfo block = AccessTools.Method(typeof(CheatGuardBypass), nameof(BlockPrefix));

            // 1. NetEaseCloudManager.Send (阻止截图/检测特征上报)
            Type netEase = AccessTools.TypeByName("Assets.Sources.Utils.NetEaseCloudManager");
            if (netEase != null)
            {
                PatchSafe(harmony, netEase, "Send", block);
                ModLog.Info("NetEaseCloudManager.Send 已拦截阻断");
            }

            // 2. GameController 退出与检测回调
            Type gameCtrl = AccessTools.TypeByName("GameController");
            if (gameCtrl != null)
            {
                MethodInfo blockEnum = AccessTools.Method(typeof(CheatGuardBypass), nameof(BlockIEnumeratorPrefix));
                PatchSafe(harmony, gameCtrl, "QuitUnityClientAfterReport", blockEnum);
                PatchSafe(harmony, gameCtrl, "OnRuntimeCheatGuardDetected", block);
                PatchSafe(harmony, gameCtrl, "OnAssetBundleLoadErrorCode", block);
                ModLog.Info("GameController 退出机制已拦截阻断");
            }

            // 3. Jump2GameRuleSendedReportHandler.Handle (阻止 packet 224 退出游戏)
            Type jumpHandler = AccessTools.TypeByName("Assets.Sources.Systems.PacketHandle.Handlers.Jump2GameRuleSendedReportHandler");
            if (jumpHandler != null)
            {
                PatchSafe(harmony, jumpHandler, "Handle", block);
                ModLog.Info("Jump2GameRuleSendedReportHandler [Packet 224] 已拦截阻断");
            }

            // 4. GameguardDataHandler.Handle (阻止 NP 认证失败强制退出)
            Type ggHandler = AccessTools.TypeByName("Assets.Sources.Systems.PacketHandle.Handlers.GameguardDataHandler");
            if (ggHandler != null)
            {
                PatchSafe(harmony, ggHandler, "Handle", block);
                ModLog.Info("GameguardDataHandler [Packet 234] 已拦截阻断");
            }

            // 5. AceReportModel.GameClose (阻止 ACE 弹窗倒计时退出)
            Type aceReport = AccessTools.TypeByName("Assets.Sources.Ui.Model.common.AceReportModel")
                          ?? AccessTools.TypeByName("Assets.Sources.Ui.Model.Common.AceReportModel");
            if (aceReport != null)
            {
                PatchSafe(harmony, aceReport, "GameClose", block);
                ModLog.Info("AceReportModel.GameClose 已拦截阻断");
            }

            // 6. EventLogger.StageLogger (过滤检测相关的日志上报)
            Type eventLogger = AccessTools.TypeByName("Assets.Sources.Utils.EventLogger.EventLogger");
            if (eventLogger != null)
            {
                MethodInfo stageLoggerPrefix = AccessTools.Method(typeof(CheatGuardBypass), nameof(SafeStageLoggerPrefix));
                foreach (MethodInfo mi in eventLogger.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (mi.Name == "StageLogger" && TryAddKey(eventLogger.FullName + "::StageLogger::" + mi.GetParameters().Length))
                    {
                        try { harmony.Patch(mi, new HarmonyMethod(stageLoggerPrefix)); ModLog.Info("EventLogger.StageLogger 过滤防护注入"); }
                        catch { }
                    }
                }
            }
        }

        public static bool SafeStageLoggerPrefix(string message)
        {
            if (string.IsNullOrEmpty(message)) return true;
            if (message.Contains("RuntimeCheatGuard") || message.Contains("AssetBundleMd5Mismatch") || message.Contains("Cheat"))
            {
                return false;
            }
            return true;
        }

        // ─────────────────────────────────────────────
        // 7. 第三人称视角原生 TPS 系统激活 (彻底消除闪频)
        // ─────────────────────────────────────────────
        private static void PatchTpsCamera(Harmony harmony)
        {
            // 1. Hook PlayerCareerUtil.IsTPS - 激活游戏内置原生 TPS 分支
            Type careerUtilType = AccessTools.TypeByName("share.constant.PlayerCareerUtil");
            if (careerUtilType != null)
            {
                MethodInfo isTpsMethod = AccessTools.Method(careerUtilType, "IsTPS");
                MethodInfo prefixIsTps = AccessTools.Method(typeof(CheatGuardBypass), nameof(PlayerCareerUtilIsTpsPrefix));
                if (isTpsMethod != null && TryAddKey("PlayerCareerUtil::IsTPS"))
                {
                    try
                    {
                        harmony.Patch(isTpsMethod, new HarmonyMethod(prefixIsTps));
                        ModLog.Info("PlayerCareerUtil.IsTPS 原生视角拦截注入成功");
                    }
                    catch (Exception ex)
                    {
                        ModLog.Warn("PlayerCareerUtil.IsTPS 注入异常: " + ex.Message);
                    }
                }
            }

            // 2. Hook TpsCameraLogic.IsActive - 使用 POSTFIX，保留官方原生坐标/角度/过渡计算，彻底消灭频闪
            Type tpsType = AccessTools.TypeByName("Assets.Sources.Info.Camera.CameraLogic.TpsCameraLogic");
            if (tpsType != null)
            {
                MethodInfo isActive = AccessTools.Method(tpsType, "IsActive");
                MethodInfo postfixIsActive = AccessTools.Method(typeof(CheatGuardBypass), nameof(TpsIsActivePostfix));
                if (isActive != null && TryAddKey("TpsCameraLogic::IsActive"))
                {
                    try
                    {
                        harmony.Patch(isActive, postfix: new HarmonyMethod(postfixIsActive));
                        ModLog.Info("TpsCameraLogic.IsActive Postfix 注入成功 (消除闪频)");
                    }
                    catch (Exception ex)
                    {
                        ModLog.Warn("TpsCameraLogic.IsActive 注入异常: " + ex.Message);
                    }
                }

                MethodInfo update = AccessTools.Method(tpsType, "Update");
                MethodInfo postfixUpdate = AccessTools.Method(typeof(CheatGuardBypass), nameof(TpsUpdatePostfix));
                if (update != null && TryAddKey("TpsCameraLogic::Update"))
                {
                    try
                    {
                        harmony.Patch(update, postfix: new HarmonyMethod(postfixUpdate));
                        ModLog.Info("TpsCameraLogic.Update 视角参数注入成功");
                    }
                    catch (Exception ex)
                    {
                        ModLog.Warn("TpsCameraLogic.Update 注入异常: " + ex.Message);
                    }
                }
            }
        }

        public static bool PlayerCareerUtilIsTpsPrefix(ref bool __result)
        {
            var cfg = Plugin.ConfigInstance;
            if (cfg != null && cfg.ThirdPersonEnabled)
            {
                __result = true;
                return false; // 返回 true，让官方原生 IsActive 计算所有坐标与过渡
            }
            return true;
        }

        public static void TpsIsActivePostfix(object __instance, ref bool __result)
        {
            var cfg = Plugin.ConfigInstance;
            if (cfg != null && cfg.ThirdPersonEnabled)
            {
                var worldCam = Contexts.sharedInstance?.worldCamera;
                if (worldCam != null && worldCam.hasCameraData)
                {
                    worldCam.cameraData.IsTps = true;
                    if (cfg.ThirdPersonFov > 0)
                    {
                        worldCam.cameraData.Fov = (int)cfg.ThirdPersonFov;
                    }
                    if (worldCam.cameraData.TransTime < 230)
                    {
                        worldCam.cameraData.TransTime = 230;
                    }
                    __result = true;
                }
            }
        }

        public static void TpsUpdatePostfix(object __instance)
        {
            var cfg = Plugin.ConfigInstance;
            if (cfg != null && cfg.ThirdPersonEnabled && __instance != null)
            {
                try
                {
                    FieldInfo distField = __instance.GetType().GetField("_distance", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (distField != null && cfg.ThirdPersonDistance > 0)
                    {
                        distField.SetValue(__instance, cfg.ThirdPersonDistance);
                    }
                    var worldCam = Contexts.sharedInstance?.worldCamera;
                    if (worldCam != null && worldCam.hasCameraData && cfg.ThirdPersonFov > 0)
                    {
                        worldCam.cameraData.Fov = (int)cfg.ThirdPersonFov;
                    }
                }
                catch { }
            }
        }

        // ─────────────────────────────────────────────
        // 8. GameGuard 守护进程与服务后台查杀
        // ─────────────────────────────────────────────
        private static void StartGameGuardTerminator()
        {
            try
            {
                var t = new System.Threading.Thread(() =>
                {
                    string[] targets = { "GameMon64", "GameMon", "npggNT64", "npggNT", "npmon2", "npggsvc" };
                    while (true)
                    {
                        try
                        {
                            foreach (string target in targets)
                            {
                                foreach (var p in System.Diagnostics.Process.GetProcessesByName(target))
                                {
                                    try { p.Kill(); } catch { }
                                }
                            }
                        }
                        catch { }
                        System.Threading.Thread.Sleep(2500);
                    }
                });
                t.IsBackground = true;
                t.Name = "GGTerminator";
                t.Start();
                ModLog.Info("GameGuard 后台查杀守护线程已启动");
            }
            catch { }
        }

        // ─────────────────────────────────────────────
        // 9. 角色与手模异步资源加载守卫 (防换模失败、防隐身、防频闪)
        // ─────────────────────────────────────────────
        private static readonly Dictionary<string, int> _bundleWaitFrames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private static void PatchModelLoading(Harmony harmony)
        {
            Type tpsLoadType = AccessTools.TypeByName("Assets.Sources.Modules.Player.Anim.ThirdPerson.LoadThirdPersonSystem");
            if (tpsLoadType != null)
            {
                MethodInfo mi = AccessTools.Method(tpsLoadType, "OnLoadEntityResource");
                if (mi != null)
                {
                    MethodInfo prefix = AccessTools.Method(typeof(CheatGuardBypass), nameof(LoadThirdPersonResourcePrefix));
                    harmony.Patch(mi, new HarmonyMethod(prefix));
                    ModLog.Info("[Patch] LoadThirdPersonSystem.OnLoadEntityResource() 已注入异步资源守卫！");
                }
            }

            Type fpsLoadType = AccessTools.TypeByName("Assets.Sources.Modules.Player.Anim.FirstPerson.LoadFirstHandSystem");
            if (fpsLoadType != null)
            {
                MethodInfo mi = AccessTools.Method(fpsLoadType, "OnLoadEntityResource");
                if (mi != null)
                {
                    MethodInfo prefix = AccessTools.Method(typeof(CheatGuardBypass), nameof(LoadFirstHandResourcePrefix));
                    harmony.Patch(mi, new HarmonyMethod(prefix));
                    ModLog.Info("[Patch] LoadFirstHandSystem.OnLoadEntityResource() 已注入手模异步资源守卫！");
                }
            }
        }

        public static bool LoadThirdPersonResourcePrefix(object entity)
        {
            if (entity == null) return true;
            try
            {
                object basicInfo = SingleSkinMod.Skin.ModelReplacer.GetMember(entity, "basicInfo");
                if (basicInfo == null) return true;
                object careerInfo = SingleSkinMod.Skin.ModelReplacer.GetMember(basicInfo, "CareerInfo");
                if (careerInfo == null) return true;

                string prefabBundle = SingleSkinMod.Skin.ModelReplacer.GetPropString(careerInfo, "PrefabBundleName");
                string animBundle = SingleSkinMod.Skin.ModelReplacer.GetPropString(careerInfo, "AnimBundleName");
                string dynAnimBundle = SingleSkinMod.Skin.ModelReplacer.GetPropString(careerInfo, "DynAnimBundleName");

                bool allReady = true;

                if (!string.IsNullOrEmpty(prefabBundle) && !CheckOrWaitBundle(prefabBundle))
                {
                    allReady = false;
                }
                if (!string.IsNullOrEmpty(animBundle) && !CheckOrWaitBundle(animBundle))
                {
                    allReady = false;
                }
                if (!string.IsNullOrEmpty(dynAnimBundle) && !CheckOrWaitBundle(dynAnimBundle))
                {
                    allReady = false;
                }

                if (!allReady)
                {
                    return false; // 阻断本帧 LoadThirdPersonSystem，等待 Bundle 就绪
                }
            }
            catch { }
            return true;
        }

        public static bool LoadFirstHandResourcePrefix(object entity)
        {
            if (entity == null) return true;
            try
            {
                object basicInfo = SingleSkinMod.Skin.ModelReplacer.GetMember(entity, "basicInfo");
                if (basicInfo == null) return true;
                object current = SingleSkinMod.Skin.ModelReplacer.GetMember(basicInfo, "Current");
                if (current == null) return true;
                string handName = SingleSkinMod.Skin.ModelReplacer.GetPropString(current, "CurrentHandName");
                if (!string.IsNullOrEmpty(handName) && (handName == "man" || handName == "woman"))
                {
                    string handBundle = "viewhand/" + handName;
                    if (!CheckOrWaitBundle(handBundle))
                    {
                        return false;
                    }
                }
            }
            catch { }
            return true;
        }

        private static bool CheckOrWaitBundle(string bundleName)
        {
            if (IsBundleLoaded(bundleName))
            {
                _bundleWaitFrames.Remove(bundleName);
                return true;
            }

            // 发起加载
            SingleSkinMod.Skin.ModelReplacer.PreloadAssetBundle(bundleName);

            if (!_bundleWaitFrames.TryGetValue(bundleName, out int count)) count = 0;
            count++;
            _bundleWaitFrames[bundleName] = count;
            if (count > 120)
            {
                ModLog.Warn($"AssetBundle [{bundleName}] 加载等待超时，放行原生流程。");
                return true;
            }

            return false;
        }

        public static bool IsBundleLoaded(string bundleName)
        {
            if (string.IsNullOrEmpty(bundleName)) return true;
            try
            {
                Type abMgrType = SingleSkinMod.Skin.ModelReplacer.FindType("SSJJAsset.AssetBundle.AssetBundleManager");
                if (abMgrType == null) return true;

                FieldInfo loadedField = abMgrType.GetField("m_LoadedAssetBundles", BindingFlags.NonPublic | BindingFlags.Static);
                if (loadedField != null)
                {
                    var dict = loadedField.GetValue(null) as System.Collections.IDictionary;
                    if (dict != null)
                    {
                        foreach (var key in dict.Keys)
                        {
                            if (key != null && string.Equals(key.ToString(), bundleName, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public static bool BlockIEnumeratorPrefix(ref System.Collections.IEnumerator __result)
        {
            __result = null;
            return false;
        }

        // ─────────────────────────────────────────────
        // 工具方法
        // ─────────────────────────────────────────────

        /// <summary>精确 patch 一个方法，每步独立 try-catch，绝不 crash</summary>
        private static void PatchSafe(Harmony harmony, Type type, string methodName, MethodInfo prefix)
        {
            if (type == null || string.IsNullOrEmpty(methodName)) return;
            string key = (type.FullName ?? type.Name) + "::" + methodName;
            if (!TryAddKey(key)) return;
            try
            {
                MethodInfo mi = AccessTools.Method(type, methodName);
                if (mi == null) return;
                // 跳过 native / extern / 含泛型 / abstract
                if (mi.IsAbstract || mi.ContainsGenericParameters) return;
                if ((mi.GetMethodImplementationFlags() & MethodImplAttributes.InternalCall) != 0) return;
                if ((mi.GetMethodImplementationFlags() & MethodImplAttributes.Native) != 0) return;
                harmony.Patch(mi, new HarmonyMethod(prefix));
                ModLog.Info($"[Patch] {type.Name}.{methodName}() 已拦截");
            }
            catch (Exception ex)
            {
                ModLog.Warn($"[Patch失败] {type.Name}.{methodName}: {ex.GetType().Name}");
            }
        }

        private static bool TryAddKey(string key)
        {
            if (_patchedKeys.Contains(key)) return false;
            _patchedKeys.Add(key);
            return true;
        }

        private static Type FindTypeBySimpleName(string name)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { foreach (Type t in asm.GetTypes()) { if (t.Name == name) return t; } }
                catch { }
            }
            return null;
        }

        // ─────────────────────────────────────────────
        // Patch 回调
        // ─────────────────────────────────────────────

        /// <summary>通用拦截 prefix：return false 跳过原始方法</summary>
        public static bool BlockPrefix() => false;

        public static bool SafeWeaponAnimInfoPrefix(object weaponInfo, ref object __result)
        {
            if (weaponInfo != null) return true;
            __result = null;
            return false;
        }

        public static bool SafeViewAnimPrefix(object entity)
        {
            if (entity == null) return false;
            try
            {
                object curWeapon = SingleSkinMod.Skin.ModelReplacer.GetMember(entity, "currentWeapon");
                if (curWeapon == null) return false;
                object weaponInfo = SingleSkinMod.Skin.ModelReplacer.GetMember(curWeapon, "WeaponInfo");
                return weaponInfo != null;
            }
            catch { return false; }
        }

        public static Exception SafeFinalizer(Exception __exception) => null;

        // ─────────────────────────────────────────────
        // 加载界面自定义壁纸 Patch
        // ─────────────────────────────────────────────
        private static void PatchLoadingScreen(Harmony harmony)
        {
            if (harmony == null) return;
            try
            {
                Type mgrType = AccessTools.TypeByName("SSJJ.BattleLoading.BattleLoadingManager")
                            ?? FindTypeBySimpleName("BattleLoadingManager");
                if (mgrType != null)
                {
                    MethodInfo loadBackImage = AccessTools.Method(mgrType, "LoadBackImage");
                    if (loadBackImage != null)
                    {
                        MethodInfo prefix = AccessTools.Method(typeof(CheatGuardBypass), nameof(LoadBackImagePrefix));
                        PatchSafe(harmony, mgrType, "LoadBackImage", prefix);
                    }
                }

                Type vmType = AccessTools.TypeByName("SSJJ.BattleLoading.BattleLoadingViewModel")
                           ?? FindTypeBySimpleName("BattleLoadingViewModel");
                if (vmType != null)
                {
                    MethodInfo setBackSprite = AccessTools.Method(vmType, "SetBackSprite");
                    if (setBackSprite != null)
                    {
                        MethodInfo postfix = AccessTools.Method(typeof(CheatGuardBypass), nameof(SetBackSpritePostfix));
                        string key = vmType.FullName + "::SetBackSprite";
                        if (TryAddKey(key))
                        {
                            harmony.Patch(setBackSprite, postfix: new HarmonyMethod(postfix));
                            ModLog.Info("[Patch] BattleLoadingViewModel.SetBackSprite 已拦截");
                        }
                    }

                    MethodInfo vmStart = AccessTools.Method(vmType, "Start");
                    if (vmStart != null)
                    {
                        MethodInfo postfix = AccessTools.Method(typeof(CheatGuardBypass), nameof(LoadingVmStartPostfix));
                        string key = vmType.FullName + "::Start";
                        if (TryAddKey(key))
                        {
                            harmony.Patch(vmStart, postfix: new HarmonyMethod(postfix));
                            ModLog.Info("[Patch] BattleLoadingViewModel.Start 已拦截");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Warn("PatchLoadingScreen 异常: " + ex.Message);
            }
        }

        public static bool LoadBackImagePrefix(string image, object sprite, ref System.Collections.IEnumerator __result)
        {
            try
            {
                UnityEngine.Sprite custom = SingleSkinMod.Visual.LoadingScreenController.GetCustomSprite();
                if (custom != null)
                {
                    if (sprite != null)
                    {
                        PropertyInfo valProp = AccessTools.Property(sprite.GetType(), "Value");
                        if (valProp != null)
                        {
                            valProp.SetValue(sprite, custom, null);
                        }
                    }
                    __result = EmptyLoadingCoroutine();
                    return false;
                }
            }
            catch (Exception ex)
            {
                ModLog.Warn("LoadBackImagePrefix 异常: " + ex.Message);
            }
            return true;
        }

        private static System.Collections.IEnumerator EmptyLoadingCoroutine()
        {
            yield break;
        }

        public static void SetBackSpritePostfix(object __instance)
        {
            SingleSkinMod.Visual.LoadingScreenController.ApplyToViewModel(__instance);
        }

        public static void LoadingVmStartPostfix(object __instance)
        {
            SingleSkinMod.Visual.LoadingScreenController.ApplyToViewModel(__instance);
        }
    }
}

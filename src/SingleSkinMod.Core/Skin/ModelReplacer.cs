using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using SingleSkinMod.Common;
using SingleSkinMod.Config;

namespace SingleSkinMod.Skin
{
    internal sealed class SkinSnapshot
    {
        internal string Weapon;
        internal string Career;
        internal string Hand;
        internal string Accessory;
        internal float Scale;
        internal float Head;
        internal int Team;
        internal int Alpha;
        internal int SelfAlpha;

        internal static SkinSnapshot Capture(object info)
        {
            string weapon = ModelReplacer.GetPropString(info, "CurrentWeaponName");
            string career = ModelReplacer.GetPropString(info, "Career");
            string hand = ModelReplacer.GetPropString(info, "CurrentHandName");
            string acc = ModelReplacer.GetPropString(info, "BackAccessory");

            if (string.IsNullOrWhiteSpace(career) && string.IsNullOrWhiteSpace(weapon))
            {
                return null;
            }

            return new SkinSnapshot
            {
                Weapon = weapon,
                Career = career,
                Hand = hand,
                Accessory = acc,
                Scale = ModelReplacer.GetPropFloat(info, "Scale", 1.0f),
                Head = ModelReplacer.GetPropFloat(info, "HeadEnlarge", 1.0f),
                Team = ModelReplacer.GetPropInt(info, "Team", 0),
                Alpha = ModelReplacer.GetPropInt(info, "Alpha", 100),
                SelfAlpha = ModelReplacer.GetPropInt(info, "SelfAlpha", 100)
            };
        }

        internal void Restore(object info)
        {
            if (!string.IsNullOrWhiteSpace(Weapon))
            {
                ModelReplacer.SetProp(info, "CurrentWeaponName", Weapon);
                ModelReplacer.SetProp(info, "WeaponName", Weapon);
            }
            if (!string.IsNullOrWhiteSpace(Career))
            {
                ModelReplacer.SetProp(info, "Career", Career);
            }
            if (!string.IsNullOrWhiteSpace(Hand))
            {
                ModelReplacer.SetProp(info, "CurrentHandName", Hand);
                ModelReplacer.SetProp(info, "HandName", Hand);
            }
            if (!string.IsNullOrWhiteSpace(Accessory))
            {
                ModelReplacer.SetProp(info, "BackAccessory", Accessory);
                ModelReplacer.SetProp(info, "Accessory", Accessory);
            }
            ModelReplacer.SetProp(info, "Scale", Scale);
            ModelReplacer.SetProp(info, "HeadEnlarge", Head);
            ModelReplacer.SetProp(info, "HeadScale", Head);
            if (Team > 0) ModelReplacer.SetProp(info, "Team", Team);
            ModelReplacer.SetProp(info, "Alpha", Alpha);
            ModelReplacer.SetProp(info, "SelfAlpha", SelfAlpha);
        }
    }

    public static class ModelReplacer
    {
        private const BindingFlags AllFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static readonly Dictionary<object, SkinSnapshot> Snapshots = new Dictionary<object, SkinSnapshot>();

        private static object _lastEntity;
        private static object _lastBasicInfo;
        private static object _lastInfo;
        private static Type _contextsType;

        // Anti-flicker & Anti-kick state tracking
        private static object _cachedEntity;
        private static string _appliedWeapon;
        private static string _appliedCharacter;
        private static string _appliedAccessory;
        private static float _appliedScale = -1f;
        private static float _appliedHeadScale = -1f;
        private static int _appliedTeam = -1;

        public static bool TryGetLocalPlayer(out object entity, out object basicInfo, out object info)
        {
            entity = basicInfo = info = null;
            try
            {
                _contextsType = _contextsType ?? FindType("Contexts");
                if (_contextsType == null) return false;

                object contexts = GetMember(_contextsType, "sharedInstance");
                if (contexts == null) return false;

                object playerContext = GetMember(contexts, "player");
                if (playerContext == null) return false;

                object entitiesObj = CallMethod(playerContext, "GetEntities");
                if (!(entitiesObj is IEnumerable entities)) return false;

                foreach (object candidate in entities)
                {
                    if (!GetPropBool(candidate, "isMyPlayer")) continue;
                    if (!GetPropBool(candidate, "hasBasicInfo")) continue;

                    object component = GetMember(candidate, "basicInfo");
                    if (component == null) continue;

                    object data = GetMember(component, "Current");
                    if (data == null) continue;

                    entity = candidate;
                    basicInfo = component;
                    info = data;
                    return true;
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("TryGetLocalPlayer 异常: " + ex.Message);
            }
            return false;
        }

        public static bool ApplyAll(ModConfig config, bool quiet = false)
        {
            if (!TryGetLocalPlayer(out object entity, out object basicInfo, out object info))
            {
                if (!quiet) ModLog.Info("未检测到当前局内本地方家实体，进入战斗后将自动应用。");
                return false;
            }

            // Anti-flicker debounce: if entity and all properties match what's already applied, skip re-applying
            bool sameEntity = (_cachedEntity == entity);
            bool sameWeapon = (_appliedWeapon == config.Weapon);
            bool sameChar = (_appliedCharacter == config.Character);
            bool sameAcc = (_appliedAccessory == config.Accessory);
            bool sameScale = Mathf.Abs(_appliedScale - config.Scale) < 0.01f;
            bool sameHead = Mathf.Abs(_appliedHeadScale - config.HeadScale) < 0.01f;
            bool sameTeam = (_appliedTeam == config.Team);

            if (sameEntity && sameWeapon && sameChar && sameAcc && sameScale && sameHead && sameTeam)
            {
                return true;
            }

            _cachedEntity = entity;
            _lastEntity = entity;
            _lastBasicInfo = basicInfo;
            _lastInfo = info;

            if (!Snapshots.ContainsKey(entity))
            {
                var snap = SkinSnapshot.Capture(info);
                if (snap != null)
                {
                    Snapshots[entity] = snap;
                }
            }

            if (!string.IsNullOrWhiteSpace(config.Weapon))
            {
                string curWeapon = GetPropString(info, "CurrentWeaponName");
                if (curWeapon != config.Weapon || _appliedWeapon != config.Weapon)
                {
                    SetProp(info, "CurrentWeaponName", config.Weapon);
                    SetProp(info, "WeaponName", config.Weapon);

                    object curWeaponComp = GetMember(entity, "currentWeapon");
                    if (curWeaponComp != null)
                    {
                        SetProp(curWeaponComp, "Name", config.Weapon);
                        Type factoryType = FindType("Assets.Sources.Info.Weapon.WeaponInfoFactory");
                        if (factoryType != null)
                        {
                            object factoryInst = GetMember(factoryType, "Instance");
                            if (factoryInst != null)
                            {
                                object newInfo = CallMethod(factoryInst, "GetWeaponInfo", config.Weapon);
                                if (newInfo != null)
                                {
                                    SetProp(curWeaponComp, "WeaponInfo", newInfo);
                                }
                            }
                        }
                    }

                    SetProp(entity, "isLoadWeapon", true);
                }
            }

            if (!string.IsNullOrWhiteSpace(config.Character))
            {
                int team = GetPropInt(info, "Team", 1);
                if (team <= 0) team = 1;
                string mappedCareer = NormalizeCareerForTeam(config.Character, team);
                string curCareer = GetPropString(info, "Career");
                if (curCareer != mappedCareer || _appliedCharacter != config.Character)
                {
                    SetProp(info, "Career", mappedCareer);

                    // 同步更新手部模型名：严格为 "woman" 或 "man"，严禁使用角色名导致 viewhand/ 找不到资产
                    Type utilType = FindType("share.constant.PlayerCareerUtil");
                    bool isWoman = false;
                    if (utilType != null)
                    {
                        object isW = CallMethod(utilType, "IsWoman", mappedCareer);
                        if (isW is bool b) isWoman = b;
                    }
                    if (!isWoman && (mappedCareer.Contains("woman") || mappedCareer.Contains("girl") || mappedCareer.Contains("pandora") || mappedCareer.Contains("luoli") || mappedCareer.Contains("nvpu") || mappedCareer.Contains("diaochan") || mappedCareer.Contains("sunshangxiang") || mappedCareer.Contains("zhenji") || mappedCareer.Contains("xianhu") || mappedCareer.Contains("nurse")))
                    {
                        isWoman = true;
                    }
                    string handName = isWoman ? "woman" : "man";
                    SetProp(info, "CurrentHandName", handName);

                    // 同步更新 CareerInfo
                    Type careerFactoryType = FindType("Assets.Sources.Info.Career.CareerInfoFactory");
                    object newCareerInfo = null;
                    if (careerFactoryType != null)
                    {
                        object factoryInst = GetMember(careerFactoryType, "Instance");
                        if (factoryInst != null)
                        {
                            newCareerInfo = CallMethod(factoryInst, "GetCareerInfo", mappedCareer);
                            if (newCareerInfo != null)
                            {
                                SetProp(basicInfo, "CareerInfo", newCareerInfo);
                            }
                        }
                    }

                    // 预加载所有角色相关 AssetBundle
                    if (newCareerInfo != null)
                    {
                        string prefabBundle = GetPropString(newCareerInfo, "PrefabBundleName");
                        string animBundle = GetPropString(newCareerInfo, "AnimBundleName");
                        string dynAnimBundle = GetPropString(newCareerInfo, "DynAnimBundleName");
                        PreloadAssetBundle(prefabBundle);
                        PreloadAssetBundle(animBundle);
                        PreloadAssetBundle(dynAnimBundle);
                    }
                    PreloadAssetBundle("viewhand/" + handName);
                }
            }

            // 注意：绝不在外部频繁暴力调用 CleanupThirdPersonUnityObjects！
            // 游戏的 LoadThirdPersonSystem 和 FirstPersonCleanupSystem 在检测到 CareerInfo 与 CurrentHandName
            // 变更后，将配合 CheatGuardBypass 的安全加载守卫，平滑无缝地在 LoadDone 中原子替换模型，绝无隐身或频闪。

            if (!string.IsNullOrWhiteSpace(config.Accessory))
            {
                string curAcc = GetPropString(info, "BackAccessory");
                if (curAcc != config.Accessory)
                {
                    SetProp(info, "BackAccessory", config.Accessory);
                    SetProp(info, "Accessory", config.Accessory);
                }
            }
            else if (config.Accessory == "")
            {
                string curAcc = GetPropString(info, "BackAccessory");
                if (!string.IsNullOrEmpty(curAcc))
                {
                    SetProp(info, "BackAccessory", "");
                    SetProp(info, "Accessory", "");
                }
            }

            if (Mathf.Abs(config.Scale - 1.0f) > 0.01f)
            {
                SetProp(info, "Scale", config.Scale);
                try { TryScaleViewModel(entity, config.Scale); } catch { }
            }

            if (Mathf.Abs(config.HeadScale - 1.0f) > 0.01f)
            {
                SetProp(info, "HeadEnlarge", config.HeadScale);
                SetProp(info, "HeadScale", config.HeadScale);
            }

            // CRITICAL: NEVER overwrite Team with 0 (which corrupts battle team and causes server kick)
            if (config.Team > 0)
            {
                SetProp(info, "Team", config.Team);
            }

            if (config.Alpha < 100) SetProp(info, "Alpha", config.Alpha);
            if (config.SelfAlpha < 100) SetProp(info, "SelfAlpha", config.SelfAlpha);

            _appliedWeapon = config.Weapon;
            _appliedCharacter = config.Character;
            _appliedAccessory = config.Accessory;
            _appliedScale = config.Scale;
            _appliedHeadScale = config.HeadScale;
            _appliedTeam = config.Team;

            if (!quiet)
            {
                ModLog.Info($"模型应用成功: 武器={config.Weapon}, 角色={config.Character}, 挂件={config.Accessory}, 缩放={config.Scale:0.00}");
            }
            return true;
        }

        public static bool RestoreOriginal()
        {
            _cachedEntity = null;
            _appliedWeapon = null;
            _appliedCharacter = null;
            _appliedAccessory = null;

            if (TryGetLocalPlayer(out object entity, out object basicInfo, out object info))
            {
                if (Snapshots.TryGetValue(entity, out SkinSnapshot snapshot))
                {
                    snapshot.Restore(info);
                    SetProp(basicInfo, "Current", info);

                    // 同步还原 CareerInfo
                    if (!string.IsNullOrEmpty(snapshot.Career))
                    {
                        Type careerFactoryType = FindType("Assets.Sources.Info.Career.CareerInfoFactory");
                        if (careerFactoryType != null)
                        {
                            object factoryInst = GetMember(careerFactoryType, "Instance");
                            if (factoryInst != null)
                            {
                                object originalCareerInfo = CallMethod(factoryInst, "GetCareerInfo", snapshot.Career);
                                if (originalCareerInfo != null)
                                {
                                    SetProp(basicInfo, "CareerInfo", originalCareerInfo);
                                }
                            }
                        }
                    }

                    Snapshots.Remove(entity);
                    ModLog.Info("已还原玩家初始模型配置。");
                    return true;
                }
            }
            return false;
        }

        private static string NormalizeCareerForTeam(string career, int team)
        {
            if (string.IsNullOrEmpty(career)) return career;

            // 潘多拉多阵营模型智能纠偏
            if (career.Equals("pandora", StringComparison.OrdinalIgnoreCase) ||
                career.Equals("pandora_01", StringComparison.OrdinalIgnoreCase) ||
                career.Equals("pandora_02", StringComparison.OrdinalIgnoreCase))
            {
                return (team == 2) ? "pandora_t" : "pandora_ct";
            }

            // 黄晓明多阵营
            if (career.Equals("hxm", StringComparison.OrdinalIgnoreCase))
            {
                return (team == 2) ? "hxm_t" : "hxm_ct";
            }

            // 幽灵多阵营
            if (career.Equals("ghost", StringComparison.OrdinalIgnoreCase))
            {
                return (team == 2) ? "ghost_t" : "ghost";
            }

            // 电音少女多阵营
            if (career.Equals("dj_girl", StringComparison.OrdinalIgnoreCase))
            {
                return (team == 2) ? "dj_girl_t" : "dj_girl";
            }

            // 机甲男女
            if (career.Equals("mechaman", StringComparison.OrdinalIgnoreCase))
            {
                return (team == 2) ? "mechaman_t" : "mechaman_ct";
            }
            if (career.Equals("mechawoman", StringComparison.OrdinalIgnoreCase))
            {
                return (team == 2) ? "mechawoman_t" : "mechawoman_ct";
            }

            return career;
        }

        public static void PreloadAssetBundle(string bundleName)
        {
            if (string.IsNullOrEmpty(bundleName)) return;
            try
            {
                Type abMgrType = FindType("SSJJAsset.AssetBundle.AssetBundleManager");
                Type ignoreCaseType = FindType("SSJJBase.String.IgnoreCaseString");
                if (abMgrType != null && ignoreCaseType != null)
                {
                    object igStr = Activator.CreateInstance(ignoreCaseType, bundleName);
                    MethodInfo loadMethod = abMgrType.GetMethod("LoadAssetBundle", BindingFlags.Public | BindingFlags.Static, null, new Type[] { ignoreCaseType.MakeByRefType(), typeof(bool) }, null);
                    if (loadMethod != null)
                    {
                        object[] args = new object[] { igStr, false };
                        loadMethod.Invoke(null, args);
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Warn($"PreloadAssetBundle({bundleName}) 异常: {ex.Message}");
            }
        }

        private static void TryScaleViewModel(object entity, float scale)
        {
            if (entity == null) return;
            string[] names = { "view", "unityView", "gameObject", "View", "model", "Model" };
            object view = null;
            foreach (string n in names)
            {
                view = GetMember(entity, n);
                if (view != null) break;
            }
            if (view == null) return;

            GameObject go = view as GameObject;
            if (go == null) go = GetMember(view, "gameObject") as GameObject;
            if (go != null && go.transform != null)
            {
                go.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
                return;
            }

            Transform tr = view as Transform;
            if (tr == null) tr = GetMember(view, "transform") as Transform;
            if (tr != null) tr.localScale = Vector3.one * Mathf.Max(0.01f, scale);
        }

        #region Reflection Helpers
        public static Type FindType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = assembly.GetType(fullName, false);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        public static object GetMember(object target, string name)
        {
            if (target == null) return null;
            Type t = target as Type ?? target.GetType();
            object inst = target is Type ? null : target;

            PropertyInfo prop = t.GetProperty(name, AllFlags);
            if (prop != null) return prop.GetValue(inst, null);

            FieldInfo fi = t.GetField(name, AllFlags);
            return fi != null ? fi.GetValue(inst) : null;
        }

        public static bool SetProp(object target, string name, object value)
        {
            if (target == null) return false;
            Type t = target.GetType();

            PropertyInfo prop = t.GetProperty(name, AllFlags);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(target, ConvertValue(value, prop.PropertyType), null);
                return true;
            }

            FieldInfo fi = t.GetField(name, AllFlags);
            if (fi != null)
            {
                fi.SetValue(target, ConvertValue(value, fi.FieldType));
                return true;
            }
            return false;
        }

        public static object CallMethod(object target, string name, params object[] args)
        {
            if (target == null) return null;
            MethodInfo mi = target.GetType().GetMethods(AllFlags)
                .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == args.Length);
            return mi?.Invoke(target, args);
        }

        public static string GetPropString(object target, string name) => GetMember(target, name)?.ToString() ?? "";
        public static float GetPropFloat(object target, string name, float fallback = 0f)
        {
            try { object v = GetMember(target, name); return v != null ? Convert.ToSingle(v) : fallback; }
            catch { return fallback; }
        }
        public static int GetPropInt(object target, string name, int fallback = 0)
        {
            try { object v = GetMember(target, name); return v != null ? Convert.ToInt32(v) : fallback; }
            catch { return fallback; }
        }
        public static bool GetPropBool(object target, string name)
        {
            try { object v = GetMember(target, name); return v != null && Convert.ToBoolean(v); }
            catch { return false; }
        }

        private static object ConvertValue(object val, Type type)
        {
            if (val == null || type.IsInstanceOfType(val)) return val;
            return Convert.ChangeType(val, type);
        }
        #endregion
    }
}

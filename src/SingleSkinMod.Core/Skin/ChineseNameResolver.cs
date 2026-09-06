using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SingleSkinMod.Common;

namespace SingleSkinMod.Skin
{
    public static class ChineseNameResolver
    {
        private static readonly Dictionary<string, string> WeaponResolvedCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> CareerResolvedCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // ─────────────────────────────────────────────
        // 1. 预置武器中文词典 (启动即显，无需等待 Tables 载入)
        // ─────────────────────────────────────────────
        private static readonly Dictionary<string, string> WeaponFallbackMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // 荣耀系列
            { "ak47_glory", "AK47-荣耀" },
            { "m4a1_glory", "M4A1-荣耀" },
            { "awp_glory", "AWP-荣耀" },
            { "barrett_glory", "巴雷特-荣耀" },
            { "nepal_glory", "尼泊尔-荣耀" },
            { "deagle_glory", "沙鹰-荣耀" },

            // 黄金系列
            { "ak47_gold", "黄金AK47" },
            { "m4a1_gold", "黄金M4A1" },
            { "awp_gold", "黄金AWP" },
            { "barrett_gold", "黄金巴雷特" },
            { "deagle_gold", "黄金沙鹰" },
            { "nepal_gold", "黄金尼泊尔" },
            { "gatling_gold", "黄金加特林" },
            { "rpk_gold", "黄金RPK" },
            { "axe_gold", "黄金军斧" },
            { "shovel_gold", "黄金工兵铲" },
            { "knife_gold", "黄金军刀" },

            // 英雄级与经典武器
            { "ak47_beast", "AK47-魔龙" },
            { "m4a1_laser", "M4A1-激光" },
            { "m4a1_thunder", "M4A1-雷霆" },
            { "barrett_aurora", "巴雷特-极光" },
            { "barrett_ice", "巴雷特-冰霜" },
            { "awp_dragon", "AWP-龙皇" },
            { "knife_death", "死神之镰" },
            { "bow_shenfa", "神罚战弓" },
            { "laser_sword", "光剑" },
            { "nunchakus_legend", "龙之双截棍" },
            { "katana_bloody", "修罗武士刀" },

            // 原生制式枪械
            { "ak47", "AK47" },
            { "m4a1", "M4A1" },
            { "awp", "AWP 狙击步枪" },
            { "barrett", "巴雷特 M82A1" },
            { "deagle", "沙漠之鹰" },
            { "glock18", "格洛克 18" },
            { "usp", "USP 手枪" },
            { "colt", "柯尔特左轮" },
            { "mp5", "MP5 冲锋枪" },
            { "p90", "P90 冲锋枪" },
            { "ump45", "UMP45 冲锋枪" },
            { "kriss_super_v", "斯泰尔/Vector" },
            { "xm1014", "XM1014 霰弹枪" },
            { "aa12", "AA-12 连发霰弹枪" },
            { "jackhammer", "气锤霰弹枪" },
            { "gatling", "加特林机枪" },
            { "rpk", "RPK 轻机枪" },
            { "m249", "M249 班用机枪" },
            { "knife", "标准军刀" },
            { "nepal", "尼泊尔军刀" },
            { "axe", "军用手斧" },
            { "shovel", "工兵铲" },
            { "katana", "武士刀" },
            { "grenade", "高爆手雷" },
            { "flashbang", "闪光手雷" },
            { "smoke", "烟雾弹" }
        };

        // ─────────────────────────────────────────────
        // 2. 预置角色中文词典
        // ─────────────────────────────────────────────
        private static readonly Dictionary<string, string> CareerFallbackMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // 热门人类角色
            { "pandora_ct", "潘多拉 [守卫者]" },
            { "pandora_t", "潘多拉 [风暴者]" },
            { "pandora", "潘多拉" },
            { "pandora_01", "潘多拉 (一阶)" },
            { "pandora_02", "潘多拉 (二阶)" },
            { "hxm_ct", "黄晓明 [守卫者]" },
            { "hxm_t", "黄晓明 [风暴者]" },
            { "ghost", "幽灵 [守卫者]" },
            { "ghost_t", "幽灵 [风暴者]" },
            { "dj_girl", "电音少女 [守卫者]" },
            { "dj_girl_t", "电音少女 [风暴者]" },
            { "mechaman_ct", "机甲男 [守卫者]" },
            { "mechaman_t", "机甲男 [风暴者]" },
            { "mechawoman_ct", "机甲女 [守卫者]" },
            { "mechawoman_t", "机甲女 [风暴者]" },
            { "man", "标准男角色" },
            { "woman", "标准女角色" },
            { "police", "标准守卫者" },
            { "type2", "标准风暴者" },
            { "baofeng", "暴风 [守卫者]" },
            { "baofengwoman", "暴风女 [守卫者]" },
            { "sas", "特种部队" },
            { "saswoman", "特种部队女" },
            { "haiying", "海鹰" },
            { "haiyingwoman", "海鹰女" },
            { "feihu", "飞虎" },
            { "feihuwoman", "飞虎女" },
            { "meat", "巨石肉盾" },
            { "meatwoman", "女巨石" },
            { "sanjiaozhou", "三角洲" },
            { "sanjiaozhouwoman", "三角洲女" },
            { "dajieda", "大姐大" },
            { "luoli", "萌系萝莉" },
            { "heihongluoli", "黑红萝莉" },
            { "lanbainvpu", "蓝白女仆" },
            { "tongyun", "彤云" },
            { "aisha", "艾莎" },
            { "attendant", "兔女郎" },
            { "schoolgirls", "女子高中生" },
            { "cheerleader", "啦啦队员" },
            { "messiah", "弥赛亚" },
            { "yueyong", "月咏" },
            { "yunli", "云丽" },
            { "angel", "安琪儿" },
            { "tong", "瞳" },
            { "cui", "萃" },
            { "diaochan", "貂蝉" },
            { "zhaoyun", "赵云" },
            { "lvbu", "吕布" },
            { "sunshangxiang", "孙尚香" },
            { "zhangfei", "张飞" },
            { "zhenji", "甄姬" },

            // 英雄角色
            { "hero", "大头英雄" },
            { "herowoman", "女英雄" },
            { "p3hero", "P3人类终极英雄" },
            { "hero_guanzi", "罐子英雄" },
            { "hero_shark", "深海狂鲨" },
            { "hero_sangzhong", "丧钟" },
            { "hero_pokong", "破空" },
            { "hero_lego", "乐高英雄" },
            { "hero_skyfire", "天火英雄" },
            { "kunlungongzhu", "昆仑公主" },

            // 变异 / 生化角色
            { "death", "变异体·终结者" },
            { "kingslayer", "变异体·弑王" },
            { "phantom", "变异体·幻影" },
            { "metalman", "变异体·铁人" },
            { "queen", "变异体·魅影女王" },
            { "cat", "变异体·萌猫" },
            { "pig", "变异体·萌猪" },
            { "chicken", "变异体·萌鸡" },
            { "xianhu", "变异体·仙狐" },
            { "pandoraMonther_baigu", "白骨精母体" },
            { "pandoraMonther", "潘多拉母体" },
            { "petrified", "石化恶魔" },
            { "petrified_kuilei", "石化傀儡" },
            { "nurse_jiuwei", "九尾妖狐" },
            { "death_xingtian", "上古刑天" },
            { "qingshe", "青蛇" },
            { "yuetu", "玉兔" },
            { "chansi", "缠丝妖母" },
            { "manwang", "蛮王" },
            { "shadow_demon", "暗影恶魔" },
            { "shadow_demon_tps", "暗影恶魔 (TPS)" },
            { "doun", "斗神" },
            { "doun_yecha", "夜叉斗神" },
            { "acg_thunder_girl", "雷电少女 (TPS)" },
            { "acg_shadow_girl", "暗影少女 (TPS)" },
            { "acg_zeus_soul", "宙斯之魂 (TPS)" }
        };

        // ─────────────────────────────────────────────
        // 3. 预置背饰中文词典
        // ─────────────────────────────────────────────
        private static readonly Dictionary<string, string> AccessoryFallbackMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "wing_zhandouopen", "战斗之翼 [展开]" },
            { "jetpack", "火箭喷气背包" },
            { "chibang1open", "炽天使之翼 [展翼]" },
            { "wing_fenghuangopen", "不死凤凰之翼" },
            { "wing_longopen", "太古魔龙之翼" },
            { "wing_guangyiopen", "极光之翼" },
            { "beishi_hudie", "梦幻蝶影背饰" },
            { "pifeng_mofa", "幻彩魔法披风" }
        };

        private static object GetWeaponInfoCacheInstance()
        {
            try
            {
                Type cacheType = ModelReplacer.FindType("Assets.Sources.CsvLoader.WeaponInfoCache");
                if (cacheType == null) return null;
                PropertyInfo instProp = cacheType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                                     ?? cacheType.BaseType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                return instProp?.GetValue(null);
            }
            catch { return null; }
        }

        /// <summary>
        /// 检查游戏内的 tables 配置表是否已加载完毕
        /// </summary>
        public static bool AreGameTablesLoaded()
        {
            try
            {
                object inst = GetWeaponInfoCacheInstance();
                if (inst != null)
                {
                    FieldInfo rowCachesField = inst.GetType().GetField("RowCaches", BindingFlags.Public | BindingFlags.Instance);
                    if (rowCachesField?.GetValue(inst) is IDictionary dict)
                    {
                        return dict.Count > 0;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// 解析武器中文名，优先从游戏 RowCaches 反射，其次从预置词典回退
        /// </summary>
        public static string ResolveWeaponName(string rawId)
        {
            if (string.IsNullOrEmpty(rawId)) return rawId;

            if (WeaponResolvedCache.TryGetValue(rawId, out string cached))
            {
                return cached;
            }

            // 1. 尝试从游戏 WeaponInfoCache 反射
            try
            {
                object inst = GetWeaponInfoCacheInstance();
                if (inst != null)
                {
                    FieldInfo rowCachesField = inst.GetType().GetField("RowCaches", BindingFlags.Public | BindingFlags.Instance);
                    if (rowCachesField?.GetValue(inst) is IDictionary dict && dict.Contains(rawId))
                    {
                        object row = dict[rawId];
                        if (row != null)
                        {
                            FieldInfo strNameField = row.GetType().GetField("StringName", BindingFlags.Public | BindingFlags.Instance);
                            string cn = strNameField?.GetValue(row) as string;
                            if (!string.IsNullOrWhiteSpace(cn))
                            {
                                string res = $"{cn.Trim()} ({rawId})";
                                WeaponResolvedCache[rawId] = res;
                                return res;
                            }
                        }
                    }
                }
            }
            catch { }

            // 2. 查预置词典
            if (WeaponFallbackMap.TryGetValue(rawId, out string fallbackCn))
            {
                string res = $"{fallbackCn} ({rawId})";
                WeaponResolvedCache[rawId] = res;
                return res;
            }

            // 3. 关键字模糊推导
            string guessed = TryGuessWeaponName(rawId);
            if (!string.IsNullOrEmpty(guessed))
            {
                string res = $"{guessed} ({rawId})";
                WeaponResolvedCache[rawId] = res;
                return res;
            }

            return rawId;
        }

        private static string TryGuessWeaponName(string id)
        {
            string lower = id.ToLowerInvariant();
            if (lower.Contains("ak47")) return "AK-47";
            if (lower.Contains("m4a1")) return "M4A1";
            if (lower.Contains("awp")) return "AWP";
            if (lower.Contains("barrett")) return "巴雷特";
            if (lower.Contains("deagle")) return "沙漠之鹰";
            if (lower.Contains("glock")) return "格洛克";
            if (lower.Contains("nepal")) return "尼泊尔";
            if (lower.Contains("knife")) return "军刀";
            if (lower.Contains("axe")) return "手斧";
            if (lower.Contains("shovel")) return "工兵铲";
            if (lower.Contains("gatling")) return "加特林";
            if (lower.Contains("bow")) return "战弓";
            if (lower.Contains("grenade")) return "手雷";
            return null;
        }

        /// <summary>
        /// 解析角色中文名，优先从 CareerConfigManager / PlayerCareerUtil 反射，其次从预置词典回退
        /// </summary>
        public static string ResolveCareerName(string rawId)
        {
            if (string.IsNullOrEmpty(rawId)) return rawId;

            if (CareerResolvedCache.TryGetValue(rawId, out string cached))
            {
                return cached;
            }

            // 1. 尝试从 PlayerCareerUtil.GetChineseName 反射官方配置
            try
            {
                Type utilType = ModelReplacer.FindType("share.constant.PlayerCareerUtil");
                if (utilType != null)
                {
                    MethodInfo getCnMethod = utilType.GetMethod("GetChineseName", BindingFlags.Public | BindingFlags.Static);
                    string cn = getCnMethod?.Invoke(null, new object[] { rawId }) as string;
                    if (!string.IsNullOrWhiteSpace(cn))
                    {
                        string res = $"{cn.Trim()} ({rawId})";
                        CareerResolvedCache[rawId] = res;
                        return res;
                    }
                }
            }
            catch { }

            // 1.2 尝试从 CareerConfigManager 反射
            try
            {
                Type mgrType = ModelReplacer.FindType("careerConfigPackage.CareerConfigManager");
                if (mgrType != null)
                {
                    MethodInfo getInstMethod = mgrType.GetMethod("GetInstance", BindingFlags.Public | BindingFlags.Static);
                    object inst = getInstMethod?.Invoke(null, null);
                    if (inst != null)
                    {
                        MethodInfo getConfigMethod = mgrType.GetMethod("GetConfig", new Type[] { typeof(string) });
                        object cfg = getConfigMethod?.Invoke(inst, new object[] { rawId });
                        if (cfg != null)
                        {
                            FieldInfo cnNameField = cfg.GetType().GetField("cnName", BindingFlags.Public | BindingFlags.Instance);
                            string cn = cnNameField?.GetValue(cfg) as string;
                            if (!string.IsNullOrWhiteSpace(cn))
                            {
                                string res = $"{cn.Trim()} ({rawId})";
                                CareerResolvedCache[rawId] = res;
                                return res;
                            }
                        }
                    }
                }
            }
            catch { }

            // 2. 查预置词典
            if (CareerFallbackMap.TryGetValue(rawId, out string fallbackCn))
            {
                string res = $"{fallbackCn} ({rawId})";
                CareerResolvedCache[rawId] = res;
                return res;
            }

            return rawId;
        }

        /// <summary>
        /// 解析背饰中文名
        /// </summary>
        public static string ResolveAccessoryName(string rawId)
        {
            if (string.IsNullOrEmpty(rawId)) return rawId;
            if (AccessoryFallbackMap.TryGetValue(rawId, out string fallbackCn))
            {
                return $"{fallbackCn} ({rawId})";
            }
            return rawId;
        }

        /// <summary>
        /// 从形如 "AK47-荣耀 (ak47_glory)" 提取出 "ak47_glory"
        /// </summary>
        public static string ExtractRawId(string displayName)
        {
            if (string.IsNullOrEmpty(displayName)) return "";
            int start = displayName.LastIndexOf('(');
            int end = displayName.LastIndexOf(')');
            if (start >= 0 && end > start)
            {
                return displayName.Substring(start + 1, end - start - 1).Trim();
            }
            return displayName.Trim();
        }

        public static void ClearCache()
        {
            WeaponResolvedCache.Clear();
            CareerResolvedCache.Clear();
        }
    }
}

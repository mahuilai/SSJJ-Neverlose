using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SingleSkinMod.Common;

namespace SingleSkinMod.Skin
{
    internal static class CatalogScanner
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        internal static List<string> Scan(params string[] typeNames)
        {
            var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string typeName in typeNames)
            {
                Type type = FindType(typeName);
                if (type == null) continue;

                foreach (FieldInfo field in type.GetFields(Flags))
                {
                    if (field.FieldType != typeof(string)) continue;
                    try
                    {
                        string val = field.GetValue(null) as string;
                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            values.Add(val.Trim());
                        }
                    }
                    catch { }
                }
            }
            var result = values.ToList();
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        private static Type FindType(string fullName)
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
    }
}

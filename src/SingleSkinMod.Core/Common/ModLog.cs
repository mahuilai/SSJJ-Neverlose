using System;
using UnityEngine;

namespace SingleSkinMod.Common
{
    internal static class ModLog
    {
        private const string Tag = "[SingleSkinMod] ";

        internal static void Info(string message)
        {
            Debug.Log(Tag + message);
        }

        internal static void Warn(string message)
        {
            Debug.LogWarning(Tag + message);
        }

        internal static void Error(string message)
        {
            Debug.LogError(Tag + message);
        }
    }
}

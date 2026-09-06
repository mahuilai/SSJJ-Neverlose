using System;
using System.Reflection;
using UnityEngine;
using SingleSkinMod.Common;

namespace SingleSkinMod.Visual
{
    public class MapColorController : MonoBehaviour
    {
        public static MapColorController Instance { get; private set; }

        public static bool Enabled { get; set; } = false;
        public static float CurrentR { get; set; } = 0.8f;
        public static float CurrentG { get; set; } = 0.4f;
        public static float CurrentB { get; set; } = 1.0f;
        public static float CurrentIntensity { get; set; } = 1.0f;
        public static float CurrentLerp { get; set; } = 1.0f;
        public static float CurrentLerp2 { get; set; } = 0.5f;

        private Material _postProcessMat;
        private Component _postProcessComp;
        private Type _postProcessType;
        private float _lastScanTime = 0f;

        private void Awake()
        {
            Instance = this;
        }

        private void Update()
        {
            if (!Enabled) return;

            if (Time.unscaledTime - _lastScanTime > 1.0f)
            {
                _lastScanTime = Time.unscaledTime;
                EnsurePostProcessMaterial();
            }

            ApplyCurrentTint();
        }

        public static void SetTint(float r, float g, float b, float intensity, float lerp, float lerp2)
        {
            CurrentR = Mathf.Clamp01(r);
            CurrentG = Mathf.Clamp01(g);
            CurrentB = Mathf.Clamp01(b);
            CurrentIntensity = Mathf.Clamp(intensity, 0f, 2f);
            CurrentLerp = Mathf.Clamp01(lerp);
            CurrentLerp2 = Mathf.Clamp01(lerp2);
            Enabled = true;

            Instance?.EnsurePostProcessMaterial();
            Instance?.ApplyCurrentTint();
        }

        public static void ApplyPreset(int preset)
        {
            switch (preset)
            {
                case 1: // 赛博紫夜
                    SetTint(0.75f, 0.35f, 1.0f, 1.0f, 0.85f, 0.6f);
                    break;
                case 2: // 暮光暖阳
                    SetTint(1.0f, 0.6f, 0.3f, 0.9f, 0.8f, 0.4f);
                    break;
                case 3: // 冰晶极地
                    SetTint(0.4f, 0.8f, 1.0f, 1.0f, 0.9f, 0.5f);
                    break;
                case 4: // 幽荧暗绿
                    SetTint(0.3f, 1.0f, 0.6f, 0.8f, 0.7f, 0.5f);
                    break;
                default: // 原版复位
                    ResetTint();
                    break;
            }
        }

        public static void ResetTint()
        {
            Enabled = false;
            CurrentR = 1f; CurrentG = 1f; CurrentB = 1f;
            CurrentIntensity = 1f; CurrentLerp = 0f; CurrentLerp2 = 0f;
            Instance?.ApplyColorDirect(Color.white, 0f, 0f);
        }

        private void EnsurePostProcessMaterial()
        {
            if (_postProcessMat != null) return;

            try
            {
                Camera cam = Camera.main;
                if (cam == null)
                {
                    var eyeObj = GameObject.Find("Eye");
                    if (eyeObj != null) cam = eyeObj.GetComponent<Camera>();
                }
                if (cam == null) return;

                if (_postProcessType == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        var t = asm.GetType("SSJJPostProcess");
                        if (t != null) { _postProcessType = t; break; }
                    }
                }

                if (_postProcessType != null)
                {
                    _postProcessComp = cam.GetComponent(_postProcessType);
                    if (_postProcessComp == null)
                    {
                        _postProcessComp = cam.gameObject.AddComponent(_postProcessType);
                    }

                    if (_postProcessComp != null)
                    {
                        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                        var f1 = _postProcessType.GetField("PostProcessMaterial", flags);
                        if (f1 != null) _postProcessMat = f1.GetValue(_postProcessComp) as Material;

                        if (_postProcessMat == null)
                        {
                            var p1 = _postProcessType.GetProperty("material", flags);
                            if (p1 != null) _postProcessMat = p1.GetValue(_postProcessComp, null) as Material;
                        }

                        if (_postProcessMat == null)
                        {
                            var f2 = _postProcessType.GetField("material", flags);
                            if (f2 != null) _postProcessMat = f2.GetValue(_postProcessComp) as Material;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Warn("EnsurePostProcessMaterial 异常: " + ex.Message);
            }
        }

        private void ApplyCurrentTint()
        {
            if (_postProcessMat == null) return;

            try
            {
                Color target = new Color(CurrentR, CurrentG, CurrentB, 1f);
                Color finalColor = Color.Lerp(Color.white, target, CurrentIntensity);

                if (_postProcessMat.HasProperty("_Color0"))
                    _postProcessMat.SetColor("_Color0", finalColor);
                else if (_postProcessMat.HasProperty("_Color"))
                    _postProcessMat.SetColor("_Color", finalColor);

                if (_postProcessMat.HasProperty("_lerp"))
                    _postProcessMat.SetFloat("_lerp", CurrentLerp);

                if (_postProcessMat.HasProperty("_lerp2"))
                    _postProcessMat.SetFloat("_lerp2", CurrentLerp2);
            }
            catch { }
        }

        private void ApplyColorDirect(Color c, float lerp, float lerp2)
        {
            if (_postProcessMat == null) return;
            try
            {
                if (_postProcessMat.HasProperty("_Color0"))
                    _postProcessMat.SetColor("_Color0", c);
                else if (_postProcessMat.HasProperty("_Color"))
                    _postProcessMat.SetColor("_Color", c);

                if (_postProcessMat.HasProperty("_lerp"))
                    _postProcessMat.SetFloat("_lerp", lerp);

                if (_postProcessMat.HasProperty("_lerp2"))
                    _postProcessMat.SetFloat("_lerp2", lerp2);
            }
            catch { }
        }
    }
}

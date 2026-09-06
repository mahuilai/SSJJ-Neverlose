using System;
using UnityEngine;
using SingleSkinMod.Common;
using SingleSkinMod.Config;

namespace SingleSkinMod.Visual
{
    public class WorldSnowController : MonoBehaviour
    {
        public static WorldSnowController Instance { get; private set; }

        private GameObject _snowObj;
        private ParticleSystem _ps;
        private ParticleSystem.EmissionModule _emission;
        private ParticleSystem.MainModule _main;
        private ParticleSystem.ShapeModule _shape;
        private Camera _lastCam;
        private float _nextCamCheck = 0f;

        public static ModConfig Config => SingleSkinMod.Plugin.ConfigInstance;

        private void Awake()
        {
            Instance = this;
        }

        private void Update()
        {
            if (Config == null) return;

            bool shouldRun = Config.WorldSnowEnabled;

            if (!shouldRun)
            {
                if (_snowObj != null && _snowObj.activeSelf)
                {
                    _snowObj.SetActive(false);
                }
                return;
            }

            if (Time.unscaledTime >= _nextCamCheck || _lastCam == null || !_lastCam.gameObject.activeInHierarchy)
            {
                _nextCamCheck = Time.unscaledTime + 1.0f;
                Camera cam = Camera.main;
                if (cam == null && Entity.PlayerUpdate.MainCamera != null)
                {
                    cam = Entity.PlayerUpdate.MainCamera;
                }

                if (cam != null && cam != _lastCam)
                {
                    _lastCam = cam;
                    SetupParticleSystem(cam);
                }
            }

            if (_snowObj != null && _lastCam != null)
            {
                if (!_snowObj.activeSelf)
                    _snowObj.SetActive(true);

                // 发射区域跟随摄像机位置，但粒子在世界空间下模拟
                _snowObj.transform.position = _lastCam.transform.position + new Vector3(0f, 4f, 0f);

                // 动态更新参数
                _main.startSpeed = Config.WorldSnowSpeed;
                _main.startSize = Config.WorldSnowSize;
                _emission.rateOverTime = Config.WorldSnowDensity;
            }
        }

        private void SetupParticleSystem(Camera cam)
        {
            if (_snowObj != null)
            {
                Destroy(_snowObj);
            }

            try
            {
                _snowObj = new GameObject("SSJJ_WorldSnow_Emitter");
                _snowObj.transform.position = cam.transform.position + new Vector3(0f, 4f, 0f);

                _ps = _snowObj.AddComponent<ParticleSystem>();
                var psr = _snowObj.GetComponent<ParticleSystemRenderer>();

                _main = _ps.main;
                _main.loop = true;
                _main.simulationSpace = ParticleSystemSimulationSpace.World; // 真实三维世界空间漂浮！
                _main.maxParticles = 1200;
                _main.startLifetime = 5.0f;
                _main.startSpeed = Config.WorldSnowSpeed > 0 ? Config.WorldSnowSpeed : 3.5f;
                _main.startSize = Config.WorldSnowSize > 0 ? Config.WorldSnowSize : 0.15f;
                _main.gravityModifier = 0.35f;

                // 粒子颜色：轻柔白色
                _main.startColor = new Color(0.95f, 0.98f, 1f, 0.85f);

                _emission = _ps.emission;
                _emission.enabled = true;
                _emission.rateOverTime = Config.WorldSnowDensity > 0 ? Config.WorldSnowDensity : 200f;

                _shape = _ps.shape;
                _shape.enabled = true;
                _shape.shapeType = ParticleSystemShapeType.Box;
                _shape.scale = new Vector3(30f, 12f, 30f);

                // 微风扰动 Noise
                var noise = _ps.noise;
                noise.enabled = true;
                noise.strength = 0.45f;
                noise.frequency = 0.25f;
                noise.scrollSpeed = 0.3f;

                // 材质：使用 Unity 内置 Particle Shader，避免依赖外部贴图
                Shader shader = Shader.Find("Particles/Standard Unlit")
                             ?? Shader.Find("Mobile/Particles/Alpha Blended")
                             ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended")
                             ?? Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    Material mat = new Material(shader);
                    mat.color = new Color(1f, 1f, 1f, 0.85f);
                    psr.material = mat;
                }

                _ps.Play();
                ModLog.Info("3D 世界空间粒子降雪系统已成功挂载到主摄像机！");
            }
            catch (Exception ex)
            {
                ModLog.Warn("挂载 3D 世界空间降雪异常: " + ex.Message);
            }
        }

        private void OnDestroy()
        {
            if (_snowObj != null)
            {
                Destroy(_snowObj);
                _snowObj = null;
            }
        }
    }
}

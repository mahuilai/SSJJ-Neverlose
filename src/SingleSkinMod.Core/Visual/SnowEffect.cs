using System;
using System.Collections.Generic;
using UnityEngine;

namespace SingleSkinMod.Visual
{
    public class SnowEffect : MonoBehaviour
    {
        public static SnowEffect Instance { get; private set; }

        private struct Snowflake
        {
            public Vector2 Position;
            public float Size;
            public float Speed;
            public float Drift;
        }

        private static List<Snowflake> _snowflakes = new List<Snowflake>();
        private static System.Random _rand = new System.Random();
        private static Texture2D _circleTexture = null;

        public static bool Enabled { get; set; } = false;
        public static int SnowCount { get; set; } = 80;
        public static float SnowSpeed { get; set; } = 60.0f;
        public static float SnowSize { get; set; } = 4.0f;

        private void Awake()
        {
            Instance = this;
        }

        private void Update()
        {
            if (!Enabled || SnowCount <= 0)
            {
                if (_snowflakes.Count > 0) _snowflakes.Clear();
                return;
            }

            int targetCount = Mathf.Clamp(SnowCount, 0, 200);
            while (_snowflakes.Count < targetCount)
            {
                _snowflakes.Add(new Snowflake
                {
                    Position = new Vector2(
                        _rand.Next(0, Mathf.Max(Screen.width, 800)),
                        _rand.Next(0, Mathf.Max(Screen.height, 600))
                    ),
                    Size = (float)(_rand.NextDouble() * 0.8 + 0.6) * SnowSize,
                    Speed = (float)(_rand.NextDouble() * 0.6 + 0.7) * SnowSpeed,
                    Drift = (float)(_rand.NextDouble() - 0.5) * 1.5f
                });
            }

            while (_snowflakes.Count > targetCount)
            {
                _snowflakes.RemoveAt(_snowflakes.Count - 1);
            }

            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < _snowflakes.Count; i++)
            {
                Snowflake sf = _snowflakes[i];
                float newX = sf.Position.x + sf.Drift * dt * 30.0f;
                float newY = sf.Position.y + sf.Speed * dt;

                if (newX < -sf.Size) newX = Screen.width + sf.Size;
                if (newX > Screen.width + sf.Size) newX = -sf.Size;

                if (newY > Screen.height + sf.Size)
                {
                    newY = -sf.Size;
                    newX = _rand.Next(0, Screen.width);
                }

                sf.Position = new Vector2(newX, newY);
                _snowflakes[i] = sf;
            }
        }

        private void OnGUI()
        {
            if (!Enabled || SnowCount <= 0 || _snowflakes.Count == 0) return;

            Texture2D tex = GetCircleTexture();
            Color prevColor = GUI.color;

            for (int i = 0; i < _snowflakes.Count; i++)
            {
                Snowflake sf = _snowflakes[i];
                GUI.color = new Color(1f, 1f, 1f, 0.75f);
                GUI.DrawTexture(new Rect(sf.Position.x, sf.Position.y, sf.Size, sf.Size), tex);
            }

            GUI.color = prevColor;
        }

        private static Texture2D GetCircleTexture()
        {
            if (_circleTexture != null) return _circleTexture;

            int size = 32;
            _circleTexture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            Color[] pixels = new Color[size * size];
            float center = size / 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(center, center));
                    float alpha = Mathf.Clamp01(1f - dist / center);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            _circleTexture.SetPixels(pixels);
            _circleTexture.Apply();
            _circleTexture.filterMode = FilterMode.Bilinear;
            _circleTexture.hideFlags = HideFlags.HideAndDontSave;
            return _circleTexture;
        }
    }
}

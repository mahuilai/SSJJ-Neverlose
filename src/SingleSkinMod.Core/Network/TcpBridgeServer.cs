using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using SingleSkinMod.Common;
using SingleSkinMod.Config;
using SingleSkinMod.Skin;
using SingleSkinMod.Hud;
using SingleSkinMod.Native;

namespace SingleSkinMod.Network
{
    public sealed class TcpBridgeServer : MonoBehaviour
    {
        private const int Port = 58888;
        private TcpListener _listener;
        private bool _running = true;
        private static readonly Queue<Action> MainThreadQueue = new Queue<Action>();

        private void Start()
        {
            Thread t = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "SingleSkinMod_TcpListener"
            };
            t.Start();
            ModLog.Info($"TCP 外部 WebView 控制台端口已开放: 127.0.0.1:{Port}");
        }

        private void Update()
        {
            lock (MainThreadQueue)
            {
                while (MainThreadQueue.Count > 0)
                {
                    try
                    {
                        MainThreadQueue.Dequeue()?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        ModLog.Error("主线程执行 TCP 调度任务异常: " + ex.Message);
                    }
                }
            }
        }

        private void OnDestroy()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
        }

        private void ListenLoop()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Parse("127.0.0.1"), Port);
                _listener.Start();

                while (_running)
                {
                    if (_listener.Pending())
                    {
                        using (TcpClient client = _listener.AcceptTcpClient())
                        using (NetworkStream stream = client.GetStream())
                        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                        using (StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                        {
                            string raw = reader.ReadLine();
                            if (!string.IsNullOrEmpty(raw))
                            {
                                string response = HandleMessage(raw.Trim());
                                if (!string.IsNullOrEmpty(response))
                                {
                                    writer.WriteLine(response);
                                }
                            }
                        }
                    }
                    Thread.Sleep(20);
                }
            }
            catch (Exception ex)
            {
                if (_running)
                {
                    ModLog.Error("TCP Socket 服务异常: " + ex.Message);
                }
            }
        }

        private string HandleMessage(string msg)
        {
            // 1. Check if JSON message
            if (msg.StartsWith("{") && msg.EndsWith("}"))
            {
                return HandleJsonMessage(msg);
            }

            // 2. Legacy string commands
            if (msg.Equals("SCAN_WEAPON", StringComparison.OrdinalIgnoreCase))
            {
                lock (MainThreadQueue)
                {
                    MainThreadQueue.Enqueue(() =>
                    {
                        var weapons = TextureReplacer.ScanWeapons();
                        ModLog.Info($"[SCAN_WEAPON] 扫描到 {weapons.Count} 个武器材质。");
                    });
                }
                return "{\"status\":\"ok\", \"msg\":\"SCAN_WEAPON dispatched\"}";
            }

            string[] p = msg.Split('|');
            if (p.Length >= 2)
            {
                if (p[0].Equals("MAP_INJECT", StringComparison.OrdinalIgnoreCase))
                {
                    string folder = p[1].Trim();
                    lock (MainThreadQueue)
                    {
                        MainThreadQueue.Enqueue(() =>
                        {
                            TextureReplacer.DoMapInject(folder);
                        });
                    }
                    return "{\"status\":\"ok\", \"msg\":\"MAP_INJECT dispatched\"}";
                }

                // keyword|filename.png
                string keyword = p[0].Trim();
                string fileName = p[1].Trim();
                lock (MainThreadQueue)
                {
                    MainThreadQueue.Enqueue(() =>
                    {
                        TextureReplacer.DoSwap(keyword, fileName);
                    });
                }
                return "{\"status\":\"ok\", \"msg\":\"DoSwap dispatched\"}";
            }

            return "{\"status\":\"error\", \"msg\":\"Unknown command format\"}";
        }

        private string HandleJsonMessage(string json)
        {
            var config = SingleSkinMod.Plugin.ConfigInstance;

            if (json.Contains("\"action\":\"GET_STATE\""))
            {
                return JsonUtility.ToJson(config);
            }

            if (json.Contains("\"action\":\"APPLY_ALL\""))
            {
                lock (MainThreadQueue)
                {
                    MainThreadQueue.Enqueue(() =>
                    {
                        ModelReplacer.ApplyAll(config);
                    });
                }
                return "{\"status\":\"ok\", \"msg\":\"ApplyAll dispatched\"}";
            }

            if (json.Contains("\"action\":\"RESTORE\""))
            {
                lock (MainThreadQueue)
                {
                    MainThreadQueue.Enqueue(() =>
                    {
                        ModelReplacer.RestoreOriginal();
                    });
                }
                return "{\"status\":\"ok\", \"msg\":\"Restore dispatched\"}";
            }

            if (json.Contains("\"action\":\"SET_MODEL\""))
            {
                try
                {
                    var req = JsonUtility.FromJson<ModConfig>(json);
                    if (req != null)
                    {
                        if (!string.IsNullOrEmpty(req.Weapon)) config.Weapon = req.Weapon;
                        if (!string.IsNullOrEmpty(req.Character)) config.Character = req.Character;
                        if (!string.IsNullOrEmpty(req.Accessory)) config.Accessory = req.Accessory;
                        if (req.Scale > 0.001f) config.Scale = req.Scale;
                        config.Save();

                        lock (MainThreadQueue)
                        {
                            MainThreadQueue.Enqueue(() =>
                            {
                                ModelReplacer.ApplyAll(config);
                            });
                        }
                        return "{\"status\":\"ok\", \"msg\":\"Model updated and applied\"}";
                    }
                }
                catch (Exception ex)
                {
                    return "{\"status\":\"error\", \"msg\":\"" + ex.Message + "\"}";
                }
            }

            if (json.Contains("\"action\":\"SET_SKYBOX\""))
            {
                try
                {
                    var req = JsonUtility.FromJson<ModConfig>(json);
                    if (req != null)
                    {
                        config.SkyboxEnabled = req.SkyboxEnabled;
                        config.SkyboxR = req.SkyboxR;
                        config.SkyboxG = req.SkyboxG;
                        config.SkyboxB = req.SkyboxB;
                        config.SkyboxIntensity = req.SkyboxIntensity;
                        config.SkyboxLerp = req.SkyboxLerp;
                        config.SkyboxLerp2 = req.SkyboxLerp2;
                        config.SnowEnabled = req.SnowEnabled;
                        config.SnowCount = req.SnowCount;
                        config.SnowSpeed = req.SnowSpeed;
                        config.SnowSize = req.SnowSize;
                        config.Save();

                        lock (MainThreadQueue)
                        {
                            MainThreadQueue.Enqueue(() =>
                            {
                                Visual.MapColorController.Enabled = config.SkyboxEnabled;
                                Visual.MapColorController.CurrentR = config.SkyboxR;
                                Visual.MapColorController.CurrentG = config.SkyboxG;
                                Visual.MapColorController.CurrentB = config.SkyboxB;
                                Visual.MapColorController.CurrentIntensity = config.SkyboxIntensity;
                                Visual.MapColorController.CurrentLerp = config.SkyboxLerp;
                                Visual.MapColorController.CurrentLerp2 = config.SkyboxLerp2;

                                Visual.SnowEffect.Enabled = config.SnowEnabled;
                                Visual.SnowEffect.SnowCount = config.SnowCount;
                                Visual.SnowEffect.SnowSpeed = config.SnowSpeed;
                                Visual.SnowEffect.SnowSize = config.SnowSize;

                                NativeBridge.SetSkyboxState(
                                    config.SkyboxEnabled, config.SkyboxR, config.SkyboxG, config.SkyboxB,
                                    config.SkyboxIntensity, config.SkyboxLerp, config.SkyboxLerp2,
                                    config.SnowEnabled ? config.SnowCount : 0,
                                    config.SnowSpeed, config.SnowSize, config.FilterPreset
                                );
                            });
                        }
                        return "{\"status\":\"ok\", \"msg\":\"Skybox and Snow updated\"}";
                    }
                }
                catch (Exception ex)
                {
                    return "{\"status\":\"error\", \"msg\":\"" + ex.Message + "\"}";
                }
            }

            return "{\"status\":\"ok\", \"received\":\"" + json.Replace("\"", "\\\"") + "\"}";
        }
    }
}

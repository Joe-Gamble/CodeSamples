using Gamble.BattleCards.Infrastructure;
using System.Threading;
using TMPro;
using UnityEngine;
using System.Linq;
using Gamble.BattleCards.Infrastructure.Platform;
using Unity.Profiling;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using OpenHardwareMonitor.Hardware;
#endif

namespace Gamble.BattleCards
{
    /// <summary>
    /// Class repsonsible for displaying and updating starts relating to system hardware
    /// </summary>
    public class UIPerformanceStats : MonoBehaviour
    {
        [SerializeField] private RectTransform container;

        // FPS
        [SerializeField] private TextMeshProUGUI fpsText;

        private float deltaTime = 0.0f;

        // Hardware
        [SerializeField] private TextMeshProUGUI gpuTempText;
        [SerializeField] private TextMeshProUGUI cpuTempText;
        [SerializeField] private TextMeshProUGUI vramLoadText;

        private int cpuTemp;
        private int gpuTemp;

        private Thread hardwareThread = null;
        private bool threadRunning = false;

        private string gpuVendor = string.Empty;

        private ProfilerRecorder vramRecorder;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private Computer computer;
        ISensor gpuSensor;
        ISensor cpuSensor;
#endif

        // latency
        [SerializeField] private TextMeshProUGUI pingText;

        private void OnEnable()
        {
            container.gameObject.SetActive(false);

            SingletonManager.NotifyOnInitialised(() =>
            {
                InitStats();
            });
        }

        void OnDestroy()
        {
            UnregisterCallbacks();

            StopHardwareThread();

            vramRecorder.Dispose();

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            computer.Close();
#endif
        }

        private void InitStats()
        {
            RegisterCallbacks();

            fpsText.transform.ToggleParent(SaveSystem.Instance.AccessibilitySettings.statsFPS.Value);
            gpuTempText.transform.ToggleParent(SaveSystem.Instance.AccessibilitySettings.statsGPU.Value);
            vramLoadText.transform.ToggleParent(SaveSystem.Instance.AccessibilitySettings.statsVRAM.Value);
            pingText.transform.ToggleParent(false);

            cpuTempText.transform.ToggleParent(SaveSystem.Instance.AccessibilitySettings.statsCPU.Value && PlatformManager.IsAdmin);

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            SetSensors();
#endif
            StartHardwareThread();

            container.gameObject.SetActive(true);
        }

        private void RegisterCallbacks()
        {
            SaveSystem.Instance.AccessibilitySettings.statsFPS.onValueChanged += OnFPSFlagChanged;
            SaveSystem.Instance.AccessibilitySettings.statsGPU.onValueChanged += OnGPUFlagChanged;
            SaveSystem.Instance.AccessibilitySettings.statsCPU.onValueChanged += OnCPUFlagChanged;
            SaveSystem.Instance.AccessibilitySettings.statsPing.onValueChanged += OnPingFlagChanged;
            SaveSystem.Instance.AccessibilitySettings.statsVRAM.onValueChanged += OnVRAMFlagChanged;


            NetworkManager.Instance.OnLocalClientConnectionChanged += OnNetworkStatusChanged;
        }

        private void UnregisterCallbacks()
        {
            SaveSystem.Instance.AccessibilitySettings.statsFPS.onValueChanged -= OnFPSFlagChanged;
            SaveSystem.Instance.AccessibilitySettings.statsGPU.onValueChanged -= OnGPUFlagChanged;
            SaveSystem.Instance.AccessibilitySettings.statsCPU.onValueChanged -= OnCPUFlagChanged;
            SaveSystem.Instance.AccessibilitySettings.statsPing.onValueChanged -= OnPingFlagChanged;
            SaveSystem.Instance.AccessibilitySettings.statsVRAM.onValueChanged -= OnVRAMFlagChanged;

            NetworkManager.Instance.OnLocalClientConnectionChanged -= OnNetworkStatusChanged;
        }

        private void OnFPSFlagChanged(bool enabled)
        {
            fpsText.transform.ToggleParent(enabled);
        }

        private void OnGPUFlagChanged(bool enabled)
        {
            gpuTempText.transform.ToggleParent(enabled);
        }

        private void OnCPUFlagChanged(bool enabled)
        {
            if (PlatformManager.Instance.Platform.IsRunningAsAdmin())
                cpuTempText.transform.ToggleParent(enabled);
        }

        private void OnPingFlagChanged(bool enabled)
        {
            if (enabled && NetworkManager.Instance.IsConnected)
            {
                pingText.transform.ToggleParent(true);
            }
            else
                pingText.transform.ToggleParent(false);
        }

        private void OnVRAMFlagChanged(bool enabled)
        {
            gpuTempText.transform.ToggleParent(enabled);
        }

        private void OnNetworkStatusChanged(bool connected)
        {
            if (SaveSystem.Instance.AccessibilitySettings.statsPing.Value)
            {
                pingText.transform.ToggleParent(connected);
            }
        }

        public void Update()
        {
            if (container.gameObject.activeInHierarchy)
            {
                SetFPS();

                SetCPUTemp();

                SetGPUTemp();

                SetLatency();

                SetVRANUsage();
            }
        }

        private void SetFPS()
        {
            if (fpsText.isActiveAndEnabled)
            {
                deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
                int fps = Mathf.RoundToInt(1.0f / deltaTime);
                fpsText.text = $"FPS: {fps}";
            }
        }

        private void SetCPUTemp()
        {
            if (cpuTempText.isActiveAndEnabled)
            {
                cpuTempText.text = $"CPU: {(cpuTemp >= 0 ? cpuTemp + "°C" : "N/A")}";
            }
        }

        private void SetGPUTemp()
        {
            if (gpuTempText.isActiveAndEnabled)
            {
                gpuTempText.text = $"GPU: {(gpuTemp >= 0 ? gpuTemp + "°C" : "N/A")}";
            }
        }

        private void SetLatency()
        {
            if (pingText.isActiveAndEnabled)
            {
                pingText.text = $"PING: {NetworkManager.Instance.CurrentPing}";
            }
        }

        private void SetVRANUsage()
        {
            if (vramLoadText.isActiveAndEnabled)
            {
                if (vramRecorder.Valid)
                {
                    long vramUsage = vramRecorder.LastValue / (1024 * 1024); // Convert bytes to MB
                    vramLoadText.text = $"VRAM Usage: {vramUsage} MB";
                }
            }
        }


        #region Hardware

        private void StartHardwareThread()
        {
            hardwareThread ??= new Thread(UpdateHardware)
            {
                IsBackground = true,
            };

            threadRunning = true;
            hardwareThread.Start();
        }

        private void StopHardwareThread()
        {
            threadRunning = false;
            if (hardwareThread != null && hardwareThread.IsAlive)
            {
                hardwareThread.Join();
            }
        }

        private void UpdateHardware()
        {
            if (threadRunning)
            {
                UpdateCPUTemperature();
                UpdateGPUTemperature();

                Thread.Sleep(1000);
            }
        }

        private void UpdateCPUTemperature()
        {
            cpuTemp = Mathf.RoundToInt(GetCPUTemperature());
        }

        private void UpdateGPUTemperature()
        {
            gpuTemp = Mathf.RoundToInt(GetGPUTemperature());
        }


        private float GetCPUTemperature()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (cpuSensor != null)
            {
                cpuSensor.Hardware.Update();
                return cpuSensor.Value.GetValueOrDefault(-1);
            }
#elif UNITY_LINUX || UNITY_EDITOR_LINUX
            return GetLinuxCPUTemperature();
#elif UNITY_OSX || UNITY_EDITOR_LINUX
            return GetMacOSCPUTemperature();
#else
            LOG.LogWarning("Unsupported platform for CPU temperature retrieval.", LOG.Type.SYSTEM);
#endif
            return -1;
        }

        private float GetGPUTemperature()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (gpuSensor != null)
            {
                gpuSensor.Hardware.Update();
                return gpuSensor.Value.GetValueOrDefault(-1);
            }
#else
            if (gpuVendor.Contains("NVIDIA"))
                return GetNvidiaTemperature();

            if (gpuVendor.Contains("AMD") || gpuVendor.Contains("Radeon"))
                return GetAMDTemperature();
#endif
            return -1;
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private void SetSensors()
        {
            computer ??= new Computer()
            {
                CPUEnabled = true,
                GPUEnabled = true,
                RAMEnabled = true,
                MainboardEnabled = false
            };

            computer.Open();

            IHardware gpuHardware = computer.Hardware.FirstOrDefault(x => x.HardwareType == HardwareType.GpuNvidia || x.HardwareType == HardwareType.GpuAti/* || x.HardwareType == HardwareType.GpuIntel*/);
            IHardware cpuHardware = computer.Hardware.FirstOrDefault(x => x.HardwareType == HardwareType.CPU);
            IHardware ramHardware = computer.Hardware.FirstOrDefault(x => x.HardwareType == HardwareType.RAM);

            gpuSensor = gpuHardware?.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Temperature);
            cpuSensor = cpuHardware?.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Temperature);

            vramRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GfxDriver Memory");
        }
#else
        private float GetNvidiaTemperature()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/C nvidia-smi --query-gpu=temperature.gpu --format=csv,noheader",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using Process process = Process.Start(psi);
                string output = process.StandardOutput.ReadToEnd();
                if (int.TryParse(output.Trim(), out int temp))
                    return temp;
            }
            catch (Exception e)
            {
                LOG.LogError($"Error getting NVIDIA GPU temperature: {e.Message}", LOG.Type.SYSTEM);
            }

            return -1;
        }

        private float GetAMDTemperature()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/C wmic /namespace:\\\\root\\wmi PATH MSAcpi_ThermalZoneTemperature GET CurrentTemperature",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using Process process = Process.Start(psi);
                string output = process.StandardOutput.ReadToEnd();
                string[] lines = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                if (lines.Length > 1 && int.TryParse(lines[1].Trim(), out int rawTemp))
                    return (rawTemp / 10f) - 273.15f; // Convert to Celsius
            }
            catch (Exception e)
            {
                LOG.LogError("Error getting AMD GPU temperature: " + e.Message, LOG.Type.SYSTEM);
            }

            return -1;
        }

#if UNITY_LINUX
    float GetLinuxCPUTemperature()
    {
        try
        {
            string tempFilePath = "/sys/class/thermal/thermal_zone0/temp";

            if (File.Exists(tempFilePath))
            {
                string tempString = File.ReadAllText(tempFilePath).Trim();
                if (int.TryParse(tempString, out int temp))
                {
                    // Convert to Celsius
                    return temp / 1000f;
                }
            }
        }
        catch (Exception e)
        {
            LOG.LogError($"Error getting Linux CPU temperature: {e.Message}", LOG.Type.SYSTEM);
        }

        return -1;
    }
#endif

#if UNITY_OSX
    float GetMacOSCPUTemperature()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "sudo",
                    Arguments = "powermetrics --samplers smc",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using Process process = Process.Start(psi);
                string output = process.StandardOutput.ReadToEnd();
                int tempIndex = output.IndexOf("CPU die temperature") + 20;
                if (tempIndex != -1)
                {
                    string tempStr = output.Substring(tempIndex, 4).Trim();
                    if (float.TryParse(tempStr, out float cpuTemp))
                    {
                        return cpuTemp;
                    }
                }
            }
            catch (Exception e)
            {
                LOG.LogError($"Error getting macOS CPU temperature: {e.Message}", LOG.Type.SYSTEM);
            }

            return -1;
        }
#endif
        
#endif
        #endregion
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;
namespace PChabit.HardwareMonitor.Hardware
{
    /// <summary>
    /// 电池服务：专门处理电池状态识别、数值计算及模拟测试逻辑
    /// </summary>
    public static class BatteryService
    {
        /// <summary>
        /// 获取电池相关数值
        /// </summary>
        public static float? GetBatteryValue(string key, Dictionary<string, ISensor> sensorCache)
        {
            // 1. 模拟模式逻辑 (用于 UI 测试)
            bool simulateBattery = false; // 默认关闭，可根据需要开启
            if (simulateBattery)
            {
                return GetSimulatedValue(key);
            }

            // 2. 真实硬件逻辑
            if (sensorCache.TryGetValue(key, out var sensor) && sensor.Value.HasValue)
            {
                float val = sensor.Value.Value;

                // 符号修正：充电时功耗/电流应为正号，放电时为负号
                if (key == "BAT.Power" || key == "BAT.Current")
                {
                    // 原实现走 MetricUtils.GetPowerStatus()（WinForms SystemInformation），
                    // PChabit v1 改为 P/Invoke GetSystemPowerStatus，避免引入 WinForms 依赖。
                    bool acOnline = IsAcOnline();

                    // [Fix] 简化且强制的符号修正逻辑
                    // 无论传感器原始值是正还是负（不同驱动标准不一），
                    // 我们只根据"是否插电"来强制赋予符号。

                    if (acOnline)
                    {
                        // 插电状态 (AcOnline=true) -> 视为输入/充电 -> 正数
                        val = Math.Abs(val);
                    }
                    else
                    {
                        // 电池供电状态 (AcOnline=false) -> 视为输出/放电 -> 负数
                        // ★ 强制取负绝对值，确保一定是负数
                        val = -Math.Abs(val);
                        

                    }
                }
                return val;
            }

            return null;
        }

        // =========================================================
        // P/Invoke 电源状态（替代上游 MetricUtils.GetPowerStatus）
        // =========================================================
        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", EntryPoint = "GetSystemPowerStatus")]
        private static extern bool GetSystemPowerStatusNative(out SYSTEM_POWER_STATUS sps);

        private static bool IsAcOnline()
        {
            try
            {
                if (GetSystemPowerStatusNative(out var sps))
                {
                    return sps.ACLineStatus == 1; // 1 = 在线（插电）
                }
            }
            catch { }
            return true; // 默认视为插电，避免符号逻辑异常
        }

        private static float? GetSimulatedValue(string key)
        {
            var now = DateTime.Now;
            int sec = now.Second;

            // 前 30 秒：模拟 [高负载放电]，后 30 秒：模拟 [快充]
            bool isCharging = sec >= 30;
            
            float voltage = isCharging 
                ? 15.5f + ((sec - 30) * 0.05f) 
                : 16.8f - (sec * 0.06f);

            float power = isCharging ? -65.0f - (sec % 5) * 4.0f : 25.0f + (sec % 3) * 5.0f;
            float current = power / voltage;
            float percent = isCharging ? (sec - 30) * (100.0f / 30.0f) : 100.0f - (sec * (100.0f / 30.0f));

            return key switch
            {
                "BAT.Percent" => Math.Clamp(percent, 0f, 100f),
                "BAT.Power" => power,
                "BAT.Voltage" => voltage,
                "BAT.Current" => current,
                _ => null
            };
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace WuPause
{
    /// <summary>
    /// Windows 更新暂停态的注册表读写与备份。
    ///
    /// 机制说明（基于 25H2 / build 26200 实测 + 微软文档 + 社区验证）：
    ///
    /// 暂停不是单一开关，而是三组键协同：
    ///   1) UX\Settings\FlightSettingsMaxPauseDays   —— UI 下拉框的最大可选天数（DWORD）
    ///   2) UX\Settings\Pause*StartTime / Pause*EndTime —— 暂停窗口的起止时刻（REG_SZ, ISO-8601 UTC）
    ///   3) UpdatePolicy\Settings\Paused*Status        —— 状态位，1 = 处于暂停
    ///
    /// 关键约束（NTLite 社区实测）：StartTime 到 EndTime 的跨度
    /// 不得超过 FlightSettingsMaxPauseDays。只改上限而不写时间戳，
    /// 不会让系统进入暂停态 —— 这正是旧版 WindowsUpdatePause.exe 失效的原因。
    /// </summary>
    internal static class WuRegistry
    {
        private const string UX_SETTINGS   = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
        private const string POLICY        = @"SOFTWARE\Microsoft\WindowsUpdate\UpdatePolicy\Settings";
        private const string WU_POLICY     = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";

        // 上限值。微软策略引擎 highrange=35，但 UI 层用的是这个 UX 值，社区实测可超。
        // 这里给一个足够大的值，真正的暂停长度由用户输入的天数决定。
        private const int MAX_PAUSE_DAYS_CAP = 36500;

        private static readonly string[] TimeStampKeys =
        {
            "PauseUpdatesStartTime",
            "PauseUpdatesExpiryTime",
            "PauseFeatureUpdatesStartTime",
            "PauseFeatureUpdatesEndTime",
            "PauseQualityUpdatesStartTime",
            "PauseQualityUpdatesEndTime",
        };

        /// <summary>把 DateTime 转成 Windows 期望的 ISO-8601 UTC 字符串。</summary>
        public static string ToWuTime(DateTime utc)
        {
            return utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------
        // 读取
        // ---------------------------------------------------------------

        public static int? ReadMaxPauseDays()
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(UX_SETTINGS))
            {
                if (k == null) return null;
                object v = k.GetValue("FlightSettingsMaxPauseDays");
                return v is int i ? i : (int?)null;
            }
        }

        public static int? ReadStatus(string name)
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(POLICY))
            {
                if (k == null) return null;
                object v = k.GetValue(name);
                return v is int i ? i : (int?)null;
            }
        }

        public static string ReadTimestamp(string name)
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(UX_SETTINGS))
            {
                if (k == null) return null;
                return k.GetValue(name) as string;
            }
        }

        /// <summary>
        /// 读出当前暂停到期时刻。优先 PauseUpdatesExpiryTime，
        /// 缺失时退而用 PauseFeatureUpdatesEndTime。
        /// </summary>
        public static DateTime? ReadExpiryUtc()
        {
            string[] order = { "PauseUpdatesExpiryTime", "PauseFeatureUpdatesEndTime", "PauseQualityUpdatesEndTime" };
            foreach (string name in order)
            {
                string s = ReadTimestamp(name);
                DateTime dt;
                if (!string.IsNullOrEmpty(s) && TryParseWuTime(s, out dt))
                    return dt;
            }
            return null;
        }

        public static bool TryParseWuTime(string s, out DateTime utc)
        {
            utc = default;
            if (string.IsNullOrWhiteSpace(s)) return false;
            string[] formats =
            {
                "yyyy-MM-ddTHH:mm:ssZ",
                "yyyy-MM-ddTHH:mm:ss.fffZ",
                "yyyy-MM-ddTHH:mm:sszzz",
            };
            return DateTime.TryParseExact(s.Trim(), formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out utc);
        }

        // ---------------------------------------------------------------
        // 写入
        // ---------------------------------------------------------------

        /// <summary>
        /// 构造完整的暂停态。
        /// </summary>
        /// <param name="days">暂停天数上限（用户输入）。</param>
        /// <returns>实际写入的到期时刻（UTC）。</returns>
        public static DateTime ApplyPause(int days)
        {
            if (days < 1) days = 1;
            if (days > MAX_PAUSE_DAYS_CAP) days = MAX_PAUSE_DAYS_CAP;

            DateTime startUtc = DateTime.UtcNow;
            DateTime endUtc = startUtc.AddDays(days);

            // 上限值必须严格大于窗口跨度，否则 WU 会判定暂停无效。
            // 注意不能对 cap 再做上限钳制：days 已钳到 MAX_PAUSE_DAYS_CAP，
            // 若 cap 也钳到同一值，在 days == 上限时会出现 span == cap 的边界，
            // 浮点比较下 span <= cap 不成立，暂停直接失效。
            int cap = days + 1;

            using (RegistryKey k = Registry.LocalMachine.CreateSubKey(UX_SETTINGS))
            {
                k.SetValue("FlightSettingsMaxPauseDays", cap, RegistryValueKind.DWord);
                k.SetValue("PauseUpdatesStartTime",      ToWuTime(startUtc), RegistryValueKind.String);
                k.SetValue("PauseUpdatesExpiryTime",     ToWuTime(endUtc),   RegistryValueKind.String);
                k.SetValue("PauseFeatureUpdatesStartTime", ToWuTime(startUtc), RegistryValueKind.String);
                k.SetValue("PauseFeatureUpdatesEndTime",   ToWuTime(endUtc),   RegistryValueKind.String);
                k.SetValue("PauseQualityUpdatesStartTime", ToWuTime(startUtc), RegistryValueKind.String);
                k.SetValue("PauseQualityUpdatesEndTime",   ToWuTime(endUtc),   RegistryValueKind.String);
            }

            using (RegistryKey k = Registry.LocalMachine.CreateSubKey(POLICY))
            {
                k.SetValue("PausedFeatureStatus", 1, RegistryValueKind.DWord);
                k.SetValue("PausedQualityStatus", 1, RegistryValueKind.DWord);
            }

            // 组策略侧的天花板同步抬高，否则策略引擎会把 UI 拉回 35 天。
            WritePolicyCap(cap);

            return endUtc;
        }

        /// <summary>
        /// 同步策略层上限。PolicyManager 里 SetMaxPauseDays 的 highrange=35，
        /// 这里写入的值若被 clamp 也无妨——真正生效的是上面的 UX 时间戳，
        /// 这一步只是尽力让下拉框也放开。
        /// </summary>
        private static void WritePolicyCap(int days)
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.CreateSubKey(WU_POLICY))
                {
                    k.SetValue("SetMaxPauseDays", days, RegistryValueKind.DWord);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // 策略键不可写不影响主功能，静默忽略
            }
            catch (IOException)
            {
            }
        }

        /// <summary>清除暂停态，恢复更新。</summary>
        public static void ResumeNow()
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(UX_SETTINGS, true))
            {
                if (k != null)
                    foreach (string name in TimeStampKeys)
                        k.DeleteValue(name, false);
            }

            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(POLICY, true))
            {
                if (k != null)
                {
                    k.DeleteValue("PausedFeatureStatus", false);
                    k.DeleteValue("PausedQualityStatus", false);
                }
            }
        }

        // ---------------------------------------------------------------
        // 备份 / 恢复
        // ---------------------------------------------------------------

        private sealed class Snapshot
        {
            public string KeyPath;
            public Dictionary<string, object[]> Values = new Dictionary<string, object[]>();
            public List<string> SubKeys = new List<string>();
        }

        /// <summary>
        /// 备份相关注册表键到 .reg 文件（UTF-16 LE，标准 regedit 格式）。
        /// 包含被删除的键记录，以便完整还原。
        /// </summary>
        public static string Backup(string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Windows Registry Editor Version 5.00");
            sb.AppendLine("; 由 WuPause 自动备份 —— 修改前请勿删除");
            sb.AppendLine("; 恢复方法：双击本文件，或 reg import 本文件");
            sb.AppendLine();

            foreach (string path in new[] { UX_SETTINGS, POLICY, WU_POLICY })
            {
                Snapshot s = TakeSnapshot(path);
                sb.AppendLine(SnapshotToReg(s));
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.Unicode);
            return filePath;
        }

        private static Snapshot TakeSnapshot(string path)
        {
            var s = new Snapshot { KeyPath = path };
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(path))
            {
                if (k == null) return s;
                foreach (string vn in k.GetValueNames())
                    s.Values[vn] = new[] { k.GetValue(vn), (object)k.GetValueKind(vn) };
                foreach (string sn in k.GetSubKeyNames())
                    s.SubKeys.Add(sn);
            }
            return s;
        }

        private static string SnapshotToReg(Snapshot s)
        {
            var sb = new StringBuilder();
            string root = "HKEY_LOCAL_MACHINE\\";
            string sub = s.KeyPath.Replace('/', '\\');

            sb.AppendLine($"[{root}{sub}]");
            if (s.Values.Count == 0 && s.SubKeys.Count == 0)
            {
                // 空键也要留痕，方便恢复时删除误建的键
                sb.AppendLine("\"__WuPause_empty__\"=\"\"");
            }
            foreach (var kv in s.Values)
            {
                sb.AppendLine($"\"{Escape(kv.Key)}\"={FormatValue(kv.Value[0], (RegistryValueKind)kv.Value[1])}");
            }
            foreach (string sk in s.SubKeys)
                sb.AppendLine($"[{root}{sub}\\{sk}]");
            sb.AppendLine();
            return sb.ToString();
        }

        private static string Escape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string FormatValue(object v, RegistryValueKind kind)
        {
            if (v == null) return "\"\"";
            switch (kind)
            {
                case RegistryValueKind.DWord:
                    return $"dword:{Convert.ToUInt32(v):X8}";
                case RegistryValueKind.QWord:
                    return $"qword:{Convert.ToUInt64(v):X16}";
                case RegistryValueKind.Binary:
                    {
                        byte[] b = (byte[])v;
                        return "hex(2):" + string.Join(",", Array.ConvertAll(b, x => x.ToString("X2")));
                    }
                default:
                    return "\"" + Escape(Convert.ToString(v, CultureInfo.InvariantCulture)) + "\"";
            }
        }

        /// <summary>从备份文件读回并逐项写回注册表。</summary>
        public static void Restore(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("备份文件不存在", filePath);

            // 导出为 reg 格式后交给 reg.exe 还原，最稳妥
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "reg.exe",
                Arguments = $"import \"{filePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi))
            {
                p.WaitForExit();
                if (p.ExitCode != 0)
                    throw new InvalidOperationException("reg import 失败: " + p.StandardError.ReadToEnd());
            }
        }
    }
}
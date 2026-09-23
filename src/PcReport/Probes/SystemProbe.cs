using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microforge.PcReport.Model;

namespace Microforge.PcReport.Probes
{
    /// <summary>
    /// 収集層。WMI・レジストリ・COM を叩いて「生値」を集めるだけ。
    ///
    /// ここでやらないこと（すべて判定層 Analysis の仕事）:
    ///   - しきい値の判定、productState の解釈、日付の解釈、表示文言の選択
    ///   - DateTime.Now の参照（now は判定層に注入される）
    ///
    /// ここで守ること（SPEC §3 設計原則 1）:
    ///   - ユーザー名・PC 名・IP アドレス・ディスク/BIOS シリアルを一切取得しない
    /// </summary>
    public static class SystemProbe
    {
        const string CvPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

        /// <summary>実機から 1 回だけ収集する。どの項目が落ちても他は止めない。</summary>
        public static SystemSnapshot Capture()
        {
            var s = new SystemSnapshot();
            s.RanAsAdmin = IsElevatedAdministrator();

            s.Os = ProbeOs();
            s.LastBootUpTime = ProbeLastBoot();
            s.Updates = ProbeUpdates();
            s.AntiVirus = ProbeAntiVirus();
            s.Defender = ProbeDefender();
            s.Cpu = ProbeCpu();
            s.Memory = ProbeMemory();
            s.LogicalDisks = ProbeLogicalDisks();
            s.PhysicalDisks = ProbePhysicalDisks();
            s.BootDisk = PickBootDisk(s.PhysicalDisks);
            s.Reliability = ProbeReliability();
            s.Tpm = ProbeTpm();
            s.SecureBoot = ProbeSecureBoot();
            s.UpgradeIndicators = ProbeUpgradeIndicators();

            return s;
        }

        static bool IsElevatedAdministrator()
        {
            try
            {
                using (var id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        // ---- 例外 → ProbeResult の共通処理 ---------------------------------

        static ProbeResult<T> Run<T>(string source, Func<T> f) where T : class
        {
            try
            {
                var v = f();
                if (v == null) return ProbeResult<T>.Empty("情報源に値がありませんでした", source);
                return ProbeResult<T>.Ok(v, source);
            }
            catch (Exception e)
            {
                return WmiGateway.IsAccessDenied(e)
                    ? ProbeResult<T>.Denied(source)
                    : ProbeResult<T>.Failed(WmiGateway.Explain(e), source);
            }
        }

        static ProbeResult<List<T>> RunList<T>(string source, Func<List<T>> f)
        {
            try
            {
                var v = f();
                if (v == null || v.Count == 0)
                    return ProbeResult<List<T>>.Empty("情報源に値がありませんでした", source);
                return ProbeResult<List<T>>.Ok(v, source);
            }
            catch (Exception e)
            {
                return WmiGateway.IsAccessDenied(e)
                    ? ProbeResult<List<T>>.Denied(source)
                    : ProbeResult<List<T>>.Failed(WmiGateway.Explain(e), source);
            }
        }

        static ProbeResult<DateTime> RunDate(string source, Func<DateTime?> f)
        {
            try
            {
                var v = f();
                if (!v.HasValue) return ProbeResult<DateTime>.Empty("情報源に値がありませんでした", source);
                return new ProbeResult<DateTime> { Outcome = ProbeOutcome.Ok, Value = v.Value, Source = source };
            }
            catch (Exception e)
            {
                return WmiGateway.IsAccessDenied(e)
                    ? ProbeResult<DateTime>.Denied(source)
                    : ProbeResult<DateTime>.Failed(WmiGateway.Explain(e), source);
            }
        }

        // ---- OS ----------------------------------------------------------

        static ProbeResult<OsInfo> ProbeOs()
        {
            const string src = "Win32_OperatingSystem / レジストリ CurrentVersion";
            return Run(src, () =>
            {
                var os = WmiGateway.First(WmiGateway.Cimv2,
                    "SELECT Caption, OSArchitecture FROM Win32_OperatingSystem");

                // Caption は「Microsoft Windows 11 Pro」等。
                // ProductName は Windows 11 でも "Windows 10" と出るので使わない。
                var caption = WmiGateway.Str(os, "Caption");
                if (caption != null) caption = caption.Replace("Microsoft ", "");

                return new OsInfo
                {
                    Caption = caption,
                    DisplayVersion = WmiGateway.RegStr(CvPath, "DisplayVersion")
                                     ?? WmiGateway.RegStr(CvPath, "ReleaseId"),
                    Ubr = WmiGateway.RegInt(CvPath, "UBR"),
                    Architecture = WmiGateway.Str(os, "OSArchitecture"),
                    Build = ReadBuildNumber()
                };
            });
        }

        static int ReadBuildNumber()
        {
            var s = WmiGateway.RegStr(CvPath, "CurrentBuildNumber");
            int n;
            if (s != null && int.TryParse(s, out n)) return n;
            return Environment.OSVersion.Version.Build;
        }

        static ProbeResult<DateTime> ProbeLastBoot()
        {
            const string src = "Win32_OperatingSystem.LastBootUpTime";
            return RunDate(src, () =>
                WmiGateway.Date(
                    WmiGateway.First(WmiGateway.Cimv2, "SELECT LastBootUpTime FROM Win32_OperatingSystem"),
                    "LastBootUpTime"));
        }

        // ---- 更新 --------------------------------------------------------

        static readonly Regex KbPattern = new Regex(@"KB\d{6,}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// QFE と WUA 履歴を「混ぜずに別々に」集める。
        /// どちらを採るか・何を除くかは判定層が決める（SPEC §7-1 ②：WUA には Store アプリの更新が混ざる）。
        /// </summary>
        static ProbeResult<UpdateSources> ProbeUpdates()
        {
            const string src = "Win32_QuickFixEngineering / Windows Update の履歴 (Microsoft.Update.Session)";
            var u = new UpdateSources();

            try
            {
                foreach (var q in WmiGateway.Query(WmiGateway.Cimv2,
                             "SELECT HotFixID, InstalledOn FROM Win32_QuickFixEngineering"))
                {
                    var raw = WmiGateway.Str(q, "InstalledOn");
                    if (string.IsNullOrEmpty(raw)) continue;

                    DateTime d;
                    // InstalledOn は "9/21/2026" 形式だがロケールで揺れる。両方試して、駄目なものは飛ばす
                    if (!DateTime.TryParse(raw, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out d)
                        && !DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.None, out d))
                        continue;

                    var id = WmiGateway.Str(q, "HotFixID");
                    u.Qfe.Add(new UpdateEntry { Date = d, Title = id, KbId = ExtractKb(id) });
                }
                if (u.Qfe.Count == 0) u.QfeReason = "更新の記録が見つかりませんでした";
            }
            catch (Exception e) { u.QfeReason = WmiGateway.Explain(e); }

            try
            {
                var t = Type.GetTypeFromProgID("Microsoft.Update.Session");
                if (t == null)
                {
                    u.WuaReason = "この Windows には該当する情報源がありません";
                }
                else
                {
                    dynamic session = Activator.CreateInstance(t);
                    dynamic searcher = session.CreateUpdateSearcher();
                    int total = searcher.GetTotalHistoryCount();
                    if (total <= 0)
                    {
                        u.WuaReason = "更新の記録が見つかりませんでした";
                    }
                    else
                    {
                        dynamic hist = searcher.QueryHistory(0, Math.Min(total, 200));
                        foreach (dynamic e in hist)
                        {
                            // Operation 1 = インストール、ResultCode 2 = 成功。それ以外は「入った」ではない
                            if ((int)e.Operation != 1 || (int)e.ResultCode != 2) continue;
                            DateTime d = ((DateTime)e.Date).ToLocalTime();
                            string title = e.Title as string;
                            u.Wua.Add(new UpdateEntry { Date = d, Title = title, KbId = ExtractKb(title) });
                        }
                        if (u.Wua.Count == 0) u.WuaReason = "成功した更新の記録が見つかりませんでした";
                    }
                }
            }
            catch (Exception e)
            {
                u.WuaReason = WmiGateway.Explain(e);
            }

            // 両方落ちたときだけ「取れなかった」。片方でも材料があれば Ok で渡し、判定層に選ばせる
            if (u.Qfe.Count == 0 && u.Wua.Count == 0)
            {
                var reasons = new List<string>();
                if (!string.IsNullOrEmpty(u.QfeReason)) reasons.Add("インストール済み更新: " + u.QfeReason);
                if (!string.IsNullOrEmpty(u.WuaReason)) reasons.Add("更新の履歴: " + u.WuaReason);
                return ProbeResult<UpdateSources>.Empty(string.Join(" / ", reasons), src);
            }
            return ProbeResult<UpdateSources>.Ok(u, src);
        }

        static string ExtractKb(string title)
        {
            if (string.IsNullOrEmpty(title)) return null;
            var m = KbPattern.Match(title);
            return m.Success ? m.Value.ToUpperInvariant() : null;
        }

        // ---- ウイルス対策 --------------------------------------------------

        static ProbeResult<List<AntiVirusProduct>> ProbeAntiVirus()
        {
            const string src = @"root\SecurityCenter2 AntiVirusProduct";
            return RunList(src, () =>
            {
                var list = new List<AntiVirusProduct>();
                foreach (var av in WmiGateway.Query(WmiGateway.SecurityCenter2,
                             "SELECT displayName, productState FROM AntiVirusProduct"))
                {
                    var name = WmiGateway.Str(av, "displayName");
                    if (name == null) continue;
                    uint state;
                    try { state = Convert.ToUInt32(av["productState"]); } catch { continue; }
                    list.Add(new AntiVirusProduct { DisplayName = name, ProductState = state });
                }
                return list;
            });
        }

        static ProbeResult<DefenderStatus> ProbeDefender()
        {
            const string src = @"root\Microsoft\Windows\Defender MSFT_MpComputerStatus";
            return Run(src, () =>
            {
                var m = WmiGateway.First(WmiGateway.DefenderNs,
                    "SELECT AntivirusSignatureLastUpdated, AntivirusSignatureVersion, AntivirusEnabled, "
                    + "RealTimeProtectionEnabled FROM MSFT_MpComputerStatus");
                if (m == null) return null;
                return new DefenderStatus
                {
                    SignatureLastUpdated = WmiGateway.Date(m, "AntivirusSignatureLastUpdated"),
                    SignatureVersion = WmiGateway.Str(m, "AntivirusSignatureVersion"),
                    AntivirusEnabled = WmiGateway.Bool(m, "AntivirusEnabled", false),
                    RealTimeProtectionEnabled = WmiGateway.Bool(m, "RealTimeProtectionEnabled", false)
                };
            });
        }

        // ---- CPU / メモリ / ディスク ----------------------------------------

        static ProbeResult<CpuInfo> ProbeCpu()
        {
            const string src = "Win32_Processor";
            return Run(src, () =>
            {
                var p = WmiGateway.First(WmiGateway.Cimv2,
                    "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
                if (p == null) return null;
                var name = WmiGateway.Str(p, "Name");
                return new CpuInfo
                {
                    Name = name == null ? null : Regex.Replace(name, @"\s+", " "),
                    Cores = WmiGateway.Int(p, "NumberOfCores"),
                    LogicalProcessors = WmiGateway.Int(p, "NumberOfLogicalProcessors")
                };
            });
        }

        static ProbeResult<MemoryInfo> ProbeMemory()
        {
            const string src = "Win32_ComputerSystem / Win32_OperatingSystem";
            return Run(src, () =>
            {
                long total = WmiGateway.Long(
                    WmiGateway.First(WmiGateway.Cimv2, "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"),
                    "TotalPhysicalMemory");
                // FreePhysicalMemory は KB 単位
                long freeKb = WmiGateway.Long(
                    WmiGateway.First(WmiGateway.Cimv2, "SELECT FreePhysicalMemory FROM Win32_OperatingSystem"),
                    "FreePhysicalMemory");
                if (total <= 0) return null;
                return new MemoryInfo { TotalBytes = total, FreeBytes = freeKb * 1024L };
            });
        }

        static string SystemDriveLetter()
        {
            var d = Environment.GetEnvironmentVariable("SystemDrive");
            return (string.IsNullOrEmpty(d) ? "C:" : d).ToUpperInvariant();
        }

        static ProbeResult<List<LogicalDiskInfo>> ProbeLogicalDisks()
        {
            const string src = "Win32_LogicalDisk (DriveType=3)";
            var sys = SystemDriveLetter();
            return RunList(src, () =>
            {
                var list = new List<LogicalDiskInfo>();
                foreach (var ld in WmiGateway.Query(WmiGateway.Cimv2,
                             "SELECT DeviceID, FreeSpace, Size FROM Win32_LogicalDisk WHERE DriveType = 3"))
                {
                    long size = WmiGateway.Long(ld, "Size");
                    if (size <= 0) continue;
                    var id = WmiGateway.Str(ld, "DeviceID");
                    list.Add(new LogicalDiskInfo
                    {
                        DeviceId = id,
                        SizeBytes = size,
                        FreeBytes = WmiGateway.Long(ld, "FreeSpace"),
                        IsSystemDrive = string.Equals(id, sys, StringComparison.OrdinalIgnoreCase)
                    });
                }
                return list;
            });
        }

        /// <summary>
        /// 物理ディスク一覧。起動ドライブに対応するものに IsBootDisk を立てる。
        /// FriendlyName は型番であってシリアル番号ではない（設計原則 1：SerialNumber は読まない）。
        /// </summary>
        static ProbeResult<List<PhysicalDiskInfo>> ProbePhysicalDisks()
        {
            const string src = @"root\Microsoft\Windows\Storage MSFT_PhysicalDisk (MSFT_Partition で起動ディスクを特定)";
            var letter = SystemDriveLetter().Substring(0, 1);

            return RunList(src, () =>
            {
                string bootDiskNumber = null;
                try
                {
                    foreach (var p in WmiGateway.Query(WmiGateway.StorageNs,
                                 "SELECT DriveLetter, DiskNumber FROM MSFT_Partition"))
                    {
                        var dl = p["DriveLetter"];
                        if (dl == null) continue;
                        string ch;
                        if (dl is char) ch = ((char)dl).ToString();
                        else if (dl is ushort) ch = ((char)(ushort)dl).ToString();
                        else ch = dl.ToString();

                        if (string.Equals(ch, letter, StringComparison.OrdinalIgnoreCase))
                        {
                            bootDiskNumber = WmiGateway.Str(p, "DiskNumber");
                            break;
                        }
                    }
                }
                catch { /* 起動ディスクが特定できなくても、一覧そのものは返す */ }

                var list = new List<PhysicalDiskInfo>();
                foreach (var d in WmiGateway.Query(WmiGateway.StorageNs,
                             "SELECT DeviceId, FriendlyName, MediaType, BusType, HealthStatus FROM MSFT_PhysicalDisk"))
                {
                    var id = WmiGateway.Str(d, "DeviceId");
                    list.Add(new PhysicalDiskInfo
                    {
                        DeviceId = id,
                        FriendlyName = WmiGateway.Str(d, "FriendlyName"),
                        MediaType = WmiGateway.Int(d, "MediaType"),
                        BusType = WmiGateway.Int(d, "BusType"),
                        HealthStatus = WmiGateway.Int(d, "HealthStatus"),
                        IsBootDisk = bootDiskNumber != null && bootDiskNumber == id
                    });
                }
                return list;
            });
        }

        static ProbeResult<PhysicalDiskInfo> PickBootDisk(ProbeResult<List<PhysicalDiskInfo>> disks)
        {
            const string src = @"root\Microsoft\Windows\Storage MSFT_Partition → MSFT_PhysicalDisk";
            if (disks == null) return ProbeResult<PhysicalDiskInfo>.Empty("収集が行われませんでした", src);
            if (!disks.IsOk)
                return new ProbeResult<PhysicalDiskInfo>
                {
                    Outcome = disks.Outcome,
                    Reason = disks.Reason,
                    Source = src
                };

            var boot = disks.Value.FirstOrDefault(d => d.IsBootDisk);
            if (boot == null)
                return ProbeResult<PhysicalDiskInfo>.Empty("起動ドライブに対応するディスクを特定できませんでした", src);
            return ProbeResult<PhysicalDiskInfo>.Ok(boot, src);
        }

        /// <summary>
        /// SMART 詳細。MSFT_StorageReliabilityCounter は**直接列挙すると昇格しても 0 行**で、
        /// MSFT_PhysicalDisk からの関連付けでしか値が返らない（SPEC §7-1 ⑪、PCR-002 の切り分け）。
        /// 非昇格では関連付けでも「拒否」ではなく「空」で返る（PCR-003 で実測）。
        /// 空を Empty として返し、その理由を権限で分けるのは判定層の仕事。
        /// </summary>
        internal static ProbeResult<List<ReliabilityCounter>> ProbeReliability()
        {
            const string src = "MSFT_PhysicalDisk → MSFT_StorageReliabilityCounter（関連付け）";
            return RunList(src, () =>
            {
                var list = new List<ReliabilityCounter>();
                // 関連付けには __PATH が要るので、キー（ObjectId）を含めて取る
                foreach (var d in WmiGateway.Query(WmiGateway.StorageNs,
                             "SELECT ObjectId, DeviceId FROM MSFT_PhysicalDisk"))
                {
                    foreach (var c in WmiGateway.Related(d, "MSFT_StorageReliabilityCounter"))
                    {
                        list.Add(new ReliabilityCounter
                        {
                            DeviceId = WmiGateway.Str(d, "DeviceId"),
                            Temperature = WmiGateway.IntOrNull(c, "Temperature"),
                            PowerOnHours = WmiGateway.LongOrNull(c, "PowerOnHours"),
                            Wear = WmiGateway.IntOrNull(c, "Wear")
                        });
                    }
                }
                // 行はあるが全部ゼロ／null＝実質「報告していない」。これも空として扱う
                if (!list.Any(x => x.HasAnyValue)) return new List<ReliabilityCounter>();
                return list;
            });
        }

        // ---- Windows 11 の 3 部品 ------------------------------------------

        static ProbeResult<TpmInfo> ProbeTpm()
        {
            const string src = @"root\cimv2\Security\MicrosoftTpm Win32_Tpm";
            return Run(src, () =>
            {
                var t = WmiGateway.First(WmiGateway.TpmNs,
                    "SELECT SpecVersion, IsEnabled_InitialValue FROM Win32_Tpm");
                if (t == null) return null;
                return new TpmInfo
                {
                    SpecVersion = WmiGateway.Str(t, "SpecVersion"),
                    IsEnabled = WmiGateway.Bool(t, "IsEnabled_InitialValue", false)
                };
            });
        }

        static ProbeResult<SecureBootInfo> ProbeSecureBoot()
        {
            const string src = @"レジストリ SecureBoot\State / kernel32!GetFirmwareType";
            return Run(src, () =>
            {
                var fw = WmiGateway.FirmwareType();
                return new SecureBootInfo
                {
                    UefiSecureBootEnabled = WmiGateway.RegInt(
                        @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled"),
                    Firmware = fw.HasValue && fw.Value <= 2 ? (FirmwareKind)fw.Value : FirmwareKind.Unknown
                };
            });
        }

        /// <summary>
        /// Windows 自身が残したアップグレード可否の判定記録（SPEC §5-1 の逃げ道、§7-5）。
        /// 参考情報なので、無くても「取得できませんでした」を出す対象ではない。
        /// </summary>
        static ProbeResult<List<UpgradeIndicator>> ProbeUpgradeIndicators()
        {
            const string src = @"AppCompatFlags\TargetVersionUpgradeExperienceIndicators";
            return RunList(src, () =>
            {
                var list = new List<UpgradeIndicator>();
                using (var k = WmiGateway.Hklm(CvPath + @"\AppCompatFlags\TargetVersionUpgradeExperienceIndicators"))
                {
                    if (k == null) return list;
                    foreach (var name in k.GetSubKeyNames().OrderBy(n => n, StringComparer.Ordinal))
                    {
                        using (var sk = k.OpenSubKey(name))
                        {
                            if (sk == null) continue;
                            var upg = sk.GetValue("UpgEx") as string;
                            if (string.IsNullOrEmpty(upg)) continue;
                            list.Add(new UpgradeIndicator
                            {
                                KeyName = name,
                                UpgEx = upg,
                                RedReason = sk.GetValue("RedReason") as string
                            });
                        }
                    }
                }
                return list;
            });
        }
    }
}

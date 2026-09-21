using System;
using System.Collections.Generic;
using System.IO;
using Microforge.PcReport.Model;

namespace Microforge.PcReport.Tests
{
    /// <summary>
    /// テスト用の SystemSnapshot を組み立てる道具。
    /// 判定層は WMI を参照しないので、こうして生値を差し替えるだけで全分岐を試せる（AC2）。
    /// </summary>
    internal static class Fixtures
    {
        /// <summary>テストの基準時刻。now は必ず注入するので、実行日が変わってもテストは動く。</summary>
        public static readonly DateTime Now = new DateTime(2026, 9, 21, 19, 30, 0);

        /// <summary>すべての項目が取れている、ごく普通の Windows 11 機。</summary>
        public static SystemSnapshot Healthy()
        {
            return new SystemSnapshot
            {
                RanAsAdmin = false,

                Os = ProbeResult<OsInfo>.Ok(new OsInfo
                {
                    Caption = "Windows 11 Pro",
                    DisplayVersion = "25H2",
                    Build = 26200,
                    Ubr = 9457,
                    Architecture = "64 ビット"
                }, "Win32_OperatingSystem"),

                LastBootUpTime = OkDate(new DateTime(2026, 9, 15, 22, 49, 0), "Win32_OperatingSystem.LastBootUpTime"),

                Updates = ProbeResult<UpdateSources>.Ok(new UpdateSources
                {
                    Qfe =
                    {
                        new UpdateEntry { Date = new DateTime(2026, 9, 15), Title = "KB5129195", KbId = "KB5129195" }
                    },
                    Wua =
                    {
                        new UpdateEntry
                        {
                            Date = new DateTime(2026, 9, 15),
                            Title = "2026-09 セキュリティ パッチ (KB5129195) (26200.9457)",
                            KbId = "KB5129195"
                        }
                    }
                }, "QFE / WUA"),

                AntiVirus = ProbeResult<List<AntiVirusProduct>>.Ok(new List<AntiVirusProduct>
                {
                    // gram17 実測値
                    new AntiVirusProduct { DisplayName = "Windows Defender", ProductState = 0x060100 },
                    new AntiVirusProduct { DisplayName = "ウイルスバスター クラウド", ProductState = 0x041000 }
                }, @"root\SecurityCenter2 AntiVirusProduct"),

                Defender = ProbeResult<DefenderStatus>.Ok(new DefenderStatus
                {
                    SignatureLastUpdated = new DateTime(2026, 9, 21, 4, 55, 0),
                    SignatureVersion = "1.459.312.0",
                    AntivirusEnabled = true,
                    RealTimeProtectionEnabled = false
                }, "MSFT_MpComputerStatus"),

                Cpu = ProbeResult<CpuInfo>.Ok(new CpuInfo
                {
                    Name = "Intel(R) Core(TM) i7-8565U CPU @ 1.80GHz",
                    Cores = 4,
                    LogicalProcessors = 8
                }, "Win32_Processor"),

                Memory = ProbeResult<MemoryInfo>.Ok(new MemoryInfo
                {
                    TotalBytes = 42_700_000_000L,
                    FreeBytes = 13_700_000_000L
                }, "Win32_ComputerSystem"),

                LogicalDisks = ProbeResult<List<LogicalDiskInfo>>.Ok(new List<LogicalDiskInfo>
                {
                    new LogicalDiskInfo
                    {
                        DeviceId = "C:",
                        SizeBytes = 2_000_000_000_000L,
                        FreeBytes = 1_680_000_000_000L,
                        IsSystemDrive = true
                    }
                }, "Win32_LogicalDisk"),

                PhysicalDisks = ProbeResult<List<PhysicalDiskInfo>>.Ok(new List<PhysicalDiskInfo>
                {
                    BootDisk()
                }, "MSFT_PhysicalDisk"),

                BootDisk = ProbeResult<PhysicalDiskInfo>.Ok(BootDisk(), "MSFT_Partition → MSFT_PhysicalDisk"),

                Reliability = ProbeResult<List<ReliabilityCounter>>.Ok(new List<ReliabilityCounter>
                {
                    new ReliabilityCounter { DeviceId = "0", Temperature = 45, PowerOnHours = 3076, Wear = 0 }
                }, "MSFT_StorageReliabilityCounter"),

                Tpm = ProbeResult<TpmInfo>.Ok(new TpmInfo { SpecVersion = "2.0, 0, 1.38", IsEnabled = true },
                    "Win32_Tpm"),

                SecureBoot = ProbeResult<SecureBootInfo>.Ok(new SecureBootInfo
                {
                    UefiSecureBootEnabled = 1,
                    Firmware = FirmwareKind.Uefi
                }, @"SecureBoot\State"),

                UpgradeIndicators = ProbeResult<List<UpgradeIndicator>>.Ok(new List<UpgradeIndicator>
                {
                    new UpgradeIndicator { KeyName = "GE25H2", UpgEx = "Green", RedReason = "None" }
                }, "AppCompatFlags")
            };
        }

        static PhysicalDiskInfo BootDisk()
        {
            return new PhysicalDiskInfo
            {
                DeviceId = "0",
                FriendlyName = "WDC WDS200T2B0B-00YS70",
                MediaType = 4,
                BusType = 11,
                HealthStatus = 0,
                IsBootDisk = true
            };
        }

        public static ProbeResult<DateTime> OkDate(DateTime d, string source)
        {
            return new ProbeResult<DateTime> { Outcome = ProbeOutcome.Ok, Value = d, Source = source };
        }

        /// <summary>
        /// すべての収集が同じ結果種別で失敗した snapshot（AC3 の「拒否／空／例外」3 通りに使う）。
        /// </summary>
        public static SystemSnapshot AllFailing(ProbeOutcome outcome, string reason)
        {
            Func<string> src = () => "テスト用の情報源";

            return new SystemSnapshot
            {
                RanAsAdmin = false,
                Os = Fail<OsInfo>(outcome, reason, src()),
                LastBootUpTime = Fail<DateTime>(outcome, reason, src()),
                Updates = Fail<UpdateSources>(outcome, reason, src()),
                AntiVirus = Fail<List<AntiVirusProduct>>(outcome, reason, src()),
                Defender = Fail<DefenderStatus>(outcome, reason, src()),
                Cpu = Fail<CpuInfo>(outcome, reason, src()),
                Memory = Fail<MemoryInfo>(outcome, reason, src()),
                LogicalDisks = Fail<List<LogicalDiskInfo>>(outcome, reason, src()),
                PhysicalDisks = Fail<List<PhysicalDiskInfo>>(outcome, reason, src()),
                BootDisk = Fail<PhysicalDiskInfo>(outcome, reason, src()),
                Reliability = Fail<List<ReliabilityCounter>>(outcome, reason, src()),
                Tpm = Fail<TpmInfo>(outcome, reason, src()),
                SecureBoot = Fail<SecureBootInfo>(outcome, reason, src()),
                UpgradeIndicators = Fail<List<UpgradeIndicator>>(outcome, reason, src())
            };
        }

        public static ProbeResult<T> Fail<T>(ProbeOutcome outcome, string reason, string source)
        {
            return new ProbeResult<T> { Outcome = outcome, Reason = reason, Source = source };
        }

        /// <summary>
        /// テストの実行フォルダから repo ルート（app/）を探す。
        /// ソース走査のテスト（AC6）で使う。
        /// </summary>
        public static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "src", "PcReport", "PcReport.csproj")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("app/ の場所が見つかりませんでした。");
        }
    }
}

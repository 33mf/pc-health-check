using System;
using System.Collections.Generic;

namespace Microforge.PcReport.Model
{
    // ------------------------------------------------------------------
    // 収集層が返す「生値」。判定・表示文言・しきい値はここに一切入れない。
    // 設計原則 1：ユーザー名・PC 名・IP アドレス・シリアル番号は
    //             そもそもこの DTO に置き場所を作らない（取得しない）。
    // ------------------------------------------------------------------

    public sealed class OsInfo
    {
        /// <summary>「Windows 11 Pro」等（"Microsoft " は収集層で落とす）。</summary>
        public string Caption;
        /// <summary>「25H2」等。</summary>
        public string DisplayVersion;
        public int Build;
        public int? Ubr;
        /// <summary>「64 ビット」等。</summary>
        public string Architecture;
    }

    public sealed class UpdateEntry
    {
        public DateTime Date;
        public string Title;
        /// <summary>「KB5129195」。KB 番号を持たないものは null。</summary>
        public string KbId;
    }

    /// <summary>
    /// 最後に更新が入った日の材料。QFE と WUA 履歴を「混ぜずに別々に」持つ。
    /// どちらを採るかは判定層が決める（SPEC §7-1 ②：WUA には Store アプリの更新が混ざる）。
    /// </summary>
    public sealed class UpdateSources
    {
        public List<UpdateEntry> Qfe = new List<UpdateEntry>();
        public List<UpdateEntry> Wua = new List<UpdateEntry>();
        /// <summary>QFE / WUA それぞれが失敗したときの理由（片方だけ落ちることがある）。</summary>
        public string QfeReason;
        public string WuaReason;
    }

    public sealed class AntiVirusProduct
    {
        public string DisplayName;
        /// <summary>SecurityCenter2 の productState（生値のまま。解釈は判定層）。</summary>
        public uint ProductState;
    }

    public sealed class DefenderStatus
    {
        public DateTime? SignatureLastUpdated;
        public string SignatureVersion;
        public bool AntivirusEnabled;
        public bool RealTimeProtectionEnabled;
    }

    public sealed class CpuInfo
    {
        public string Name;
        public int Cores;
        public int LogicalProcessors;
    }

    public sealed class MemoryInfo
    {
        public long TotalBytes;
        public long FreeBytes;
    }

    public sealed class LogicalDiskInfo
    {
        /// <summary>「C:」。ドライブレターは個人を特定しないので載せてよい。</summary>
        public string DeviceId;
        public long SizeBytes;
        public long FreeBytes;
        public bool IsSystemDrive;
    }

    public sealed class PhysicalDiskInfo
    {
        public string DeviceId;
        /// <summary>「WDC WDS200T2B0B-00YS70」＝型番。シリアル番号ではない（設計原則 1）。</summary>
        public string FriendlyName;
        /// <summary>MSFT_PhysicalDisk.MediaType の生値（3=HDD 4=SSD 0=不明）。</summary>
        public int MediaType;
        /// <summary>MSFT_PhysicalDisk.BusType の生値（17=NVMe 11=SATA 7=USB 8=RAID）。</summary>
        public int BusType;
        /// <summary>MSFT_PhysicalDisk.HealthStatus の生値（0=正常 1=警告 2=異常）。</summary>
        public int HealthStatus;
        public bool IsBootDisk;
    }

    public sealed class ReliabilityCounter
    {
        public string DeviceId;
        public int? Temperature;
        public long? PowerOnHours;
        public int? Wear;

        /// <summary>温度・使用時間・消耗のいずれも報告されていない＝実質「空」。</summary>
        public bool HasAnyValue
        {
            get
            {
                return (Temperature.HasValue && Temperature.Value > 0)
                    || (PowerOnHours.HasValue && PowerOnHours.Value > 0)
                    || (Wear.HasValue && Wear.Value > 0);
            }
        }
    }

    public sealed class TpmInfo
    {
        /// <summary>「2.0, 0, 1.38」等の生値。</summary>
        public string SpecVersion;
        public bool IsEnabled;
    }

    public enum FirmwareKind
    {
        Unknown = 0,
        Bios = 1,
        Uefi = 2
    }

    public sealed class SecureBootInfo
    {
        /// <summary>レジストリ SecureBoot\State\UEFISecureBootEnabled。値が無ければ null。</summary>
        public int? UefiSecureBootEnabled;
        /// <summary>kernel32!GetFirmwareType の結果（PEFirmwareType レジストリは通常起動では存在しない。SPEC §7-1）。</summary>
        public FirmwareKind Firmware;
    }

    public sealed class UpgradeIndicator
    {
        public string KeyName;
        public string UpgEx;
        public string RedReason;
    }

    /// <summary>
    /// 収集層の全出力。判定層はこれと now だけを受け取る（AC2）。
    /// </summary>
    public sealed class SystemSnapshot
    {
        /// <summary>昇格した管理者として動いているか。SMART 詳細が空のときの理由分けに使う。</summary>
        public bool RanAsAdmin;

        public ProbeResult<OsInfo> Os;
        public ProbeResult<DateTime> LastBootUpTime;
        public ProbeResult<UpdateSources> Updates;
        public ProbeResult<List<AntiVirusProduct>> AntiVirus;
        public ProbeResult<DefenderStatus> Defender;
        public ProbeResult<CpuInfo> Cpu;
        public ProbeResult<MemoryInfo> Memory;
        public ProbeResult<List<LogicalDiskInfo>> LogicalDisks;
        public ProbeResult<PhysicalDiskInfo> BootDisk;
        public ProbeResult<List<PhysicalDiskInfo>> PhysicalDisks;
        public ProbeResult<List<ReliabilityCounter>> Reliability;
        public ProbeResult<TpmInfo> Tpm;
        public ProbeResult<SecureBootInfo> SecureBoot;
        public ProbeResult<List<UpgradeIndicator>> UpgradeIndicators;
    }
}

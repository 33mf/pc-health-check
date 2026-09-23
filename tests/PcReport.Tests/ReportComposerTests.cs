using System;
using System.Collections.Generic;
using System.Linq;
using Microforge.PcReport.Analysis;
using Microforge.PcReport.Model;
using Xunit;

namespace Microforge.PcReport.Tests
{
    /// <summary>
    /// AC2：判定ロジック（しきい値・productState の解釈・日付の解釈・表示文言の選択）の単体テスト。
    /// 収集（WMI/レジストリ）を一切動かさずに書けていること自体が、分離できている証拠。
    /// now はすべて引数で渡す＝実行日が変わってもテストの意味が変わらない（Lesson #19 日付爆弾）。
    /// </summary>
    public class ReportComposerTests
    {
        static Report Compose(SystemSnapshot s, DateTime? now = null)
        {
            return ReportComposer.Compose(s, now ?? Fixtures.Now, "1.0.0");
        }

        static IEnumerable<Item> AllItems(Report r)
        {
            return r.Sections.SelectMany(s => s.Items);
        }

        static Item Find(Report r, string labelPrefix)
        {
            return AllItems(r).FirstOrDefault(i =>
                i.Label != null && i.Label.StartsWith(labelPrefix, StringComparison.Ordinal));
        }

        // ---- now の注入（AC2）--------------------------------------------

        [Fact]
        public void 基準時刻はレポートにそのまま入る()
        {
            var now = new DateTime(2030, 1, 2, 3, 4, 5);
            var r = Compose(Fixtures.Healthy(), now);
            Assert.Equal(now, r.GeneratedAt);
        }

        [Theory]
        // 更新が 44 日前までは問題なし、45 日で要確認、90 日で要対応
        [InlineData(0, Status.Ok)]
        [InlineData(44, Status.Ok)]
        [InlineData(45, Status.Warn)]
        [InlineData(89, Status.Warn)]
        [InlineData(90, Status.Bad)]
        [InlineData(400, Status.Bad)]
        public void 最後に更新が入った日の判定は経過日数だけで決まる(int daysAgo, Status expected)
        {
            var now = new DateTime(2027, 6, 15, 12, 0, 0);
            var installed = now.Date.AddDays(-daysAgo);

            var s = Fixtures.Healthy();
            s.Updates = ProbeResult<UpdateSources>.Ok(new UpdateSources
            {
                Qfe = { new UpdateEntry { Date = installed, Title = "KB5000001", KbId = "KB5000001" } }
            }, "QFE");

            var item = Find(Compose(s, now), "最後に更新プログラムが入った日");
            Assert.Equal(expected, item.Status);
        }

        [Theory]
        [InlineData(29, Status.Ok)]
        [InlineData(30, Status.Warn)]
        public void 長く再起動していないと要確認になる(int daysAgo, Status expected)
        {
            var now = new DateTime(2027, 6, 15, 12, 0, 0);
            var s = Fixtures.Healthy();
            s.LastBootUpTime = Fixtures.OkDate(now.Date.AddDays(-daysAgo), "LastBootUpTime");

            Assert.Equal(expected, Find(Compose(s, now), "最後に再起動した日").Status);
        }

        [Fact]
        public void 同じ入力でも基準時刻が違えば判定が変わる()
        {
            var s = Fixtures.Healthy();
            var installed = new DateTime(2026, 9, 15);
            s.Updates = ProbeResult<UpdateSources>.Ok(new UpdateSources
            {
                Qfe = { new UpdateEntry { Date = installed, Title = "KB5129195", KbId = "KB5129195" } }
            }, "QFE");

            Assert.Equal(Status.Ok, Find(Compose(s, installed.AddDays(10)), "最後に更新プログラム").Status);
            Assert.Equal(Status.Warn, Find(Compose(s, installed.AddDays(50)), "最後に更新プログラム").Status);
            Assert.Equal(Status.Bad, Find(Compose(s, installed.AddDays(120)), "最後に更新プログラム").Status);
        }

        // ---- 更新の取得元の絞り込み（SPEC §7-1 ②、gram17 実測）-----------

        [Fact]
        public void Storeアプリの更新は最後の更新日に数えない()
        {
            // KB 番号を持たない＝Store アプリ。実測で WUA 履歴に混ざっていた
            var entries = new[]
            {
                new UpdateEntry { Date = new DateTime(2026, 9, 20), Title = "9NRZT3Q9R3DL-Microsoft.WindowsAppRuntime.2", KbId = null },
                new UpdateEntry { Date = new DateTime(2026, 9, 15), Title = "2026-09 セキュリティ パッチ (KB5129195)", KbId = "KB5129195" }
            };

            var kept = ReportComposer.FilterWindowsUpdates(entries);
            Assert.Single(kept);
            Assert.Equal("KB5129195", kept[0].KbId);
        }

        [Theory]
        // Defender の定義更新とプラットフォーム更新は「Windows の更新プログラム」に数えない
        [InlineData("KB2267602", "セキュリティ インテリジェンス更新プログラム")]
        [InlineData("KB4052623", "Microsoft Defender Antivirus マルウェア対策プラットフォームの更新プログラム")]
        public void ウイルス対策の自動更新は最後の更新日に数えない(string kb, string title)
        {
            var entries = new[]
            {
                new UpdateEntry { Date = new DateTime(2026, 9, 20), Title = title, KbId = kb },
                new UpdateEntry { Date = new DateTime(2026, 9, 15), Title = "2026-09 セキュリティ パッチ (KB5129195)", KbId = "KB5129195" }
            };

            var kept = ReportComposer.FilterWindowsUpdates(entries);
            Assert.Single(kept);
            Assert.Equal("KB5129195", kept[0].KbId);
        }

        [Fact]
        public void 更新の取得元は両方ともレポートに残す()
        {
            // AC9 で「更新の履歴」画面と突き合わせるため、QFE と WUA の両方が見えている必要がある
            var item = Find(Compose(Fixtures.Healthy()), "最後に更新プログラムが入った日");
            Assert.Contains("Win32_QuickFixEngineering", item.Source);
            Assert.Contains("更新の履歴", item.Source);
        }

        [Fact]
        public void Windowsの更新が一件も無ければ取得できませんと書く()
        {
            var s = Fixtures.Healthy();
            s.Updates = ProbeResult<UpdateSources>.Ok(new UpdateSources
            {
                // Store アプリだけ＝Windows の更新は 1 件も無い
                Wua = { new UpdateEntry { Date = new DateTime(2026, 9, 20), Title = "SomeStoreApp", KbId = null } },
                QfeReason = "更新の記録が見つかりませんでした"
            }, "QFE / WUA");

            var item = Find(Compose(s), "最後に更新プログラムが入った日");
            Assert.Equal(Status.Unavailable, item.Status);
            Assert.False(string.IsNullOrEmpty(item.Reason));
        }

        // ---- productState の解釈（SPEC §7-1 ① の実測値で確かめる）--------

        [Theory]
        [InlineData(0x041000u, true)]   // ウイルスバスター クラウド：有効
        [InlineData(0x060100u, false)]  // Windows Defender：他社 AV があるため受動モード
        public void productStateから有効かどうかを読む(uint state, bool expected)
        {
            Assert.Equal(expected, ReportComposer.IsAntiVirusEnabled(state));
        }

        [Theory]
        [InlineData(0x041000u, true)]
        [InlineData(0x060100u, true)]
        [InlineData(0x041010u, false)]  // 下位バイト 0x10＝定義ファイルが古い
        public void productStateから定義ファイルが最新かを読む(uint state, bool expected)
        {
            Assert.Equal(expected, ReportComposer.IsAntiVirusUpToDate(state));
        }

        [Fact]
        public void 他社のウイルス対策が有効ならDefenderが無効でも要対応にしない()
        {
            var r = Compose(Fixtures.Healthy());
            var defender = AllItems(r).First(i => i.Label == "ウイルス対策ソフト" && i.Value.Contains("Defender"));
            Assert.NotEqual(Status.Bad, defender.Status);
        }

        [Fact]
        public void 他社のウイルス対策が有効ならDefenderのリアルタイム保護オフを警告しない()
        {
            // gram17 実測の状態：ウイルスバスター有効・Defender は待機中で RTP オフ。これは正常
            var item = Find(Compose(Fixtures.Healthy()), "Microsoft Defender の定義ファイル");
            Assert.Equal(Status.Info, item.Status);
            Assert.DoesNotContain("無防備", item.Note ?? "");
        }

        [Fact]
        public void 他社のウイルス対策が無いときのリアルタイム保護オフは要対応()
        {
            var s = Fixtures.Healthy();
            s.AntiVirus = ProbeResult<List<AntiVirusProduct>>.Ok(new List<AntiVirusProduct>
            {
                new AntiVirusProduct { DisplayName = "Windows Defender", ProductState = 0x061000 }
            }, "SecurityCenter2");

            var item = Find(Compose(s), "Microsoft Defender の定義ファイル");
            Assert.Equal(Status.Bad, item.Status);
        }

        [Fact]
        public void ウイルス対策が一つも有効でなければ要対応として出す()
        {
            var s = Fixtures.Healthy();
            s.AntiVirus = ProbeResult<List<AntiVirusProduct>>.Ok(new List<AntiVirusProduct>
            {
                new AntiVirusProduct { DisplayName = "Windows Defender", ProductState = 0x060100 }
            }, "SecurityCenter2");

            var av = AllItems(Compose(s)).First(i => i.Label == "ウイルス対策ソフト");
            Assert.Equal(Status.Bad, av.Status);
        }

        [Theory]
        [InlineData(7, Status.Warn)]
        [InlineData(6, Status.Ok)]
        public void Defenderの定義ファイルが古いと要確認になる(int daysAgo, Status expected)
        {
            var now = new DateTime(2027, 3, 1, 9, 0, 0);
            var s = Fixtures.Healthy();
            // 他社 AV を外して Defender が現役の状態にする
            s.AntiVirus = ProbeResult<List<AntiVirusProduct>>.Ok(new List<AntiVirusProduct>
            {
                new AntiVirusProduct { DisplayName = "Windows Defender", ProductState = 0x061000 }
            }, "SecurityCenter2");
            s.Defender = ProbeResult<DefenderStatus>.Ok(new DefenderStatus
            {
                SignatureLastUpdated = now.Date.AddDays(-daysAgo),
                SignatureVersion = "1.0.0.0",
                AntivirusEnabled = true,
                RealTimeProtectionEnabled = true
            }, "MSFT_MpComputerStatus");

            Assert.Equal(expected, Find(Compose(s, now), "Microsoft Defender の定義ファイル").Status);
        }

        // ---- 起動ドライブの種類（SPEC §4 ★ / §7-3）-----------------------

        [Theory]
        [InlineData(4, 11, "WDC WDS200T2B0B", "SSD")]        // gram17 実測：SATA SSD
        [InlineData(0, 17, "Samsung", "SSD")]                 // NVMe なら SSD 確定
        [InlineData(3, 11, "ST1000DM003", "HDD")]
        [InlineData(0, 11, "Generic SSD Drive", "SSD（型番から推定）")]
        [InlineData(0, 11, "Virtual Disk", null)]             // VM：判定できない
        public void MediaTypeとBusTypeから起動ドライブの種類を決める(int media, int bus, string name, string expected)
        {
            Assert.Equal(expected, ReportComposer.ClassifyDriveKind(media, bus, name));
        }

        [Fact]
        public void HDD起動は要確認にして換装を案内する()
        {
            var s = Fixtures.Healthy();
            var hdd = new PhysicalDiskInfo
            {
                DeviceId = "0", FriendlyName = "ST1000DM003", MediaType = 3, BusType = 11,
                HealthStatus = 0, IsBootDisk = true
            };
            s.BootDisk = ProbeResult<PhysicalDiskInfo>.Ok(hdd, "MSFT_PhysicalDisk");

            var item = Find(Compose(s), "起動ドライブの種類");
            Assert.Equal(Status.Warn, item.Status);
            Assert.Contains("SSD", item.Note);
        }

        [Fact]
        public void 種類を報告しないディスクは取得できませんと書く()
        {
            var s = Fixtures.Healthy();
            s.BootDisk = ProbeResult<PhysicalDiskInfo>.Ok(new PhysicalDiskInfo
            {
                DeviceId = "0", FriendlyName = "Virtual Disk", MediaType = 0, BusType = 1,
                HealthStatus = 0, IsBootDisk = true
            }, "MSFT_PhysicalDisk");

            var item = Find(Compose(s), "起動ドライブの種類");
            Assert.Equal(Status.Unavailable, item.Status);
            Assert.Contains("報告していません", item.Reason);
        }

        // ---- ディスクの空き・メモリ ----------------------------------------

        [Theory]
        [InlineData(0.25, Status.Ok)]
        [InlineData(0.19, Status.Warn)]
        [InlineData(0.09, Status.Bad)]
        public void ディスクの空きが少ないと警告する(double freeRatio, Status expected)
        {
            var s = Fixtures.Healthy();
            const long size = 1_000_000_000_000L;
            s.LogicalDisks = ProbeResult<List<LogicalDiskInfo>>.Ok(new List<LogicalDiskInfo>
            {
                new LogicalDiskInfo
                {
                    DeviceId = "C:", SizeBytes = size,
                    FreeBytes = (long)(size * freeRatio), IsSystemDrive = true
                }
            }, "Win32_LogicalDisk");

            Assert.Equal(expected, Find(Compose(s), "ディスクの空き").Status);
        }

        [Theory]
        [InlineData(4L * 1024 * 1024 * 1024, Status.Warn)]
        [InlineData(16L * 1024 * 1024 * 1024, Status.Ok)]
        public void メモリが少ないと要確認になる(long total, Status expected)
        {
            var s = Fixtures.Healthy();
            s.Memory = ProbeResult<MemoryInfo>.Ok(
                new MemoryInfo { TotalBytes = total, FreeBytes = total / 4 }, "Win32_ComputerSystem");

            Assert.Equal(expected, Find(Compose(s), "メモリ").Status);
        }

        // ---- SMART 詳細：空の理由を権限で分ける（SPEC §7-1 ③、R-Evidence-5）----

        [Fact]
        public void 非昇格でSMART詳細が空なら管理者権限が理由になる()
        {
            var s = Fixtures.Healthy();
            s.RanAsAdmin = false;
            s.Reliability = ProbeResult<List<ReliabilityCounter>>.Empty(
                "情報源に値がありませんでした", "MSFT_StorageReliabilityCounter");

            var item = Find(Compose(s), "詳しい SMART 情報");
            Assert.Equal(Status.Unavailable, item.Status);
            Assert.Contains("管理者権限", item.Reason);
        }

        [Fact]
        public void 昇格してもSMART詳細が空ならディスクが報告していないと書く()
        {
            // 昇格しているのに空＝権限のせいにはできない。断定を分ける（R-Evidence-5）
            var s = Fixtures.Healthy();
            s.RanAsAdmin = true;
            s.Reliability = ProbeResult<List<ReliabilityCounter>>.Empty(
                "情報源に値がありませんでした", "MSFT_StorageReliabilityCounter");

            var item = Find(Compose(s), "詳しい SMART 情報");
            Assert.Equal(Status.Unavailable, item.Status);
            Assert.Contains("報告していません", item.Reason);
            Assert.DoesNotContain("管理者権限", item.Reason);
        }

        [Fact]
        public void 非昇格でSMART詳細が拒否されたら管理者権限が理由になる()
        {
            var s = Fixtures.Healthy();
            s.RanAsAdmin = false;
            s.Reliability = ProbeResult<List<ReliabilityCounter>>.Denied("MSFT_StorageReliabilityCounter");

            var item = Find(Compose(s), "詳しい SMART 情報");
            Assert.Equal(Status.Unavailable, item.Status);
            Assert.Contains("管理者権限", item.Reason);
        }

        [Fact]
        public void 昇格していてSMART詳細が拒否されたら管理者として実行を勧めない()
        {
            // 昇格しているのに「管理者権限が必要」「管理者として実行すると…」と出すと、やっても取れない案内になる（原則 2）
            var s = Fixtures.Healthy();
            s.RanAsAdmin = true;
            s.Reliability = ProbeResult<List<ReliabilityCounter>>.Denied("MSFT_StorageReliabilityCounter");

            var item = Find(Compose(s), "詳しい SMART 情報");
            Assert.Equal(Status.Unavailable, item.Status);
            Assert.Contains("拒否", item.Reason);
            Assert.DoesNotContain("管理者権限が必要", item.Reason);
            Assert.DoesNotContain("管理者として実行すると", item.Reason);
        }

        [Fact]
        public void 昇格していればSMART詳細の理由に管理者として実行の案内は出ない()
        {
            // Empty / Denied / Failed のどれでも、昇格時は「管理者として実行」を勧めない
            foreach (var p in new[]
            {
                ProbeResult<List<ReliabilityCounter>>.Empty("情報源に値がありませんでした", "x"),
                ProbeResult<List<ReliabilityCounter>>.Denied("x"),
                ProbeResult<List<ReliabilityCounter>>.Failed("取得中にエラーが発生しました", "x")
            })
            {
                var s = Fixtures.Healthy();
                s.RanAsAdmin = true;
                s.Reliability = p;
                var item = Find(Compose(s), "詳しい SMART 情報");
                Assert.DoesNotContain("管理者として実行", item.Reason);
            }
        }

        [Fact]
        public void SMART詳細が取れたら温度と使用時間を出す()
        {
            var item = Find(Compose(Fixtures.Healthy()), "詳しい SMART 情報");
            Assert.Equal(Status.Info, item.Status);
            Assert.Contains("45℃", item.Value);
            Assert.Contains("3,076 時間", item.Value);
        }

        // ---- Windows 10 のサポート状況 --------------------------------------

        [Fact]
        public void Windows10ならサポート終了の注記を出す()
        {
            var s = Fixtures.Healthy();
            s.Os = ProbeResult<OsInfo>.Ok(new OsInfo
            {
                Caption = "Windows 10 Pro", DisplayVersion = "22H2", Build = 19045, Ubr = 5011,
                Architecture = "64 ビット"
            }, "Win32_OperatingSystem");

            var item = Find(Compose(s), "Windows 10 のサポート状況");
            Assert.Equal(Status.Warn, item.Status);
            Assert.Contains("2025 年 10 月 14 日", item.Value);
            Assert.Contains("ESU", item.Note);
        }

        [Fact]
        public void Windows11でもサポート状況の行は消さない()
        {
            // 行ごと消すと「なぜ無いのか」が読み手に分からない（原則 2 の趣旨）
            var item = Find(Compose(Fixtures.Healthy()), "Windows 10 のサポート状況");
            Assert.NotNull(item);
            Assert.Equal(Status.Info, item.Status);
        }

        [Fact]
        public void ビルド番号が取れなければWindows10かどうかを断定しない()
        {
            var s = Fixtures.Healthy();
            s.Os = ProbeResult<OsInfo>.Ok(new OsInfo { Caption = "Windows", Build = 0 }, "Win32_OperatingSystem");

            var item = Find(Compose(s), "Windows 10 のサポート状況");
            Assert.Equal(Status.Unavailable, item.Status);
        }

        // ---- Windows 11 の条件（判定せず部品を並べる。SPEC §5-1）------------

        [Fact]
        public void 対応CPUかどうかは自前で判定しない()
        {
            var item = Find(Compose(Fixtures.Healthy()), "CPU（Windows 11 の対応 CPU か）");
            Assert.Equal(Status.Info, item.Status);
            Assert.Contains("判定していません", item.Note);
        }

        [Fact]
        public void 旧来のBIOS起動ならセキュアブートは使えないと書く()
        {
            var s = Fixtures.Healthy();
            s.SecureBoot = ProbeResult<SecureBootInfo>.Ok(new SecureBootInfo
            {
                UefiSecureBootEnabled = null, Firmware = FirmwareKind.Bios
            }, "GetFirmwareType");

            var item = Find(Compose(s), "セキュアブート");
            Assert.Equal(Status.Warn, item.Status);
            Assert.Contains("BIOS", item.Value);
        }

        [Fact]
        public void セキュアブートの状態が無ければ取得できませんと書く()
        {
            var s = Fixtures.Healthy();
            s.SecureBoot = ProbeResult<SecureBootInfo>.Ok(new SecureBootInfo
            {
                UefiSecureBootEnabled = null, Firmware = FirmwareKind.Uefi
            }, "GetFirmwareType");

            Assert.Equal(Status.Unavailable, Find(Compose(s), "セキュアブート").Status);
        }
    }
}

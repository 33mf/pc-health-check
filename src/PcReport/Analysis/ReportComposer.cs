using System;
using System.Collections.Generic;
using System.Linq;
using Microforge.PcReport.Model;

namespace Microforge.PcReport.Analysis
{
    /// <summary>
    /// 判定層。SystemSnapshot（生値）と now だけを受け取って Report を組み立てる。
    ///
    /// この class は WMI・レジストリ・DateTime.Now を一切参照しない（AC2）。
    /// したがって、拒否・空・例外のどれを渡しても単体テストで結果を確かめられる（AC3）。
    ///
    /// 貫く原則（SPEC §3 原則 2）:
    ///   取れなかった項目を消さない。§4 の項目は必ず「値」か「取得できませんでした（理由）」で出る。
    /// </summary>
    public static class ReportComposer
    {
        /// <summary>
        /// SPEC §4 の項目が必ずレポートに出ていることを機械的に確かめるための一覧（AC4 の判定に使う）。
        /// ディスクのように台数ぶん行が増えるものは「この文字列で始まる行が 1 つ以上ある」で判定する。
        /// </summary>
        public static readonly string[] RequiredLabelPrefixes =
        {
            "Windows の版",
            "最後に再起動した日",
            "最後に更新プログラムが入った日",
            "Windows 10 のサポート状況",
            "ウイルス対策ソフト",
            "Microsoft Defender の定義ファイル",
            "CPU",
            "メモリ",
            "ディスクの空き",
            "起動ドライブの種類",
            "ディスクの健康状態",
            "詳しい SMART 情報",
            "TPM",
            "セキュアブート"
        };

        const int Win11MinimumBuild = 22000;

        // ---- しきい値（SPEC には数値の定めが無いので、参考実装の暫定値をそのまま採用した）----
        const int RebootWarnDays = 30;
        const int UpdateWarnDays = 45;
        const int UpdateBadDays = 90;
        const int DefenderSignatureWarnDays = 7;
        const double MemoryWarnBytes = 7.5 * 1024 * 1024 * 1024;   // 8 GB 機を拾うため 7.5 GiB で切る
        const double DiskFreeWarnPercent = 20;
        const double DiskFreeBadPercent = 10;

        /// <summary>
        /// 「Windows の更新プログラム」に数えないもの。
        /// ウイルス対策の自動更新は毎日のように入るので、これを数えると
        /// Windows Update が止まっていても「昨日更新された」と見えてしまう（原則 2 の趣旨に反する）。
        ///   KB2267602 = Defender の定義ファイル更新
        ///   KB4052623 = Defender のマルウェア対策プラットフォーム更新
        ///               （gram17 実測 2026-09-21：KB 番号を持つので KB 有無だけでは落とせない）
        /// </summary>
        static readonly string[] NonWindowsUpdateKbs = { "KB2267602", "KB4052623" };

        /// <summary>KB 番号が変わっても拾えるよう、タイトルでも落とす。</summary>
        static readonly string[] NonWindowsUpdateTitleMarkers =
        {
            "Defender",
            "Security Intelligence",
            "セキュリティ インテリジェンス",
            "マルウェア対策プラットフォーム",
            "Antimalware Platform"
        };

        public static Report Compose(SystemSnapshot s, DateTime now, string toolVersion)
        {
            if (s == null) throw new ArgumentNullException("s");

            var r = new Report
            {
                GeneratedAt = now,
                ToolVersion = toolVersion,
                RanAsAdmin = s.RanAsAdmin
            };

            r.Sections.Add(WindowsSection(s, now));
            r.Sections.Add(SecuritySection(s, now));
            r.Sections.Add(HardwareSection(s));
            r.Sections.Add(DiskHealthSection(s));
            r.Sections.Add(Win11Section(s));
            return r;
        }

        // ------------------------------------------------------------------
        // 共通：ProbeResult を必ず 1 行に落とす。ここが「項目が消えない」保証の要。
        // ------------------------------------------------------------------

        static Item Resolve<T>(string label, ProbeResult<T> p, Func<T, Item> onOk)
        {
            if (p == null)
                return Item.Unavailable(label, "収集が行われませんでした", null);

            if (!p.IsOk)
                return Item.Unavailable(label, p.Reason, p.Source);

            Item i;
            try
            {
                i = onOk(p.Value);
            }
            catch (Exception e)
            {
                // 判定中の想定外も「黙って消える」より理由を出すほうがよい
                return Item.Unavailable(label, "内容を読み取れませんでした（" + e.GetType().Name + "）", p.Source);
            }

            if (i == null) return Item.Unavailable(label, "情報源に値がありませんでした", p.Source);
            if (i.Label == null) i.Label = label;
            if (i.Source == null) i.Source = p.Source;
            if (i.Status == Status.Unavailable && string.IsNullOrEmpty(i.Reason))
                i.Reason = "情報源に値がありませんでした";
            return i;
        }

        // ------------------------------------------------------------------
        // Windows
        // ------------------------------------------------------------------

        static Section WindowsSection(SystemSnapshot s, DateTime now)
        {
            var sec = new Section("Windows", "OS の版と、更新がちゃんと届いているか。");

            sec.Add(Resolve("Windows の版", s.Os, os =>
            {
                if (string.IsNullOrEmpty(os.Caption) && os.Build <= 0) return null;
                var v = string.IsNullOrEmpty(os.Caption) ? "Windows" : os.Caption;
                if (!string.IsNullOrEmpty(os.DisplayVersion)) v += "　バージョン " + os.DisplayVersion;
                v += "（ビルド " + os.Build + (os.Ubr.HasValue ? "." + os.Ubr.Value : "") + "）";
                if (!string.IsNullOrEmpty(os.Architecture)) v += "　" + os.Architecture;
                return new Item { Value = v, Status = Status.Info };
            }));

            sec.Add(Resolve("最後に再起動した日", s.LastBootUpTime, boot =>
            {
                var i = new Item { Value = Formatting.DateWithAgo(boot, now), Status = Status.Ok };
                if (Formatting.DaysAgo(boot, now) >= RebootWarnDays)
                {
                    i.Status = Status.Warn;
                    i.Note = "長く再起動していません。更新の適用が保留になっていることがあります。"
                           + "一度「再起動」してください（「シャットダウン」では高速スタートアップのため"
                           + "数えなおしにならないことがあります）。";
                }
                return i;
            }));

            sec.Add(LastUpdateItem(s, now));
            sec.Add(Windows10SupportItem(s));
            return sec;
        }

        /// <summary>
        /// 最後に更新プログラムが入った日。
        /// SPEC §7-1 ② の実測により、WUA 履歴には Store アプリの更新が混ざる。そこで
        ///   主  = QFE（Win32_QuickFixEngineering）
        ///   補助 = WUA のうち KB 番号を持ち、Defender 定義更新（KB2267602）でないもの
        /// を別々に出し、新しい方を採る。どちらが「更新の履歴」画面と一致するかは AC9 で確定する。
        /// </summary>
        static Item LastUpdateItem(SystemSnapshot s, DateTime now)
        {
            const string label = "最後に更新プログラムが入った日";

            return Resolve(label, s.Updates, u =>
            {
                var qfe = Newest(FilterWindowsUpdates(u.Qfe));
                var wua = Newest(FilterWindowsUpdates(u.Wua));

                var chosen = Newer(qfe, wua);
                if (chosen == null)
                {
                    var reasons = new List<string>();
                    reasons.Add("インストール済み更新: "
                        + (string.IsNullOrEmpty(u.QfeReason) ? "Windows の更新プログラムが見つかりませんでした" : u.QfeReason));
                    reasons.Add("更新の履歴: "
                        + (string.IsNullOrEmpty(u.WuaReason) ? "Windows の更新プログラムが見つかりませんでした" : u.WuaReason));
                    return Item.Unavailable(label, string.Join(" / ", reasons));
                }

                var i = new Item
                {
                    Value = Formatting.DateWithAgo(chosen.Date, now),
                    Status = Status.Ok,
                    // AC9 で「更新の履歴」画面と突き合わせられるよう、両方の取得元を並べて残す
                    Source = "インストール済み更新（Win32_QuickFixEngineering）＝" + Describe(qfe, now)
                           + "　／　更新の履歴（Windows Update）＝" + Describe(wua, now)
                };
                if (!string.IsNullOrEmpty(chosen.Title))
                    i.Value += "　" + Formatting.Shorten(chosen.Title, 70);

                int n = Formatting.DaysAgo(chosen.Date, now);
                if (n >= UpdateBadDays)
                {
                    i.Status = Status.Bad;
                    i.Note = "3 か月以上、更新が入っていません。Windows Update が止まっている可能性があります。";
                }
                else if (n >= UpdateWarnDays)
                {
                    i.Status = Status.Warn;
                    i.Note = "1 か月半以上、更新が入っていません。「設定 → Windows Update」で更新を確認してください。";
                }
                return i;
            });
        }

        /// <summary>
        /// WUA 履歴から「Windows の更新プログラム」だけを残す。
        ///   - KB 番号を持たないもの＝Store アプリの更新（SPEC §7-1 ②）を落とす
        ///   - Defender の定義／プラットフォーム更新を落とす
        /// </summary>
        public static List<UpdateEntry> FilterWindowsUpdates(IEnumerable<UpdateEntry> entries)
        {
            if (entries == null) return new List<UpdateEntry>();
            return entries.Where(e => e != null && IsWindowsUpdate(e)).ToList();
        }

        static bool IsWindowsUpdate(UpdateEntry e)
        {
            // Store アプリの更新は KB 番号を持たない
            if (string.IsNullOrEmpty(e.KbId)) return false;

            foreach (var kb in NonWindowsUpdateKbs)
                if (string.Equals(e.KbId, kb, StringComparison.OrdinalIgnoreCase)) return false;

            if (!string.IsNullOrEmpty(e.Title))
                foreach (var marker in NonWindowsUpdateTitleMarkers)
                    if (e.Title.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) return false;

            return true;
        }

        static UpdateEntry Newest(IEnumerable<UpdateEntry> entries)
        {
            if (entries == null) return null;
            UpdateEntry best = null;
            foreach (var e in entries)
            {
                if (e == null) continue;
                if (best == null || e.Date > best.Date) best = e;
            }
            return best;
        }

        static UpdateEntry Newer(UpdateEntry a, UpdateEntry b)
        {
            if (a == null) return b;
            if (b == null) return a;
            return a.Date >= b.Date ? a : b;
        }

        static string Describe(UpdateEntry e, DateTime now)
        {
            if (e == null) return "該当なし";
            return Formatting.Date(e.Date) + (string.IsNullOrEmpty(e.Title) ? "" : " " + Formatting.Shorten(e.Title, 60));
        }

        /// <summary>
        /// Windows 10 のサポート状況（SPEC §4 必須）。
        /// Windows 11 のときも「該当しない」と書いて行を残す＝見る人が「項目が消えた」と迷わないため。
        /// </summary>
        static Item Windows10SupportItem(SystemSnapshot s)
        {
            const string label = "Windows 10 のサポート状況";

            return Resolve(label, s.Os, os =>
            {
                if (os.Build <= 0)
                    return Item.Unavailable(label, "ビルド番号が取得できず、Windows 10 かどうかを判定できませんでした");

                if (os.Build >= Win11MinimumBuild)
                    return new Item
                    {
                        Value = "この PC は Windows 11 です（Windows 10 のサポート終了は当てはまりません）",
                        Status = Status.Info,
                        Source = "ビルド番号 " + os.Build + " ≧ " + Win11MinimumBuild
                    };

                return new Item
                {
                    Value = "Windows 10 は 2025 年 10 月 14 日にサポートが終了しています",
                    Status = Status.Warn,
                    Note = "延長セキュリティ更新（ESU）に登録していなければ、2025 年 10 月以降は更新が止まっています"
                         + "（ESU は 2027 年 10 月 12 日まで）。ESU に登録しているかどうかは、このツールでは判定していません。",
                    Source = "ビルド番号 " + os.Build + " < " + Win11MinimumBuild
                };
            });
        }

        // ------------------------------------------------------------------
        // ウイルス対策
        // ------------------------------------------------------------------

        /// <summary>
        /// SecurityCenter2 の productState の解釈。非公式だが広く使われている解釈で、
        /// SPEC §7-1 ① の実測（ウイルスバスター 0x041000＝有効、Defender 0x060100＝受動）とも一致する。
        /// </summary>
        public static bool IsAntiVirusEnabled(uint productState)
        {
            uint b = (productState >> 8) & 0xFF;
            return b == 0x10 || b == 0x11;
        }

        /// <summary>定義ファイルが最新か（下位バイト 0x00＝最新、0x10＝古い）。</summary>
        public static bool IsAntiVirusUpToDate(uint productState)
        {
            return (productState & 0xFF) == 0x00;
        }

        /// <summary>
        /// Defender 以外のウイルス対策ソフトが有効か。
        /// 有効なら Defender は受動モードに入り、リアルタイム保護がオフなのは正常な状態になる。
        /// この判定をせずに「リアルタイム保護がオフ」を警告すると誤報になる
        /// （gram17 実測 2026-09-21：ウイルスバスター有効・Defender RTP オフ）。
        /// </summary>
        public static bool HasActiveThirdPartyAntiVirus(IEnumerable<AntiVirusProduct> products)
        {
            if (products == null) return false;
            return products.Any(p => p != null
                                     && !string.IsNullOrEmpty(p.DisplayName)
                                     && p.DisplayName.IndexOf("Defender", StringComparison.OrdinalIgnoreCase) < 0
                                     && IsAntiVirusEnabled(p.ProductState));
        }

        static Section SecuritySection(SystemSnapshot s, DateTime now)
        {
            var sec = new Section("ウイルス対策", "有効になっているか、定義ファイルが新しいか。");

            bool thirdPartyActive = s.AntiVirus != null && s.AntiVirus.IsOk
                                    && HasActiveThirdPartyAntiVirus(s.AntiVirus.Value);

            if (s.AntiVirus == null || !s.AntiVirus.IsOk)
            {
                sec.Add(Item.Unavailable("ウイルス対策ソフト",
                    s.AntiVirus == null ? "収集が行われませんでした" : s.AntiVirus.Reason,
                    s.AntiVirus == null ? null : s.AntiVirus.Source));
            }
            else
            {
                var rows = new List<Item>();
                foreach (var av in s.AntiVirus.Value)
                {
                    bool enabled = IsAntiVirusEnabled(av.ProductState);
                    bool upToDate = IsAntiVirusUpToDate(av.ProductState);
                    rows.Add(new Item
                    {
                        Label = "ウイルス対策ソフト",
                        Value = av.DisplayName + "　" + (enabled ? "有効" : "無効")
                              + "／定義ファイル" + (upToDate ? "は最新" : "が古い"),
                        Status = !enabled ? Status.Bad : (!upToDate ? Status.Warn : Status.Ok),
                        Source = s.AntiVirus.Source + " (productState=0x" + av.ProductState.ToString("X6") + ")"
                    });
                }

                // 有効なものが 1 つでもあれば、無効な製品（他社 AV 使用時の Defender や入れ替えの残骸）は Info に下げる
                bool anyEnabled = rows.Any(p => p.Status != Status.Bad);
                foreach (var p in rows)
                {
                    if (anyEnabled && p.Status == Status.Bad)
                    {
                        p.Status = Status.Info;
                        p.Note = "このソフトは無効ですが、別のソフトが有効なので問題ありません。";
                    }
                    sec.Add(p);
                }
                if (!anyEnabled && rows.Count > 0)
                    rows[rows.Count - 1].Note = "有効なウイルス対策ソフトが見つかりません。Windows セキュリティを開いて確認してください。";
            }

            sec.Add(Resolve("Microsoft Defender の定義ファイル", s.Defender, d =>
            {
                if (!d.SignatureLastUpdated.HasValue)
                    return Item.Unavailable("Microsoft Defender の定義ファイル", "定義ファイルの日付が報告されていません");

                var updated = d.SignatureLastUpdated.Value;

                // 他社のウイルス対策ソフトが有効なら、Defender は受動モード＝これが正常な状態
                if (!d.AntivirusEnabled || thirdPartyActive)
                    return new Item
                    {
                        Value = "Defender は待機中（他のウイルス対策ソフトが動いているため。正常です）　最終更新 "
                              + Formatting.DateWithAgo(updated, now),
                        Status = Status.Info
                    };

                var i = new Item
                {
                    Value = Formatting.DateWithAgo(updated, now)
                          + (string.IsNullOrEmpty(d.SignatureVersion) ? "" : "　版 " + d.SignatureVersion),
                    Status = Status.Ok
                };
                if (Formatting.DaysAgo(updated, now) >= DefenderSignatureWarnDays)
                {
                    i.Status = Status.Warn;
                    i.Note = "定義ファイルが 1 週間以上更新されていません。";
                }
                if (!d.RealTimeProtectionEnabled)
                {
                    i.Status = Status.Bad;
                    i.Note = "リアルタイム保護がオフになっています。他にウイルス対策ソフトが入っていないので、"
                           + "この PC はいま無防備です。「Windows セキュリティ」を開いて有効にしてください。";
                }
                return i;
            }));

            return sec;
        }

        // ------------------------------------------------------------------
        // CPU・メモリ・ディスク
        // ------------------------------------------------------------------

        static Section HardwareSection(SystemSnapshot s)
        {
            var sec = new Section("CPU・メモリ・ディスク", "「遅い」の原因がここに出ることが多い。");

            sec.Add(Resolve("CPU", s.Cpu, c =>
            {
                if (string.IsNullOrEmpty(c.Name)) return null;
                var v = c.Name;
                if (c.Cores > 0 && c.LogicalProcessors > 0)
                    v += "（" + c.Cores + " コア / " + c.LogicalProcessors + " スレッド）";
                return new Item { Value = v, Status = Status.Info };
            }));

            sec.Add(Resolve("メモリ", s.Memory, m =>
            {
                var i = new Item
                {
                    Value = Formatting.Gb(m.TotalBytes) + "（いま空き " + Formatting.Gb(m.FreeBytes) + "）",
                    Status = Status.Ok
                };
                if (m.TotalBytes < MemoryWarnBytes)
                {
                    i.Status = Status.Warn;
                    i.Note = "今の Windows には少なめです。ブラウザでタブを多く開くと遅くなりやすい容量です。";
                }
                return i;
            }));

            sec.Add(BootDriveKindItem(s));

            if (s.LogicalDisks == null || !s.LogicalDisks.IsOk)
            {
                sec.Add(Item.Unavailable("ディスクの空き",
                    s.LogicalDisks == null ? "収集が行われませんでした" : s.LogicalDisks.Reason,
                    s.LogicalDisks == null ? null : s.LogicalDisks.Source));
            }
            else
            {
                foreach (var d in s.LogicalDisks.Value)
                {
                    double pct = d.SizeBytes <= 0 ? 0 : (double)d.FreeBytes / d.SizeBytes * 100;
                    var i = new Item
                    {
                        Label = "ディスクの空き " + d.DeviceId + (d.IsSystemDrive ? "（起動ドライブ）" : ""),
                        Value = Formatting.Gb(d.FreeBytes) + " 空き / " + Formatting.Gb(d.SizeBytes)
                              + "（" + Formatting.Percent(pct) + "）",
                        Status = Status.Ok,
                        Source = s.LogicalDisks.Source
                    };
                    if (pct < DiskFreeBadPercent)
                    {
                        i.Status = Status.Bad;
                        i.Note = "空きがほとんどありません。更新が入らない・動作が極端に遅くなる原因になります。";
                    }
                    else if (pct < DiskFreeWarnPercent)
                    {
                        i.Status = Status.Warn;
                        i.Note = "空きが少なくなっています。";
                    }
                    sec.Add(i);
                }
            }

            return sec;
        }

        /// <summary>MediaType / BusType の生値から SSD か HDD かを決める（SPEC §4 ★ / §7-3）。</summary>
        public static string ClassifyDriveKind(int mediaType, int busType, string friendlyName)
        {
            // MediaType: 3=HDD 4=SSD 0=不明 / BusType: 17=NVMe（NVMe なら SSD 確定）
            if (mediaType == 4 || busType == 17) return "SSD";
            if (mediaType == 3) return "HDD";

            var up = (friendlyName ?? "").ToUpperInvariant();
            if (up.Contains("SSD") || up.Contains("NVME")) return "SSD（型番から推定）";
            return null;
        }

        static string BusName(int busType)
        {
            switch (busType)
            {
                case 17: return "NVMe";
                case 11: return "SATA";
                case 7: return "USB";
                case 8: return "RAID";
                default: return null;
            }
        }

        static Item BootDriveKindItem(SystemSnapshot s)
        {
            const string label = "起動ドライブの種類";

            return Resolve(label, s.BootDisk, d =>
            {
                var kind = ClassifyDriveKind(d.MediaType, d.BusType, d.FriendlyName);
                var bus = BusName(d.BusType);
                var source = s.BootDisk.Source + " (MediaType=" + d.MediaType + ", BusType=" + d.BusType + ")";

                if (kind == null)
                    return Item.Unavailable(label,
                        "ディスクが種類を報告していません（仮想マシンや一部の古い機器で起こります）", source);

                var i = new Item
                {
                    Value = kind + "　" + d.FriendlyName + (bus != null ? "（" + bus + "）" : ""),
                    Source = source,
                    Status = Status.Ok
                };
                if (kind == "HDD")
                {
                    i.Status = Status.Warn;
                    i.Note = "起動ドライブが HDD です。古い PC が「遅い」いちばん多い原因で、"
                           + "SSD に換装すると体感が大きく変わります。";
                }
                return i;
            });
        }

        // ------------------------------------------------------------------
        // ディスクの健康状態
        // ------------------------------------------------------------------

        static Section DiskHealthSection(SystemSnapshot s)
        {
            var sec = new Section("ディスクの健康状態",
                "Windows が把握している範囲の判定。温度や使用時間などの詳しい値は管理者権限が必要。");

            if (s.PhysicalDisks == null || !s.PhysicalDisks.IsOk)
            {
                sec.Add(Item.Unavailable("ディスクの健康状態",
                    s.PhysicalDisks == null ? "収集が行われませんでした" : s.PhysicalDisks.Reason,
                    s.PhysicalDisks == null ? null : s.PhysicalDisks.Source));
            }
            else
            {
                foreach (var d in s.PhysicalDisks.Value)
                {
                    var label = "ディスクの健康状態"
                              + (string.IsNullOrEmpty(d.FriendlyName) ? "" : "（" + d.FriendlyName + "）");
                    var i = new Item
                    {
                        Label = label,
                        Source = s.PhysicalDisks.Source + " HealthStatus=" + d.HealthStatus
                    };
                    switch (d.HealthStatus)
                    {
                        case 0:
                            i.Value = "正常";
                            i.Status = Status.Ok;
                            break;
                        case 1:
                            i.Value = "警告";
                            i.Status = Status.Warn;
                            i.Note = "Windows がこのディスクに警告を出しています。大事なデータのバックアップを先に取ってください。";
                            break;
                        case 2:
                            i.Value = "異常";
                            i.Status = Status.Bad;
                            i.Note = "Windows がこのディスクを異常と判定しています。すぐにバックアップを取り、交換を検討してください。";
                            break;
                        default:
                            i.Status = Status.Unavailable;
                            i.Reason = "ディスクが健康状態を報告していません";
                            break;
                    }
                    sec.Add(i);
                }
            }

            sec.Add(SmartDetailItem(s));
            return sec;
        }

        /// <summary>
        /// 詳しい SMART 情報。
        /// SPEC §7-1 ③⑪ の実測：関連付けで取ると、非昇格では「拒否」ではなく「空」が返り、昇格すると値が取れた。
        /// ただし昇格しても空の機種・VM がありうるので、取れないときの理由を権限で分ける（R-Evidence-5）。
        /// 「管理者として実行すると取得できることがあります」は、非昇格のときにしか出さない
        /// （昇格しているのに出すと、やっても取れない案内になる。原則 2）。
        /// </summary>
        static Item SmartDetailItem(SystemSnapshot s)
        {
            const string label = "詳しい SMART 情報（温度・使用時間など）";
            var p = s.Reliability;

            if (p == null) return Item.Unavailable(label, "収集が行われませんでした", null);

            if (p.Outcome == ProbeOutcome.Denied && s.RanAsAdmin)
                return Item.Unavailable(label, "管理者権限で実行していても、読み取りを拒否されました", p.Source);

            if (p.Outcome == ProbeOutcome.Empty)
                return Item.Unavailable(label,
                    s.RanAsAdmin
                        ? "この PC のディスクは詳細情報を報告していません"
                        : "管理者権限が必要です（「管理者として実行」すると取得できることがあります）",
                    p.Source);

            return Resolve(label, p, counters =>
            {
                var rows = new List<string>();
                foreach (var c in counters)
                {
                    var parts = new List<string>();
                    if (c.Temperature.HasValue && c.Temperature.Value > 0)
                        parts.Add("温度 " + c.Temperature.Value + "℃");
                    if (c.PowerOnHours.HasValue && c.PowerOnHours.Value > 0)
                        parts.Add("使用時間 " + Formatting.Thousands(c.PowerOnHours.Value) + " 時間");
                    if (c.Wear.HasValue && c.Wear.Value > 0)
                        parts.Add("消耗 " + c.Wear.Value + "%");
                    if (parts.Count > 0) rows.Add(string.Join("、", parts));
                }
                if (rows.Count == 0) return null;
                return new Item { Value = string.Join(" ／ ", rows), Status = Status.Info };
            });
        }

        // ------------------------------------------------------------------
        // Windows 11 の条件（判定ではなく 3 部品の状態を並べる。SPEC §5-1）
        // ------------------------------------------------------------------

        static Section Win11Section(SystemSnapshot s)
        {
            bool isWin11 = s.Os != null && s.Os.IsOk && s.Os.Value.Build >= Win11MinimumBuild;

            var sec = new Section("Windows 11 に上げるための条件",
                isWin11
                    ? "この PC はすでに Windows 11 です（参考情報）。"
                    : "条件は TPM 2.0・対応 CPU・セキュアブートの 3 つ。このツールは「上げられる／上げられない」の"
                      + "判定はせず、部品ごとの状態を並べます。");

            sec.Add(Resolve("TPM", s.Tpm, t =>
            {
                var spec = t.SpecVersion ?? "";
                var ver = spec.Split(',')[0].Trim();
                if (ver.Length == 0)
                    return Item.Unavailable("TPM", "TPM の版が報告されていません");
                return new Item
                {
                    Value = "TPM " + ver + (t.IsEnabled ? "（有効）" : "（無効）"),
                    Status = ver.StartsWith("2") && t.IsEnabled ? Status.Ok : Status.Warn
                };
            }));

            sec.Add(Resolve("セキュアブート", s.SecureBoot, sb =>
            {
                if (sb.Firmware == FirmwareKind.Bios)
                    return new Item
                    {
                        Value = "旧来の BIOS モードで起動しています（セキュアブートは使えません）",
                        Status = Status.Warn,
                        Note = "UEFI モードへの切り替えが必要です。"
                    };

                if (!sb.UefiSecureBootEnabled.HasValue)
                    return Item.Unavailable("セキュアブート",
                        sb.Firmware == FirmwareKind.Unknown
                            ? "ファームウェアの種別もセキュアブートの状態も報告されていません"
                            : "セキュアブートの状態が報告されていません");

                return sb.UefiSecureBootEnabled.Value == 1
                    ? new Item { Value = "有効（UEFI）", Status = Status.Ok }
                    : new Item
                    {
                        Value = "無効（UEFI 対応。設定で有効にできる可能性があります）",
                        Status = Status.Warn
                    };
            }));

            sec.Add(Resolve("CPU（Windows 11 の対応 CPU か）", s.Cpu, c => new Item
            {
                Value = string.IsNullOrEmpty(c.Name) ? "（上の CPU 欄を参照）" : c.Name,
                Status = Status.Info,
                Note = "対応 CPU かどうかは Microsoft の一覧で決まり、一覧は更新されるため、"
                     + "このツールでは判定していません。"
            }));

            // 参考情報。取れなければ行そのものを出さない（§4 の必須項目ではないため）
            if (s.UpgradeIndicators != null && s.UpgradeIndicators.IsOk && s.UpgradeIndicators.Value.Count > 0)
            {
                var rows = s.UpgradeIndicators.Value.Select(u =>
                {
                    var verdict = u.UpgEx == "Green" ? "可" : u.UpgEx == "Red" ? "不可" : u.UpgEx;
                    var reason = string.IsNullOrEmpty(u.RedReason) || u.RedReason == "None"
                        ? "" : "（" + u.RedReason + "）";
                    return u.KeyName + "：" + verdict + reason;
                });
                sec.Add(new Item
                {
                    Label = "Windows 自身の判定記録（参考）",
                    Value = string.Join("　", rows),
                    Status = Status.Info,
                    Note = "Windows が過去に行ったアップグレード可否チェックの記録です。"
                         + "記録した時点の判定で、最新とは限りません。",
                    Source = s.UpgradeIndicators.Source
                });
            }

            return sec;
        }
    }
}

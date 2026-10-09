using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microforge.PcReport.Rendering;
using Microforge.PcReport.Saving;
using Xunit;

namespace Microforge.PcReport.Tests
{
    /// <summary>
    /// PCR-003・PCR-004：保存先の退避（SPEC §9-2・§9-5・§9-6）。
    /// 素の exe は 5 段、パッケージ実行（MSIX / Store 版）と判定できないときは %LOCALAPPDATA% を外した 4 段。
    /// ウイルス対策ソフトのフォルダ保護に止められたら次の候補へ退避し、書けた場所に保存する（どこにも書けなければ ReportSaveException）。
    /// 書き込み処理を差し替えて「拒否」を再現する（実機のフォルダ保護は再現できないため）。
    /// </summary>
    public class SaveLocationTests
    {
        const string Html = "<html>report</html>";
        const string FileName = "pc-report_2026-09-23_1542.html";

        const string ExeDir = @"C:\Tools\PcReport";
        const string DesktopDir = @"C:\Users\someone\Desktop";
        const string DownloadsDir = @"C:\Users\someone\Downloads";
        const string AppDataDir = @"C:\Users\someone\AppData\Local\Microforge\PcReport";
        const string TempDir = @"C:\Users\someone\AppData\Local\Temp";

        /// <summary>製品と同じ組み立て（BuildCandidates）で、パスだけ差し替えた候補。</summary>
        static IList<SaveCandidate> Candidates(PackageState package = PackageState.NotPackaged)
        {
            return ReportSaver.BuildCandidates(package, ExeDir, DesktopDir, DownloadsDir, AppDataDir, TempDir);
        }

        /// <summary>指定した場所への書き込みだけを拒否する（フォルダシールドの再現）。書けた先を記録する。</summary>
        sealed class FakeDisk
        {
            readonly HashSet<string> denied;
            readonly Exception error;
            public readonly List<string> Attempts = new List<string>();
            public readonly Dictionary<string, string> Written = new Dictionary<string, string>();

            public FakeDisk(Exception error, params string[] deniedDirs)
            {
                this.error = error;
                denied = new HashSet<string>(deniedDirs, StringComparer.OrdinalIgnoreCase);
            }

            public void Write(string path, string html)
            {
                Attempts.Add(path);
                if (denied.Contains(Path.GetDirectoryName(path))) throw error;
                Written[path] = html;
            }
        }

        static readonly Dictionary<SaveLocation, string> Dirs = new Dictionary<SaveLocation, string>
        {
            { SaveLocation.ExeFolder, ExeDir }, { SaveLocation.Desktop, DesktopDir }, { SaveLocation.Downloads, DownloadsDir },
            { SaveLocation.AppData, AppDataDir }, { SaveLocation.Temp, TempDir }
        };

        static FakeDisk Deny(params SaveLocation[] locations)
        {
            return new FakeDisk(new UnauthorizedAccessException("Access to the path is denied."),
                locations.Select(l => Dirs[l]).ToArray());
        }

        // ---- 候補の並び（SPEC §9-6） -------------------------------------------

        [Fact]
        public void 素のexeでは5段でこの順に並ぶ()
        {
            Assert.Equal(
                new[] { SaveLocation.ExeFolder, SaveLocation.Desktop, SaveLocation.Downloads, SaveLocation.AppData, SaveLocation.Temp },
                Candidates(PackageState.NotPackaged).Select(x => x.Location).ToArray());
        }

        [Theory]
        [InlineData(PackageState.Packaged)]
        [InlineData(PackageState.Unknown)]
        public void パッケージ実行か判定できないときはLOCALAPPDATAを候補から外す(PackageState state)
        {
            // MSIX のアプリが %LOCALAPPDATA% に書くと、パッケージ専用の場所に振り替えられて利用者から見えなくなる
            var c = Candidates(state);
            Assert.Equal(
                new[] { SaveLocation.ExeFolder, SaveLocation.Desktop, SaveLocation.Downloads, SaveLocation.Temp },
                c.Select(x => x.Location).ToArray());
            Assert.DoesNotContain(c, x => x.Directory == AppDataDir);
        }

        [Fact]
        public void 既定の候補は実行環境から組み立てる()
        {
            // テストは素の exe（テストホスト）として動くので、5 段になる
            var c = ReportSaver.DefaultCandidates();
            Assert.Equal(PackageState.NotPackaged, PackageContext.Detect());
            Assert.Equal(
                new[] { SaveLocation.ExeFolder, SaveLocation.Desktop, SaveLocation.Downloads, SaveLocation.AppData, SaveLocation.Temp },
                c.Select(x => x.Location).ToArray());
            Assert.EndsWith(@"\Microforge\PcReport", c[3].Directory);
            // ダウンロード フォルダは、取れなければ null（候補から外れる）。取れたなら絶対パス。
            // 存在までは確かめない（プロファイルを作りたての環境などでは無いことがあり、それは製品の正しい動き）
            Assert.True(c[2].Directory == null || Path.IsPathRooted(c[2].Directory),
                "ダウンロード フォルダの値がおかしい: " + c[2].Directory);
        }

        // ---- 素の exe：退避の順 -------------------------------------------------

        [Fact]
        public void 書ければexeと同じ場所に保存し退避扱いにしない()
        {
            var disk = Deny();
            var r = ReportSaver.Save(Html, FileName, Candidates(), disk.Write);

            Assert.Equal(SaveLocation.ExeFolder, r.Location);
            Assert.False(r.IsFallback);
            Assert.Equal(ExeDir + @"\" + FileName, r.Path);
            Assert.Single(disk.Attempts);
        }

        [Fact]
        public void exeの場所が拒否されたらデスクトップに保存する()
        {
            var disk = Deny(SaveLocation.ExeFolder);
            var r = ReportSaver.Save(Html, FileName, Candidates(), disk.Write);

            Assert.Equal(SaveLocation.Desktop, r.Location);
            Assert.True(r.IsFallback);
            Assert.Equal(Html, disk.Written[r.Path]);
        }

        [Fact]
        public void exeの場所とデスクトップが拒否されたらダウンロードに保存する()
        {
            var disk = Deny(SaveLocation.ExeFolder, SaveLocation.Desktop);
            var r = ReportSaver.Save(Html, FileName, Candidates(), disk.Write);

            Assert.Equal(SaveLocation.Downloads, r.Location);
            Assert.True(r.IsFallback);
            Assert.Equal(DownloadsDir + @"\" + FileName, r.Path);
        }

        [Fact]
        public void ダウンロードまで拒否されたらアプリの保存先に保存する()
        {
            var disk = Deny(SaveLocation.ExeFolder, SaveLocation.Desktop, SaveLocation.Downloads);
            var r = ReportSaver.Save(Html, FileName, Candidates(), disk.Write);

            Assert.Equal(SaveLocation.AppData, r.Location);
            Assert.Equal(AppDataDir + @"\" + FileName, r.Path);
        }

        [Fact]
        public void 四つとも拒否されたら一時フォルダに保存する()
        {
            var disk = Deny(SaveLocation.ExeFolder, SaveLocation.Desktop, SaveLocation.Downloads, SaveLocation.AppData);
            var r = ReportSaver.Save(Html, FileName, Candidates(), disk.Write);

            Assert.Equal(SaveLocation.Temp, r.Location);
            Assert.True(r.IsFallback);
            Assert.Equal(5, disk.Attempts.Count);
        }

        // ---- パッケージ実行（MSIX / Store 版）：退避の順 --------------------------

        [Fact]
        public void パッケージ実行ではインストール先が拒否されデスクトップに保存する()
        {
            // インストール先（WindowsApps）は読み取り専用。拒否されたら黙って次へ進む
            var disk = Deny(SaveLocation.ExeFolder);
            var r = ReportSaver.Save(Html, FileName, Candidates(PackageState.Packaged), disk.Write);

            Assert.Equal(SaveLocation.Desktop, r.Location);
        }

        [Fact]
        public void パッケージ実行でデスクトップも拒否されたらダウンロードに保存する()
        {
            var disk = Deny(SaveLocation.ExeFolder, SaveLocation.Desktop);
            var r = ReportSaver.Save(Html, FileName, Candidates(PackageState.Packaged), disk.Write);

            Assert.Equal(SaveLocation.Downloads, r.Location);
        }

        [Fact]
        public void パッケージ実行ではダウンロードの次はLOCALAPPDATAを飛ばして一時フォルダに保存する()
        {
            var disk = Deny(SaveLocation.ExeFolder, SaveLocation.Desktop, SaveLocation.Downloads);
            var r = ReportSaver.Save(Html, FileName, Candidates(PackageState.Packaged), disk.Write);

            Assert.Equal(SaveLocation.Temp, r.Location);
            Assert.DoesNotContain(disk.Attempts, p => p.StartsWith(AppDataDir, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(4, disk.Attempts.Count);
        }

        [Theory]
        [InlineData(PackageState.NotPackaged)]
        [InlineData(PackageState.Packaged)]
        [InlineData(PackageState.Unknown)]
        public void 全部拒否されたらフォルダ保護の可能性と次の一手を伝える(PackageState state)
        {
            var disk = Deny(SaveLocation.ExeFolder, SaveLocation.Desktop, SaveLocation.Downloads, SaveLocation.AppData, SaveLocation.Temp);

            var e = Assert.Throws<ReportSaveException>(
                () => ReportSaver.Save(Html, FileName, Candidates(state), disk.Write));

            Assert.Contains("レポートを保存できませんでした", e.Message);
            Assert.Contains("フォルダ保護", e.Message);
            Assert.Contains("別のフォルダ", e.Message);
            Assert.Contains("許可", e.Message);
            Assert.IsType<UnauthorizedAccessException>(e.InnerException);
            Assert.Empty(disk.Written);
            // 利用者に見せる文言に、試した場所のフルパス（ユーザー名を含む）を出さない
            foreach (var d in Dirs.Values) Assert.DoesNotContain(d, e.Message);
            Assert.DoesNotContain("someone", e.Message);
        }

        // ---- 例外の扱い ---------------------------------------------------------

        [Fact]
        public void IOExceptionでも次の候補へ進む()
        {
            // フォルダ保護・ディスク満杯・パスが無い等は IOException 系で来ることがある
            var disk = new FakeDisk(new IOException("The process cannot access the file."), ExeDir, DesktopDir);

            var r = ReportSaver.Save(Html, FileName, Candidates(), disk.Write);
            Assert.Equal(SaveLocation.Downloads, r.Location);
        }

        [Fact]
        public void 書き込みの拒否以外の例外は退避せずにそのまま投げる()
        {
            // 退避しても直らない失敗（引数の誤りなど）を「保存先が無い」と取り違えない
            var disk = new FakeDisk(new ArgumentException("bad path"), ExeDir);

            Assert.Throws<ArgumentException>(() => ReportSaver.Save(Html, FileName, Candidates(), disk.Write));
            Assert.Single(disk.Attempts);
        }

        [Fact]
        public void 場所が取れない候補は飛ばす()
        {
            // デスクトップが無いアカウント（サービス等）では GetFolderPath が空を返す。ダウンロードも取れないことがある
            var candidates = ReportSaver.BuildCandidates(PackageState.NotPackaged, ExeDir, "", null, AppDataDir, TempDir);
            var disk = Deny(SaveLocation.ExeFolder);

            var r = ReportSaver.Save(Html, FileName, candidates, disk.Write);
            Assert.Equal(SaveLocation.AppData, r.Location);
            Assert.Equal(2, disk.Attempts.Count);
        }

        [Fact]
        public void 指定された場所は退避扱いにしない()
        {
            var r = ReportSaver.Save(Html, FileName,
                new[] { new SaveCandidate(SaveLocation.Specified, @"D:\out") }, Deny().Write);
            Assert.False(r.IsFallback);
        }

        [Theory]
        [InlineData(SaveLocation.Desktop, "デスクトップ")]
        [InlineData(SaveLocation.Downloads, "ダウンロード フォルダ")]
        [InlineData(SaveLocation.AppData, "このアプリの保存先")]
        [InlineData(SaveLocation.Temp, "一時フォルダ")]
        public void 退避先は呼び名だけで伝える(SaveLocation location, string expected)
        {
            var name = ReportSaver.DisplayName(location);
            Assert.Equal(expected, name);
            Assert.DoesNotContain(@"\", name);
            Assert.DoesNotContain(":", name);
        }


        [Fact]
        public void 実際のファイル書き込みでフォルダが無ければ作ってBOM付きUTF8で書く()
        {
            var root = Path.Combine(Path.GetTempPath(), "PcReportTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                var dir = Path.Combine(root, "Microforge", "PcReport");
                var r = ReportSaver.Save("日本語", FileName, new[] { new SaveCandidate(SaveLocation.AppData, dir) });

                var bytes = File.ReadAllBytes(r.Path);
                Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
                Assert.Equal("日本語", File.ReadAllText(r.Path));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Fact]
        public void 同じ名前の既存レポートが開かれていて書けなければ消さずに次の候補へ進む()
        {
            // 同じ分に再実行し、前のレポートを別のプロセスが（削除は許して）開いている状況
            var root = Path.Combine(Path.GetTempPath(), "PcReportTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                var first = Path.Combine(root, "exe");
                var second = Path.Combine(root, "desktop");
                Directory.CreateDirectory(first);
                var existing = Path.Combine(first, FileName);
                File.WriteAllText(existing, "前のレポート");

                SaveResult r;
                using (new FileStream(existing, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                {
                    r = ReportSaver.Save("新しいレポート", FileName, new[]
                    {
                        new SaveCandidate(SaveLocation.ExeFolder, first),
                        new SaveCandidate(SaveLocation.Desktop, second)
                    });
                }

                Assert.Equal(SaveLocation.Desktop, r.Location);
                Assert.True(File.Exists(existing), "開かれていた既存のレポートが消された");
                Assert.Equal("前のレポート", File.ReadAllText(existing));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        /// <summary>AC6：生成した HTML に、保存先の候補になりうるパスが含まれない（設計原則 1）。</summary>
        [Fact]
        public void 生成したHTMLに保存先のパスが含まれない()
        {
            var report = ReportBuilder.BuildFromLiveSystem(new DateTime(2026, 9, 23, 15, 42, 0), "1.0.0");
            var html = HtmlReport.Render(report);

            var paths = ReportSaver.DefaultCandidates()
                .Select(c => c.Directory)
                .Concat(new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                })
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => p.TrimEnd('\\'));

            foreach (var p in paths)
            {
                Assert.True(html.IndexOf(p, StringComparison.OrdinalIgnoreCase) < 0, "保存先のパス " + p + " が HTML に含まれている");
                // HTML では \ が \\ や / に化けることがあるので、その形でも調べる
                Assert.True(html.IndexOf(p.Replace(@"\", "/"), StringComparison.OrdinalIgnoreCase) < 0, p + "（/ 区切り）が HTML に含まれている");
                Assert.True(html.IndexOf(p.Replace(@"\", @"\\"), StringComparison.OrdinalIgnoreCase) < 0, p + "（\\\\ 区切り）が HTML に含まれている");
            }
            Assert.DoesNotContain("AppData", html);
        }
    }
}

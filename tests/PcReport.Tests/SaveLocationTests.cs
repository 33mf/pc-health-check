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
    /// PCR-003：保存先の 4 段退避（SPEC §9-2・§9-5）。
    /// ウイルス対策ソフトのフォルダ保護に止められても、必ずどこかに保存する。
    /// 書き込み処理を差し替えて「拒否」を再現する（実機のフォルダ保護は再現できないため）。
    /// </summary>
    public class SaveLocationTests
    {
        const string Html = "<html>report</html>";
        const string FileName = "pc-report_2026-09-23_1542.html";

        static IList<SaveCandidate> FourCandidates()
        {
            return new[]
            {
                new SaveCandidate(SaveLocation.ExeFolder, @"C:\Tools\PcReport"),
                new SaveCandidate(SaveLocation.Desktop, @"C:\Users\someone\Desktop"),
                new SaveCandidate(SaveLocation.AppData, @"C:\Users\someone\AppData\Local\Microforge\PcReport"),
                new SaveCandidate(SaveLocation.Temp, @"C:\Users\someone\AppData\Local\Temp")
            };
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

        static FakeDisk Deny(params SaveLocation[] locations)
        {
            var dirs = FourCandidates().Where(c => locations.Contains(c.Location)).Select(c => c.Directory).ToArray();
            return new FakeDisk(new UnauthorizedAccessException("Access to the path is denied."), dirs);
        }

        [Fact]
        public void 書ければexeと同じ場所に保存し退避扱いにしない()
        {
            var disk = Deny();
            var r = ReportSaver.Save(Html, FileName, FourCandidates(), disk.Write);

            Assert.Equal(SaveLocation.ExeFolder, r.Location);
            Assert.False(r.IsFallback);
            Assert.Equal(@"C:\Tools\PcReport\" + FileName, r.Path);
            Assert.Single(disk.Attempts);
        }

        [Fact]
        public void exeの場所が拒否されたらデスクトップに保存する()
        {
            var disk = Deny(SaveLocation.ExeFolder);
            var r = ReportSaver.Save(Html, FileName, FourCandidates(), disk.Write);

            Assert.Equal(SaveLocation.Desktop, r.Location);
            Assert.True(r.IsFallback);
            Assert.Equal(Html, disk.Written[r.Path]);
        }

        [Fact]
        public void exeの場所とデスクトップが拒否されたらアプリの保存先に保存する()
        {
            var disk = Deny(SaveLocation.ExeFolder, SaveLocation.Desktop);
            var r = ReportSaver.Save(Html, FileName, FourCandidates(), disk.Write);

            Assert.Equal(SaveLocation.AppData, r.Location);
            Assert.True(r.IsFallback);
            Assert.Equal(@"C:\Users\someone\AppData\Local\Microforge\PcReport\" + FileName, r.Path);
        }

        [Fact]
        public void 三つとも拒否されたら一時フォルダに保存する()
        {
            var disk = Deny(SaveLocation.ExeFolder, SaveLocation.Desktop, SaveLocation.AppData);
            var r = ReportSaver.Save(Html, FileName, FourCandidates(), disk.Write);

            Assert.Equal(SaveLocation.Temp, r.Location);
            Assert.True(r.IsFallback);
            Assert.Equal(4, disk.Attempts.Count);
        }

        [Fact]
        public void 全部拒否されたらフォルダ保護の可能性と次の一手を伝える()
        {
            var disk = Deny(SaveLocation.ExeFolder, SaveLocation.Desktop, SaveLocation.AppData, SaveLocation.Temp);

            var e = Assert.Throws<ReportSaveException>(
                () => ReportSaver.Save(Html, FileName, FourCandidates(), disk.Write));

            Assert.Contains("レポートを保存できませんでした", e.Message);
            Assert.Contains("フォルダ保護", e.Message);
            Assert.Contains("別のフォルダ", e.Message);
            Assert.Contains("許可", e.Message);
            Assert.IsType<UnauthorizedAccessException>(e.InnerException);
            Assert.Empty(disk.Written);
            // 利用者に見せる文言に、試した場所のフルパス（ユーザー名を含む）を出さない
            foreach (var c in FourCandidates()) Assert.DoesNotContain(c.Directory, e.Message);
            Assert.DoesNotContain("someone", e.Message);
        }

        [Fact]
        public void IOExceptionでも次の候補へ進む()
        {
            // フォルダ保護・ディスク満杯・パスが無い等は IOException 系で来ることがある
            var dirs = FourCandidates().Take(2).Select(c => c.Directory).ToArray();
            var disk = new FakeDisk(new IOException("The process cannot access the file."), dirs);

            var r = ReportSaver.Save(Html, FileName, FourCandidates(), disk.Write);
            Assert.Equal(SaveLocation.AppData, r.Location);
        }

        [Fact]
        public void 書き込みの拒否以外の例外は退避せずにそのまま投げる()
        {
            // 退避しても直らない失敗（引数の誤りなど）を「保存先が無い」と取り違えない
            var disk = new FakeDisk(new ArgumentException("bad path"), FourCandidates()[0].Directory);

            Assert.Throws<ArgumentException>(() => ReportSaver.Save(Html, FileName, FourCandidates(), disk.Write));
            Assert.Single(disk.Attempts);
        }

        [Fact]
        public void 場所が取れない候補は飛ばす()
        {
            // デスクトップが無いアカウント（サービス等）では GetFolderPath が空を返す
            var candidates = FourCandidates().ToList();
            candidates[1] = new SaveCandidate(SaveLocation.Desktop, "");
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
        public void 既定の候補は4段でこの順に並ぶ()
        {
            var c = ReportSaver.DefaultCandidates();
            Assert.Equal(new[] { SaveLocation.ExeFolder, SaveLocation.Desktop, SaveLocation.AppData, SaveLocation.Temp },
                c.Select(x => x.Location).ToArray());
            Assert.EndsWith(@"\Microforge\PcReport", c[2].Directory);
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

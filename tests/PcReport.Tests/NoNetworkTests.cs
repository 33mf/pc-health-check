using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microforge.PcReport.Analysis;
using Microforge.PcReport.Model;
using Microforge.PcReport.Rendering;
using Xunit;

namespace Microforge.PcReport.Tests
{
    /// <summary>
    /// AC6：通信しない（SPEC §2・§3 設計原則 5）。
    ///   1. 生成された HTML に外部参照（http:// https://）が無い
    ///   2. 製品のソースに HttpClient / WebClient / WebRequest / Socket が無い
    /// </summary>
    public class NoNetworkTests
    {
        static readonly Regex ExternalUrl = new Regex(@"https?://", RegexOptions.IgnoreCase);

        public static IEnumerable<object[]> Snapshots()
        {
            yield return new object[] { "正常な機", Fixtures.Healthy() };
            yield return new object[] { "全項目が拒否", Fixtures.AllFailing(ProbeOutcome.Denied, "管理者権限が必要です") };
            yield return new object[] { "全項目が空", Fixtures.AllFailing(ProbeOutcome.Empty, "情報源に値がありませんでした") };
            yield return new object[] { "全項目が例外", Fixtures.AllFailing(ProbeOutcome.Failed, "取得中にエラーが発生しました") };
        }

        [Theory]
        [MemberData(nameof(Snapshots))]
        public void 生成したHTMLに外部参照が無い(string name, SystemSnapshot snapshot)
        {
            var html = HtmlReport.Render(ReportComposer.Compose(snapshot, Fixtures.Now, "1.0.0"));

            var hit = ExternalUrl.Match(html);
            Assert.False(hit.Success,
                name + " の HTML に外部参照がある: " + Excerpt(html, hit.Index));

            // 外部から何かを読み込む属性・要素そのものが無いこと
            foreach (var marker in new[] { "<script", "<iframe", "<link ", "<img ", "url(" })
                Assert.False(html.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0,
                    name + " の HTML に " + marker + " がある");
        }

        static string Excerpt(string s, int at)
        {
            var from = Math.Max(0, at - 60);
            var len = Math.Min(140, s.Length - from);
            return s.Substring(from, len);
        }

        // ---- ソースの走査 ---------------------------------------------------

        static readonly string[] ForbiddenApis = { "HttpClient", "WebClient", "WebRequest", "Socket" };

        public static IEnumerable<object[]> ForbiddenApiNames()
        {
            return ForbiddenApis.Select(a => new object[] { a });
        }

        [Theory]
        [MemberData(nameof(ForbiddenApiNames))]
        public void 製品のソースに通信APIが無い(string api)
        {
            var srcDir = Path.Combine(Fixtures.RepoRoot(), "src");
            var pattern = new Regex(@"\b" + Regex.Escape(api) + @"\b");

            var hits = new List<string>();
            foreach (var file in Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories))
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                    if (pattern.IsMatch(lines[i]))
                        hits.Add(Relative(file) + ":" + (i + 1) + ": " + lines[i].Trim());
            }

            Assert.True(hits.Count == 0, api + " が製品のソースにある:\n" + string.Join("\n", hits));
        }

        [Fact]
        public void 製品のソースにSystemNetの通信名前空間が無い()
        {
            // System.Net.WebUtility（HTML エスケープ）は使うが、Http / Sockets / Mail は使わない
            var srcDir = Path.Combine(Fixtures.RepoRoot(), "src");
            var pattern = new Regex(@"using\s+System\.Net\.(Http|Sockets|Mail|NetworkInformation)\b");

            var hits = Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => pattern.IsMatch(File.ReadAllText(f)))
                .Select(Relative)
                .ToList();

            Assert.True(hits.Count == 0, "通信の名前空間を使っているファイル: " + string.Join(", ", hits));
        }

        [Fact]
        public void 製品のマニフェストは昇格を要求しない()
        {
            // SPEC §3 設計原則 3：asInvoker のまま（AC1）
            var manifest = Path.Combine(Fixtures.RepoRoot(), "src", "PcReport", "app.manifest");
            var text = File.ReadAllText(manifest);

            Assert.Contains("level=\"asInvoker\"", text);
            Assert.DoesNotContain("requireAdministrator", text);
            Assert.DoesNotContain("highestAvailable", text);
        }

        static string Relative(string full)
        {
            var root = Fixtures.RepoRoot();
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(root.Length).TrimStart('\\', '/')
                : full;
        }
    }
}

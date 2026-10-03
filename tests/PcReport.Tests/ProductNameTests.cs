using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microforge.PcReport.Analysis;
using Microforge.PcReport.Rendering;
using Xunit;

namespace Microforge.PcReport.Tests
{
    /// <summary>
    /// PCR-005：利用者に見せる名前を Store で予約したアプリ名「PC 健康診断」に揃える。
    /// レポート・通知・exe のファイル情報・MSIX の表示名が食い違うと、Store で見た名前と開いたものの名前が違って迷うため。
    /// 一方で exe 名と出力ファイル名は変えない（過去の検証キットと同一性を保つため）。
    /// </summary>
    public class ProductNameTests
    {
        const string StoreName = "PC 健康診断";
        const string OldName = "PC 状態レポート";

        [Fact]
        public void 製品名はStoreで予約したアプリ名()
        {
            Assert.Equal(StoreName, ReportBuilder.ProductName);
        }

        [Fact]
        public void レポートのタイトルと見出しとフッターが製品名()
        {
            var html = HtmlReport.Render(ReportComposer.Compose(Fixtures.Healthy(), Fixtures.Now, "1.0.0"));

            Assert.Contains("<title>" + StoreName + " 2026-09-21</title>", html);
            Assert.Contains("<h1>" + StoreName + "</h1>", html);
            Assert.Contains("<p class=\"ver\">" + StoreName + " v1.0.0", html);
            Assert.DoesNotContain(OldName, html);
        }

        [Fact]
        public void exeのファイル情報の製品名が製品名と同じ()
        {
            var product = typeof(ReportBuilder).Assembly.GetCustomAttribute<AssemblyProductAttribute>();
            Assert.NotNull(product);
            Assert.Equal(ReportBuilder.ProductName, product.Product);
        }

        [Fact]
        public void exe名と出力ファイル名は変えない()
        {
            Assert.Equal("PcReport.exe", Path.GetFileName(typeof(ReportBuilder).Assembly.Location));
            Assert.Equal("pc-report_2026-09-21_1930.html", ReportBuilder.FileNameFor(Fixtures.Now));
        }

        [Fact]
        public void Store用のIdentityの表示名が製品名()
        {
            Assert.Equal(StoreName, ReadDisplayName("store.psd1"));
        }

        [Fact]
        public void 検証版のIdentityの表示名は製品名で始まる()
        {
            // 検証版は Store 版と並べて入れても見分けられるよう、製品名の後ろに印を付ける
            var name = ReadDisplayName("dev.psd1");
            Assert.StartsWith(StoreName + " ", name);
        }

        [Fact]
        public void 製品とパッケージのソースに旧名が残っていない()
        {
            var root = Fixtures.RepoRoot();
            var hits = new[] { "src", Path.Combine("packaging", "msix") }
                .SelectMany(d => Directory.EnumerateFiles(Path.Combine(root, d), "*", SearchOption.AllDirectories))
                .Where(f => !IsBuildOutput(root, f))
                .Where(f => new[] { ".cs", ".xml", ".psd1", ".ps1", ".manifest", ".csproj", ".proj" }
                    .Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Where(f => File.ReadAllText(f).Contains(OldName))
                .Select(f => f.Substring(root.Length + 1))
                .ToList();

            Assert.True(hits.Count == 0, "旧名が残っている：" + string.Join(", ", hits));
        }

        static string ReadDisplayName(string identityFile)
        {
            var path = Path.Combine(Fixtures.RepoRoot(), "packaging", "msix", "identity", identityFile);
            var m = Regex.Match(File.ReadAllText(path), @"^\s*DisplayName\s*=\s*'([^']*)'", RegexOptions.Multiline);
            Assert.True(m.Success, identityFile + " に DisplayName がありません");
            return m.Groups[1].Value;
        }

        static bool IsBuildOutput(string root, string path)
        {
            var rel = path.Substring(root.Length + 1).Replace('\\', '/');
            return rel.Contains("/bin/") || rel.Contains("/obj/") || rel.StartsWith("packaging/msix/out/", StringComparison.Ordinal);
        }
    }
}

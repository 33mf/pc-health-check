using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microforge.PcReport.Analysis;
using Microforge.PcReport.Rendering;
using Microforge.PcReport.Saving;
using Xunit;

namespace Microforge.PcReport.Tests
{
    /// <summary>
    /// PCR-005：利用者に見せる名前を Store で予約したアプリ名「PC 健康診断」に揃える。
    /// レポート・通知・exe のファイル情報・MSIX の表示名が食い違うと、Store で見た名前と開いたものの名前が違って迷うため。
    /// 一方で exe 名と出力ファイル名は変えない（過去の検証キットと同一性を保つため）。
    /// PCR-007：exe の「ファイルの説明」も製品名にし、利用者に見せる発行元の綴りを Store と同じ MicroForge に揃える（SPEC §9-7・§9-8）。
    /// </summary>
    public class ProductNameTests
    {
        const string StoreName = "PC 健康診断";
        const string OldName = "PC 状態レポート";
        const string Brand = "MicroForge";
        const string OldBrand = "Microforge";

        /// <summary>
        /// 旧い綴りのまま残してよい識別子（変えると名前空間・保存先・検証版の Identity が変わる）。ここに無いものは表示文字列とみなす。
        /// 名前空間は namespace / using の行と RootNamespace・assemblyIdentity でだけ許す（文言の中に紛れた「Microforge.PcReport」は通さない）。
        /// </summary>
        static readonly string[] IdentifierPatterns =
        {
            @"^\s*(namespace|using)\s+Microforge\.PcReport\b",          // 名前空間
            @"<RootNamespace>Microforge\.PcReport</RootNamespace>",      // 名前空間（Directory.Build.props）
            @"<assemblyIdentity\b[^>]*\bname=""Microforge\.PcReport""",  // アセンブリ ID（app.manifest）
            @"\bMicroforge\.PcReport\.Dev(_[0-9a-z]{13})?\b",            // 検証版の Identity Name と PFN（開発用のスクリプトにだけ出る）
            @"CN=Microforge PCR-002 Dev \(self-signed\)",                // 検証版の Publisher（証明書の Subject。PFN の元）
            @"\\Microforge\\PcReport\b",                                 // %LOCALAPPDATA%\Microforge\PcReport（コメント中の保存先パス）
            @"""Microforge"",\s*""PcReport""",                           // 保存先パスの組み立て（Path.Combine）
        };

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
        public void exeのファイルの説明が製品名()
        {
            var title = typeof(ReportBuilder).Assembly.GetCustomAttribute<AssemblyTitleAttribute>();
            Assert.NotNull(title);
            Assert.Equal(ReportBuilder.ProductName, title.Title);
            // 利用者が見るのは Win32 のバージョン リソース（タスク マネージャー・プロパティの「詳細」）
            var info = FileVersionInfo.GetVersionInfo(typeof(ReportBuilder).Assembly.Location);
            Assert.Equal(ReportBuilder.ProductName, info.FileDescription);
            Assert.Equal(ReportBuilder.ProductName, info.ProductName);
            Assert.Equal(Brand, info.CompanyName);
        }

        [Fact]
        public void exeの会社名と著作権の綴りがStoreの発行元と同じ()
        {
            var asm = typeof(ReportBuilder).Assembly;
            Assert.Equal(Brand, asm.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company);
            Assert.Equal(Brand, asm.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright);
        }

        [Fact]
        public void レポートのロゴ文字とフッターの綴りがStoreの発行元と同じ()
        {
            var html = HtmlReport.Render(ReportComposer.Compose(Fixtures.Healthy(), Fixtures.Now, "1.0.0"));

            Assert.Contains("<div class=\"brand\">" + Brand + "</div>", html);
            Assert.Contains("<p class=\"ver\">" + StoreName + " v1.0.0　" + Brand + "</p>", html);
            Assert.DoesNotContain(OldBrand, html);
        }

        [Fact]
        public void Store用と検証版のIdentityの発行元表示名がMicroForge()
        {
            Assert.Equal(Brand, ReadValue("store.psd1", "PublisherDisplayName"));
            Assert.Equal(Brand, ReadValue("dev.psd1", "PublisherDisplayName"));
        }

        [Fact]
        public void 製品とパッケージの表示文字列に旧い綴りが残っていない()
        {
            var root = Fixtures.RepoRoot();
            var hits = SourceFiles(root)
                .Concat(new[] { Path.Combine(root, "Directory.Build.props") })
                .SelectMany(f => File.ReadAllLines(f).Select((line, n) => new { f, n, line }))
                .Where(x => IdentifierPatterns.Aggregate(x.line, (s, p) => Regex.Replace(s, p, ""))
                              .IndexOf(OldBrand, StringComparison.Ordinal) >= 0)
                .Select(x => x.f.Substring(root.Length + 1) + ":" + (x.n + 1) + "  " + x.line.Trim())
                .ToList();

            Assert.True(hits.Count == 0, "識別子でない場所に旧い綴りが残っている：\n" + string.Join("\n", hits));
        }

        [Fact]
        public void 識別子は旧い綴りのまま()
        {
            // 綴りの統一に巻き込んで識別子を変えていないこと（SPEC §9-8 で「触ってはいけない」とされたもの）
            Assert.Equal("Microforge.PcReport", typeof(ReportBuilder).Namespace);
            Assert.Equal("Microforge.PcReport.Dev", ReadValue("dev.psd1", "Name"));
            Assert.Equal("CN=Microforge PCR-002 Dev (self-signed)", ReadValue("dev.psd1", "Publisher"));
            Assert.Equal("MicroForge.PC", ReadValue("store.psd1", "Name"));
            Assert.Equal("CN=7EE8B8FD-7E10-4A73-B4F7-5DB0938E2BAC", ReadValue("store.psd1", "Publisher"));
            Assert.Equal("MicroForge.PC_ptpbgrtq4ytwy", ReadValue("store.psd1", "PackageFamilyName"));

            // 保存先のフォルダ名（大文字小文字も含めて変えない。変えると既存の利用者と別のフォルダになる）
            var appData = ReportSaver.DefaultCandidates().Single(c => c.Location == SaveLocation.AppData).Directory;
            Assert.EndsWith(@"\Microforge\PcReport", appData, StringComparison.Ordinal);

            // アセンブリ ID（app.manifest）
            var manifest = File.ReadAllText(Path.Combine(Fixtures.RepoRoot(), "src", "PcReport", "app.manifest"));
            Assert.Matches(@"<assemblyIdentity\b[^>]*\bname=""Microforge\.PcReport""", manifest);
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
            var hits = SourceFiles(root)
                .Where(f => File.ReadAllText(f).Contains(OldName))
                .Select(f => f.Substring(root.Length + 1))
                .ToList();

            Assert.True(hits.Count == 0, "旧名が残っている：" + string.Join(", ", hits));
        }

        /// <summary>製品（src）・パッケージ（packaging/msix）・利用者に結果を見せる道具（tools）のソース。ビルド生成物は除く。</summary>
        static IEnumerable<string> SourceFiles(string root)
        {
            return new[] { "src", Path.Combine("packaging", "msix"), "tools" }
                .SelectMany(d => Directory.EnumerateFiles(Path.Combine(root, d), "*", SearchOption.AllDirectories))
                .Where(f => !IsBuildOutput(root, f))
                .Where(f => new[] { ".cs", ".xml", ".psd1", ".ps1", ".manifest", ".csproj", ".proj" }
                    .Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
        }

        static string ReadDisplayName(string identityFile)
        {
            return ReadValue(identityFile, "DisplayName");
        }

        static string ReadValue(string identityFile, string key)
        {
            var path = Path.Combine(Fixtures.RepoRoot(), "packaging", "msix", "identity", identityFile);
            var m = Regex.Match(File.ReadAllText(path), @"^\s*" + key + @"\s*=\s*'([^']*)'", RegexOptions.Multiline);
            Assert.True(m.Success, identityFile + " に " + key + " がありません");
            return m.Groups[1].Value;
        }

        static bool IsBuildOutput(string root, string path)
        {
            var rel = path.Substring(root.Length + 1).Replace('\\', '/');
            return rel.Contains("/bin/") || rel.Contains("/obj/") || rel.StartsWith("packaging/msix/out/", StringComparison.Ordinal);
        }
    }
}

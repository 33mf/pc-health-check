using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using Microforge.PcReport.Analysis;
using Microforge.PcReport.Model;
using Microforge.PcReport.Probes;
using Microforge.PcReport.Rendering;
using Xunit;

namespace Microforge.PcReport.Tests
{
    /// <summary>
    /// AC3：取得失敗を消さない（SPEC §3 設計原則 2）。
    /// 収集が「拒否」「空」「例外」を返したとき、その項目が HTML に
    /// 「取得できませんでした（理由）」で必ず出ることを確かめる。
    /// </summary>
    public class UnavailableItemsTests
    {
        public static IEnumerable<object[]> ThreeFailureKinds()
        {
            // 拒否（管理者権限が要る）／空（呼べたが中身が無い）／例外（呼び出し自体が落ちた）
            yield return new object[] { ProbeOutcome.Denied, "管理者権限が必要です" };
            yield return new object[] { ProbeOutcome.Empty, "情報源に値がありませんでした" };
            yield return new object[] { ProbeOutcome.Failed, "取得中にエラーが発生しました（ManagementException）" };
        }

        [Theory]
        [MemberData(nameof(ThreeFailureKinds))]
        public void 収集が失敗しても項目は消えず理由付きでHTMLに出る(ProbeOutcome outcome, string reason)
        {
            var snapshot = Fixtures.AllFailing(outcome, reason);
            var report = ReportComposer.Compose(snapshot, Fixtures.Now, "1.0.0");
            var html = HtmlReport.Render(report);

            foreach (var prefix in ReportComposer.RequiredLabelPrefixes)
            {
                var item = report.Sections.SelectMany(s => s.Items)
                    .FirstOrDefault(i => i.Label != null && i.Label.StartsWith(prefix, StringComparison.Ordinal));

                Assert.True(item != null, "§4 の項目「" + prefix + "」がレポートから消えている");
                Assert.Equal(Status.Unavailable, item.Status);
                Assert.False(string.IsNullOrWhiteSpace(item.Reason),
                    "「" + prefix + "」に取れなかった理由が書かれていない");
            }

            // HTML 側にも「取得できませんでした（理由）」として現れている
            Assert.Contains("取得できませんでした（", html);
            Assert.Contains(reason, html);

            // 「問題なし」と読み違えられないよう、注意書きが出ている
            Assert.Contains("取得できなかった項目は「問題なし」という意味ではありません。", html);
        }

        [Theory]
        [MemberData(nameof(ThreeFailureKinds))]
        public void 収集が全滅してもHTMLの生成自体は成功する(ProbeOutcome outcome, string reason)
        {
            var report = ReportComposer.Compose(Fixtures.AllFailing(outcome, reason), Fixtures.Now, "1.0.0");
            var html = HtmlReport.Render(report);

            Assert.StartsWith("<!doctype html>", html);
            Assert.EndsWith("</html>", html);
        }

        [Fact]
        public void 収集結果そのものが無くても項目は消えない()
        {
            // 収集層が項目を埋め忘れた（null）ときでも、黙って消さない
            var snapshot = new SystemSnapshot { RanAsAdmin = false };
            var report = ReportComposer.Compose(snapshot, Fixtures.Now, "1.0.0");

            foreach (var prefix in ReportComposer.RequiredLabelPrefixes)
            {
                var item = report.Sections.SelectMany(s => s.Items)
                    .FirstOrDefault(i => i.Label != null && i.Label.StartsWith(prefix, StringComparison.Ordinal));
                Assert.True(item != null, "§4 の項目「" + prefix + "」がレポートから消えている");
                Assert.Equal(Status.Unavailable, item.Status);
                Assert.False(string.IsNullOrWhiteSpace(item.Reason));
            }
        }

        [Fact]
        public void 正常な機でもSPEC4の項目はすべて出ている()
        {
            var report = ReportComposer.Compose(Fixtures.Healthy(), Fixtures.Now, "1.0.0");

            foreach (var prefix in ReportComposer.RequiredLabelPrefixes)
            {
                var item = report.Sections.SelectMany(s => s.Items)
                    .FirstOrDefault(i => i.Label != null && i.Label.StartsWith(prefix, StringComparison.Ordinal));

                Assert.True(item != null, "§4 の項目「" + prefix + "」がレポートに無い");
                // 値があるか、理由があるか。どちらも無い行は作らない
                bool hasValue = item.Status != Status.Unavailable && !string.IsNullOrWhiteSpace(item.Value);
                bool hasReason = item.Status == Status.Unavailable && !string.IsNullOrWhiteSpace(item.Reason);
                Assert.True(hasValue || hasReason, "「" + prefix + "」が値も理由も持っていない");
            }
        }

        // ---- 例外 → 日本語の理由への変換（収集層の内部ヘルパ）----------------

        [Fact]
        public void アクセス拒否の例外は管理者権限が必要ですになる()
        {
            Assert.True(WmiGateway.IsAccessDenied(new UnauthorizedAccessException()));
            Assert.Equal("管理者権限が必要です", WmiGateway.Explain(new UnauthorizedAccessException()));

            // WBEM_E_ACCESS_DENIED
            var com = new COMException("denied", unchecked((int)0x80041003));
            Assert.True(WmiGateway.IsAccessDenied(com));
            Assert.Equal("管理者権限が必要です", WmiGateway.Explain(com));
        }

        [Fact]
        public void 拒否でない例外は拒否として扱わない()
        {
            var e = new InvalidOperationException("なにか別の失敗");
            Assert.False(WmiGateway.IsAccessDenied(e));
            Assert.Contains("エラーが発生しました", WmiGateway.Explain(e));
        }

        [Fact]
        public void 理由は必ず日本語で空にならない()
        {
            var exceptions = new Exception[]
            {
                new UnauthorizedAccessException(),
                new InvalidOperationException(),
                new ManagementException(),
                new COMException("x", unchecked((int)0x80004005)),
                new TimeoutException()
            };

            foreach (var e in exceptions)
                Assert.False(string.IsNullOrWhiteSpace(WmiGateway.Explain(e)));
        }
    }
}

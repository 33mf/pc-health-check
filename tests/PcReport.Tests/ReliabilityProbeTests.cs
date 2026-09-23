using System;
using System.Collections.Generic;
using System.Linq;
using Microforge.PcReport.Analysis;
using Microforge.PcReport.Model;
using Microforge.PcReport.Probes;
using Xunit;

namespace Microforge.PcReport.Tests
{
    /// <summary>
    /// PCR-003 AC8：SMART 詳細は MSFT_PhysicalDisk からの関連付けで取る（SPEC §7-1 ⑪）。
    /// ディスクごとの結果のまとめ方（CombineReliability）は WMI に触れないので、ここで全分岐を固定する。
    /// 昇格したときに実機で値が出ることは 003_evidence で確認する。
    /// 非昇格で「管理者権限が必要です」と出ることは ReportComposerTests の差し替えで確かめている。
    /// </summary>
    public class ReliabilityProbeTests
    {
        const string Src = "MSFT_PhysicalDisk → MSFT_StorageReliabilityCounter（関連付け）";

        static SystemProbe.DiskReliability Disk(string id, int? temp = null, long? hours = null, Exception error = null)
        {
            var d = new SystemProbe.DiskReliability { DeviceId = id, Error = error };
            if (error == null)
                d.Counters.Add(new ReliabilityCounter { DeviceId = id, Temperature = temp, PowerOnHours = hours });
            return d;
        }

        [Fact]
        public void SMART詳細の収集はクエリの誤りで失敗しない()
        {
            // 実機の WMI を叩く。値（取れる／空／拒否）は機種と権限で変わるので、仕組みが壊れていないことだけを見る
            var r = SystemProbe.ProbeReliability();

            Assert.True(r.Outcome != ProbeOutcome.Failed, "SMART 詳細の収集が失敗した: " + r.Reason);
            Assert.Contains("関連付け", r.Source);
        }

        [Fact]
        public void 値のあるディスクだけを返す()
        {
            var r = SystemProbe.CombineReliability(new[] { Disk("0", 45, 3122), Disk("1") }, Src);

            Assert.Equal(ProbeOutcome.Ok, r.Outcome);
            Assert.Single(r.Value);
            Assert.Equal("0", r.Value[0].DeviceId);
            Assert.Null(r.PartialReason);
        }

        [Fact]
        public void 一台が失敗しても他のディスクの値は残し取れなかったことも書く()
        {
            var r = SystemProbe.CombineReliability(new[]
            {
                Disk("0", 45, 3122),
                Disk("1", error: new UnauthorizedAccessException())
            }, Src);

            Assert.Equal(ProbeOutcome.Ok, r.Outcome);
            Assert.Single(r.Value);
            Assert.Contains("1 台", r.PartialReason);
            Assert.Contains("取得できませんでした", r.PartialReason);
        }

        [Fact]
        public void 全部のディスクが拒否されたら拒否として返す()
        {
            var r = SystemProbe.CombineReliability(new[]
            {
                Disk("0", error: new UnauthorizedAccessException()),
                Disk("1", error: new UnauthorizedAccessException())
            }, Src);

            Assert.Equal(ProbeOutcome.Denied, r.Outcome);
        }

        [Fact]
        public void 値が取れず拒否以外で失敗したら失敗として返す()
        {
            var r = SystemProbe.CombineReliability(new[] { Disk("0"), Disk("1", error: new InvalidOperationException()) }, Src);

            Assert.Equal(ProbeOutcome.Failed, r.Outcome);
            Assert.Contains("InvalidOperationException", r.Reason);
        }

        [Fact]
        public void 行はあっても値がゼロや空なら空として返す()
        {
            var r = SystemProbe.CombineReliability(new[] { Disk("0", 0, 0), Disk("1") }, Src);
            Assert.Equal(ProbeOutcome.Empty, r.Outcome);

            var none = SystemProbe.CombineReliability(new SystemProbe.DiskReliability[0], Src);
            Assert.Equal(ProbeOutcome.Empty, none.Outcome);
        }

        [Fact]
        public void 一部のディスクが取れなかったことはレポートの注記に出る()
        {
            var s = Fixtures.Healthy();
            s.RanAsAdmin = true;
            s.Reliability = SystemProbe.CombineReliability(new[]
            {
                Disk("0", 45, 3122),
                Disk("1", error: new InvalidOperationException())
            }, Src);

            var item = ReportComposer.Compose(s, new DateTime(2026, 9, 23, 20, 0, 0), "1.0.0")
                .Sections.SelectMany(x => x.Items).First(i => i.Label.StartsWith("詳しい SMART 情報"));
            Assert.Equal(Status.Info, item.Status);
            Assert.Contains("45℃", item.Value);
            Assert.Contains("1 台のディスクは取得できませんでした", item.Note);
        }
    }
}

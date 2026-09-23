using System.Security.Principal;
using Microforge.PcReport.Model;
using Microforge.PcReport.Probes;
using Xunit;

namespace Microforge.PcReport.Tests
{
    /// <summary>
    /// PCR-003 AC8：SMART 詳細は MSFT_PhysicalDisk からの関連付けで取る（SPEC §7-1 ⑪）。
    /// 実機の WMI を叩く。値そのものは機種と権限で変わるので、「取得の仕組みが壊れていない」ことだけを見る。
    /// 昇格したときに値が出ることは実機で確認する（003_evidence）。
    /// </summary>
    public class ReliabilityProbeTests
    {
        [Fact]
        public void SMART詳細の収集はクエリの誤りで失敗しない()
        {
            var r = SystemProbe.ProbeReliability();

            // 取れた・空・拒否のどれかであって、「取得中にエラー」（不正なクエリ等）ではない
            Assert.True(r.Outcome != ProbeOutcome.Failed, "SMART 詳細の収集が失敗した: " + r.Reason);
            Assert.Contains("関連付け", r.Source);
        }

        [Fact]
        public void 非昇格ではSMART詳細は取れた扱いにならない()
        {
            using (var id = WindowsIdentity.GetCurrent())
            {
                // 昇格して走らせたときは確かめようがないので飛ばす
                if (new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator)) return;
            }

            var r = SystemProbe.ProbeReliability();
            Assert.NotEqual(ProbeOutcome.Ok, r.Outcome);
        }
    }
}

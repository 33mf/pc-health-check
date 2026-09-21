using System;
using System.Reflection;
using Microforge.PcReport.Analysis;
using Microforge.PcReport.Model;
using Microforge.PcReport.Probes;

namespace Microforge.PcReport
{
    /// <summary>
    /// 収集層と判定層をつなぐ入口。now を必ず外から受け取る（AC2：Lesson #19 日付爆弾の回避）。
    /// テストからも同じ入口を使う（AC5：実機から生成した HTML に個人情報が無いことの確認）。
    /// </summary>
    public static class ReportBuilder
    {
        /// <summary>実機を 1 回収集して、指定時刻を基準にレポートを組み立てる。</summary>
        public static Report BuildFromLiveSystem(DateTime now, string toolVersion = null)
        {
            var snapshot = SystemProbe.Capture();
            return ReportComposer.Compose(snapshot, now, toolVersion ?? ToolVersion);
        }

        public static string ToolVersion
        {
            get
            {
                try { return Assembly.GetExecutingAssembly().GetName().Version.ToString(3); }
                catch { return "1.0.0"; }
            }
        }

        /// <summary>SPEC §9-2：pc-report_YYYY-MM-DD_HHMM.html</summary>
        public static string FileNameFor(DateTime now)
        {
            return "pc-report_" + now.ToString("yyyy-MM-dd_HHmm") + ".html";
        }
    }
}

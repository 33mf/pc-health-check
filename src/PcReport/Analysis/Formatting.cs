using System;
using System.Globalization;

namespace Microforge.PcReport.Analysis
{
    /// <summary>
    /// 表示のための整形。now は必ず引数で受け取る（Lesson #19：日付爆弾＝実行時刻に依存させない）。
    /// WMI を一切参照しないので、そのまま単体テストできる。
    /// </summary>
    public static class Formatting
    {
        static readonly CultureInfo Ja = CultureInfo.GetCultureInfo("ja-JP");

        /// <summary>「2026年9月21日」。</summary>
        public static string Date(DateTime d)
        {
            return d.ToString("yyyy年M月d日", Ja);
        }

        /// <summary>now から見て何日前か。未来日付は 0 に丸める。</summary>
        public static int DaysAgo(DateTime d, DateTime now)
        {
            var n = (int)Math.Floor((now.Date - d.Date).TotalDays);
            return n < 0 ? 0 : n;
        }

        /// <summary>「今日」「3 日前」。</summary>
        public static string Ago(DateTime d, DateTime now)
        {
            var n = DaysAgo(d, now);
            return n <= 0 ? "今日" : n + " 日前";
        }

        /// <summary>「2026年9月21日（3 日前）」。</summary>
        public static string DateWithAgo(DateTime d, DateTime now)
        {
            return Date(d) + "（" + Ago(d, now) + "）";
        }

        /// <summary>「39.8 GB」。1024 進で数える。</summary>
        public static string Gb(double bytes)
        {
            return (bytes / 1024 / 1024 / 1024).ToString("0.#", Ja) + " GB";
        }

        public static string Percent(double v)
        {
            return v.ToString("0", Ja) + "%";
        }

        public static string Thousands(long v)
        {
            return v.ToString("N0", Ja);
        }

        /// <summary>長いタイトルを丸める（レポートの 1 行が崩れないように）。</summary>
        public static string Shorten(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= n ? s : s.Substring(0, n) + "…";
        }
    }
}

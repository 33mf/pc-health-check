using System;
using System.Collections.Generic;

namespace Microforge.PcReport.Model
{
    /// <summary>
    /// 項目の判定。Unavailable は「取れなかった」＝消さずに理由付きで出す（SPEC §3 設計原則 2）。
    /// </summary>
    public enum Status
    {
        Ok,
        Info,
        Warn,
        Bad,
        Unavailable
    }

    /// <summary>レポートの 1 行。</summary>
    public sealed class Item
    {
        public string Label;
        public string Value;
        public Status Status = Status.Info;

        /// <summary>見る人向けの一言（なぜ注意か／何をすればよいか）。</summary>
        public string Note;

        /// <summary>取れなかった理由（Status.Unavailable のときだけ入る）。</summary>
        public string Reason;

        /// <summary>取得元（検証用。HTML では小さく表示する）。</summary>
        public string Source;

        public static Item Unavailable(string label, string reason, string source = null)
        {
            return new Item
            {
                Label = label,
                Status = Status.Unavailable,
                Reason = string.IsNullOrEmpty(reason) ? "理由を特定できませんでした" : reason,
                Source = source
            };
        }
    }

    public sealed class Section
    {
        public string Title;
        public string Lead;
        public List<Item> Items = new List<Item>();

        public Section(string title, string lead = null)
        {
            Title = title;
            Lead = lead;
        }

        public Item Add(Item i)
        {
            Items.Add(i);
            return i;
        }
    }

    public sealed class Report
    {
        /// <summary>レポートの基準時刻。収集時に注入された now がそのまま入る（Lesson #19：日付爆弾の回避）。</summary>
        public DateTime GeneratedAt;

        public string ToolVersion;

        /// <summary>昇格した管理者として動いているか。SMART 詳細が空のときの理由分けに使う（SPEC §7-1 ③）。</summary>
        public bool RanAsAdmin;

        public List<Section> Sections = new List<Section>();
    }
}

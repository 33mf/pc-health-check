using System;

namespace Microforge.PcReport.Model
{
    /// <summary>
    /// 収集の結果種別。「取れなかった」を 3 つに分けて持つ（dispatch AC3）。
    /// 判定層はこの 3 つをすべて「取得できませんでした（理由）」に落とす＝項目が消えない。
    /// </summary>
    public enum ProbeOutcome
    {
        /// <summary>値が取れた。</summary>
        Ok,

        /// <summary>アクセス拒否された（管理者権限が要る等）。</summary>
        Denied,

        /// <summary>呼び出しは成功したが中身が無かった（拒否ではない）。SPEC §7-1 の SMART 詳細がこれ。</summary>
        Empty,

        /// <summary>例外で失敗した。</summary>
        Failed
    }

    /// <summary>
    /// 収集層から判定層へ渡す箱。収集層は判定・表示文言を一切作らず、生値と結果種別だけを入れる。
    /// </summary>
    public sealed class ProbeResult<T>
    {
        public ProbeOutcome Outcome;

        /// <summary>Outcome が Ok のときだけ意味を持つ生値。</summary>
        public T Value;

        /// <summary>Ok 以外のときの、見る人に分かる日本語の理由。</summary>
        public string Reason;

        /// <summary>取得元（WMI クラス名やレジストリパス）。成否にかかわらず入れる。</summary>
        public string Source;

        /// <summary>
        /// Ok だが一部（複数ディスクのうち何台か等）は取れなかったときの、その理由。
        /// 取れた分だけを出して「全部取れた」と読ませないため（設計原則 2）。
        /// </summary>
        public string PartialReason;

        public bool IsOk
        {
            get { return Outcome == ProbeOutcome.Ok && Value != null; }
        }

        public static ProbeResult<T> Ok(T value, string source)
        {
            // 値が null なら Ok を名乗らせない（判定層で NullReference を起こさないため）
            if (value == null) return Empty("情報源に値がありませんでした", source);
            return new ProbeResult<T> { Outcome = ProbeOutcome.Ok, Value = value, Source = source };
        }

        public static ProbeResult<T> Denied(string source, string reason = null)
        {
            return new ProbeResult<T>
            {
                Outcome = ProbeOutcome.Denied,
                Reason = string.IsNullOrEmpty(reason) ? "管理者権限が必要です" : reason,
                Source = source
            };
        }

        public static ProbeResult<T> Empty(string reason, string source)
        {
            return new ProbeResult<T>
            {
                Outcome = ProbeOutcome.Empty,
                Reason = string.IsNullOrEmpty(reason) ? "情報源に値がありませんでした" : reason,
                Source = source
            };
        }

        public static ProbeResult<T> Failed(string reason, string source)
        {
            return new ProbeResult<T>
            {
                Outcome = ProbeOutcome.Failed,
                Reason = string.IsNullOrEmpty(reason) ? "取得中にエラーが発生しました" : reason,
                Source = source
            };
        }
    }
}

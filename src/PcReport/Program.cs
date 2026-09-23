using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microforge.PcReport.Rendering;
using Microforge.PcReport.Saving;

namespace Microforge.PcReport
{
    /// <summary>
    /// ダブルクリック → 収集 → exe と同じ場所に HTML を保存（書けなければ退避。ReportSaver）→ 既定のブラウザで開く。
    /// インストール・設定画面・初回ウィザード・通信は持たない（SPEC §3 原則 4・5）。
    ///
    /// 検証用オプション（編集長・Code が使う。ふつうの利用者は使わない）:
    ///   --out &lt;dir&gt;   保存先フォルダを指定する
    ///   --no-open      保存したあとブラウザで開かない
    /// </summary>
    internal static class Program
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AttachConsole(int dwProcessId);

        const int AttachParentProcess = -1;

        [STAThread]
        static int Main(string[] args)
        {
            string outDir = null;
            bool open = true;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--no-open") open = false;
                else if (args[i] == "--out" && i + 1 < args.Length) outDir = args[++i];
            }

            // オプション付きで呼ばれたとき＝コマンドラインからの検証実行。
            // 呼び出し元のコンソールに結果を書けるようにする（ダブルクリック時は何も起きない）。
            bool console = args.Length > 0 && AttachConsole(AttachParentProcess);

            try
            {
                // now は 1 回だけ決め、ファイル名とレポート本文で同じ値を使う
                var now = DateTime.Now;
                var report = ReportBuilder.BuildFromLiveSystem(now);
                var html = HtmlReport.Render(report);
                var saved = ReportSaver.Save(html, ReportBuilder.FileNameFor(now), Candidates(outDir));

                if (console)
                {
                    Console.Out.WriteLine(saved.Path);
                    if (saved.IsFallback)
                        Console.Out.WriteLine("（退避：" + ReportSaver.DisplayName(saved.Location) + "）");
                }
                else if (saved.IsFallback)
                {
                    // SPEC §9-5：いつもの場所に書けず退避したときだけ、開く前に 1 回知らせる。
                    // 場所の呼び名だけを出し、フルパスは出さない（ユーザー名が含まれる。設計原則 1）
                    MessageBox.Show(
                        "レポートは" + ReportSaver.DisplayName(saved.Location) + "に保存しました。",
                        "PC 状態レポート", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                if (open) Process.Start(new ProcessStartInfo(saved.Path) { UseShellExecute = true });
                return 0;
            }
            catch (ReportSaveException e)
            {
                // 候補のどこにも書けなかった（SPEC §9-5）。文言は ReportSaver.FailureMessage のまま出す
                if (console) Console.Error.WriteLine("PcReport: " + e.Message.Replace("\n\n", " ").Replace("\n", " "));
                else
                    MessageBox.Show(e.Message, "PC 状態レポート", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }
            catch (Exception e)
            {
                if (console) Console.Error.WriteLine("PcReport: " + e.Message);
                else
                    MessageBox.Show("レポートを作れませんでした。\n\n" + e.Message,
                        "PC 状態レポート", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }
        }

        /// <summary>
        /// SPEC §9-2・§9-5：exe と同じ場所 → デスクトップ → %LOCALAPPDATA%\Microforge\PcReport → %TEMP% の順に試す。
        /// --out が指定されていれば、そこだけを使う（検証で保存先を取り違えないため）。
        /// </summary>
        static IList<SaveCandidate> Candidates(string outDir)
        {
            if (outDir != null)
                return new[] { new SaveCandidate(SaveLocation.Specified, Path.GetFullPath(outDir)) };
            return ReportSaver.DefaultCandidates();
        }
    }
}

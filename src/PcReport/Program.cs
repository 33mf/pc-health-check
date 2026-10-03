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
                    Notify("レポートは" + ReportSaver.DisplayName(saved.Location) + "に保存しました。",
                        MessageBoxIcon.Information);
                }

                if (open)
                {
                    // 保存はできている。開けなかったことを「作れなかった」と取り違えさせない
                    try { Process.Start(new ProcessStartInfo(saved.Path) { UseShellExecute = true }); }
                    catch (Exception)
                    {
                        var msg = "レポートは" + ReportSaver.DisplayName(saved.Location)
                                  + "に保存しましたが、ブラウザで開けませんでした。";
                        if (console) Console.Error.WriteLine("PcReport: " + msg);
                        else Notify(msg, MessageBoxIcon.Warning);
                    }
                }
                return 0;
            }
            catch (ReportSaveException e)
            {
                // 候補のどこにも書けなかった（SPEC §9-5）。文言は ReportSaver.FailureMessage のまま出す
                if (console)
                    // 検証する人向けに元の例外の種類だけ添える（例外の本文はパスを含むので出さない）
                    Console.Error.WriteLine("PcReport: " + e.Message.Replace("\n\n", " ").Replace("\n", " ")
                        + (e.InnerException != null ? "（原因の種類：" + e.InnerException.GetType().Name + "）" : ""));
                else
                    Notify(e.Message, MessageBoxIcon.Warning);
                return 1;
            }
            catch (Exception e)
            {
                // 例外の本文はパス（ユーザー名を含む）を含みうるので、種類だけを出す（設計原則 1）
                var msg = "レポートを作れませんでした。（原因の種類：" + e.GetType().Name + "）";
                if (console) Console.Error.WriteLine("PcReport: " + msg);
                else Notify(msg, MessageBoxIcon.Warning);
                return 1;
            }
        }

        /// <summary>
        /// 通知を最前面に出す。収集に時間がかかる間に利用者が別のウィンドウを触っていると、
        /// オーナーの無い MessageBox は背面に回り、OK を待ったまま「何も起きない」ように見えるため。
        /// オーナーは見えない（Opacity 0）が、タスクバーにはボタンを出す（ほかの最前面ウィンドウの下に入っても探せるように）。
        /// 対話できるデスクトップが無い等で表示できなければ、何もしない（保存済みのレポートを失敗扱いにしない）。
        /// </summary>
        static void Notify(string text, MessageBoxIcon icon)
        {
            try
            {
                using (var owner = new Form
                {
                    Text = ReportBuilder.ProductName,
                    TopMost = true,
                    ShowInTaskbar = true,
                    Opacity = 0,
                    StartPosition = FormStartPosition.CenterScreen
                })
                {
                    owner.Show();
                    owner.Activate();
                    MessageBox.Show(owner, text, ReportBuilder.ProductName, MessageBoxButtons.OK, icon);
                }
            }
            catch (Exception) { /* 通知を出せない環境。保存・終了コードには影響させない */ }
        }

        /// <summary>
        /// SPEC §9-2・§9-5・§9-6：exe と同じ場所 → デスクトップ → ダウンロード →
        /// （素の exe のときだけ）%LOCALAPPDATA%\Microforge\PcReport → %TEMP% の順に試す（ReportSaver.BuildCandidates）。
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

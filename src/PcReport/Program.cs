using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microforge.PcReport.Rendering;

namespace Microforge.PcReport
{
    /// <summary>
    /// ダブルクリック → 収集 → exe と同じ場所に HTML を保存 → 既定のブラウザで開く。
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
                var path = Save(html, ReportBuilder.FileNameFor(now), outDir);

                if (console) Console.Out.WriteLine(path);
                if (open) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return 0;
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
        /// SPEC §9-2：exe と同じ場所に保存し、書き込めなければデスクトップに退避する。
        /// --out が指定されていれば、そこだけを使う（検証で保存先を取り違えないため）。
        /// </summary>
        static string Save(string html, string fileName, string outDir)
        {
            // BOM 付き UTF-8。ブラウザが文字コードを取り違えないようにする
            var enc = new UTF8Encoding(true);

            string[] candidates;
            if (outDir != null)
            {
                Directory.CreateDirectory(outDir);
                candidates = new[] { outDir };
            }
            else
            {
                candidates = new[]
                {
                    AppDomain.CurrentDomain.BaseDirectory,
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                };
            }

            Exception last = null;
            foreach (var dir in candidates)
            {
                if (string.IsNullOrEmpty(dir)) continue;
                try
                {
                    var p = Path.Combine(dir, fileName);
                    File.WriteAllText(p, html, enc);
                    return p;
                }
                catch (Exception e) { last = e; }
            }
            throw new IOException("レポートを保存できる場所が見つかりませんでした。", last);
        }
    }
}

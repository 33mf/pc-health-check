using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Microforge.PcReport.Saving
{
    /// <summary>保存先の種類。利用者に見せるのは種類の呼び名だけで、フルパスは見せない（設計原則 1）。</summary>
    public enum SaveLocation
    {
        /// <summary>exe と同じ場所（SPEC §9-2 の既定）。</summary>
        ExeFolder,
        /// <summary>デスクトップ。</summary>
        Desktop,
        /// <summary>%LOCALAPPDATA%\Microforge\PcReport\（Microforge のツールで共通に使う置き場所）。</summary>
        AppData,
        /// <summary>%TEMP%。</summary>
        Temp,
        /// <summary>--out で指定された場所（検証用）。退避しない。</summary>
        Specified
    }

    public sealed class SaveCandidate
    {
        public SaveLocation Location;
        public string Directory;

        public SaveCandidate(SaveLocation location, string directory)
        {
            Location = location;
            Directory = directory;
        }
    }

    public sealed class SaveResult
    {
        public string Path;
        public SaveLocation Location;

        /// <summary>既定の場所（exe と同じ場所）に書けず、別の場所に退避したか。</summary>
        public bool IsFallback
        {
            get { return Location != SaveLocation.ExeFolder && Location != SaveLocation.Specified; }
        }
    }

    /// <summary>どの候補にも保存できなかった。Message は利用者にそのまま見せる文言。</summary>
    public sealed class ReportSaveException : Exception
    {
        public ReportSaveException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// SPEC §9-2・§9-5：候補を順に試して、必ずどこかに保存する。
    /// ウイルス対策ソフトのフォルダ保護（ランサムウェア対策）は、exe の横もデスクトップも止めうる
    /// （SPEC §7-1 ⑨：ウイルスバスターのフォルダシールドが HTML の書き込みをブロックした実例）。
    /// 書き込み処理は差し替えられる（単体テストで「拒否」を再現するため）。
    /// </summary>
    public static class ReportSaver
    {
        public const string FailureMessage =
            "レポートを保存できませんでした。" +
            "ウイルス対策ソフトのフォルダ保護（ランサムウェア対策）に止められた可能性があります。\n\n" +
            "・PcReport.exe を別のフォルダ（例：C:\\PcReport）に移して、もう一度実行してください。\n" +
            "・または、ウイルス対策ソフトの設定で PcReport.exe を許可してください。";

        /// <summary>利用者に見せる場所の呼び名。フルパスは出さない（ユーザー名が含まれるため）。</summary>
        public static string DisplayName(SaveLocation location)
        {
            switch (location)
            {
                case SaveLocation.ExeFolder: return "このアプリと同じ場所";
                case SaveLocation.Desktop: return "デスクトップ";
                case SaveLocation.AppData: return "このアプリの保存先";
                case SaveLocation.Temp: return "一時フォルダ";
                default: return "指定された場所";
            }
        }

        /// <summary>既定の 4 段（exe と同じ場所 → デスクトップ → %LOCALAPPDATA%\Microforge\PcReport → %TEMP%）。</summary>
        public static IList<SaveCandidate> DefaultCandidates()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return new[]
            {
                new SaveCandidate(SaveLocation.ExeFolder, AppDomain.CurrentDomain.BaseDirectory),
                new SaveCandidate(SaveLocation.Desktop,
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
                new SaveCandidate(SaveLocation.AppData,
                    string.IsNullOrEmpty(localAppData) ? null : System.IO.Path.Combine(localAppData, "Microforge", "PcReport")),
                new SaveCandidate(SaveLocation.Temp, SafeTempPath())
            };
        }

        /// <summary>BOM 付き UTF-8 で書く（ブラウザが文字コードを取り違えないように）。フォルダが無ければ作る。</summary>
        public static void WriteFile(string path, string html)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            File.WriteAllText(path, html, new UTF8Encoding(true));
        }

        public static SaveResult Save(string html, string fileName, IList<SaveCandidate> candidates)
        {
            return Save(html, fileName, candidates, WriteFile);
        }

        /// <summary>
        /// 候補を順に試す。書き込みを拒否された（UnauthorizedAccessException / IOException）ときだけ次へ進む。
        /// それ以外の例外は「退避しても直らない」ので、そのまま投げる。
        /// </summary>
        public static SaveResult Save(string html, string fileName, IList<SaveCandidate> candidates,
            Action<string, string> write)
        {
            Exception last = null;
            foreach (var c in candidates)
            {
                if (string.IsNullOrEmpty(c.Directory)) continue;
                var path = System.IO.Path.Combine(c.Directory, fileName);
                try
                {
                    write(path, html);
                    return new SaveResult { Path = path, Location = c.Location };
                }
                catch (UnauthorizedAccessException e) { last = e; }
                catch (IOException e) { last = e; }
            }
            throw new ReportSaveException(FailureMessage, last);
        }

        static string SafeTempPath()
        {
            try { return System.IO.Path.GetTempPath(); }
            catch (System.Security.SecurityException) { return null; }
        }
    }
}

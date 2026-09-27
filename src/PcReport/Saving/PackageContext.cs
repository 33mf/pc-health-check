using System;
using System.Runtime.InteropServices;

namespace Microforge.PcReport.Saving
{
    /// <summary>パッケージ（MSIX / Store 版）として動いているか。</summary>
    public enum PackageState
    {
        /// <summary>素の exe として動いている。</summary>
        NotPackaged,
        /// <summary>パッケージ ID を持って動いている（MSIX / Store 版）。</summary>
        Packaged,
        /// <summary>判定できなかった（API が無い古い Windows、想定外の戻り値など）。</summary>
        Unknown
    }

    /// <summary>
    /// 保存先を決めるための、実行環境の情報。
    /// パッケージ化されたフルトラストのアプリが %LOCALAPPDATA% に書いたファイルは、パッケージ専用の場所に
    /// 振り替えられて利用者から見えなくなる（PCR-002 で実測。SPEC §9-6）。そのため判定が要る。
    /// </summary>
    public static class PackageContext
    {
        const int ErrorInsufficientBuffer = 122;
        const int AppModelErrorNoPackage = 15700;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int GetCurrentPackageFullName(ref int packageFullNameLength, System.Text.StringBuilder packageFullName);

        /// <summary>
        /// GetCurrentPackageFullName で判定する（Windows 8 以降）。
        /// 長さ 0 で呼ぶと、パッケージがあれば ERROR_INSUFFICIENT_BUFFER、無ければ APPMODEL_ERROR_NO_PACKAGE が返る。
        /// それ以外（API が無い等）は Unknown。呼び出し側は Unknown を「パッケージかもしれない」として扱う。
        /// </summary>
        public static PackageState Detect()
        {
            try
            {
                int length = 0;
                int rc = GetCurrentPackageFullName(ref length, null);
                if (rc == AppModelErrorNoPackage) return PackageState.NotPackaged;
                if (rc == ErrorInsufficientBuffer || rc == 0) return PackageState.Packaged;
                return PackageState.Unknown;
            }
            // API が無い（Windows 7 等）なら本来パッケージではありえないが、対象外の OS なので、
            // 「判定できないときは %LOCALAPPDATA% を使わない」側にそろえて Unknown にする（SPEC §9-6）
            catch (EntryPointNotFoundException) { return PackageState.Unknown; }
            catch (DllNotFoundException) { return PackageState.Unknown; }
        }

        static readonly Guid DownloadsFolderId = new Guid("374DE290-123F-4565-9164-39C4925E467B");

        [DllImport("shell32.dll")]
        static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags,
            IntPtr hToken, out IntPtr ppszPath);

        /// <summary>
        /// ダウンロード フォルダ（既知フォルダ FOLDERID_Downloads）。利用者が場所を移していても、その場所を返す。
        /// 取れなければ null（候補から外れる）。
        /// </summary>
        public static string DownloadsFolder()
        {
            IntPtr p = IntPtr.Zero;
            try
            {
                if (SHGetKnownFolderPath(DownloadsFolderId, 0, IntPtr.Zero, out p) != 0) return null;
                return Marshal.PtrToStringUni(p);
            }
            catch (EntryPointNotFoundException) { return null; }
            finally
            {
                if (p != IntPtr.Zero) Marshal.FreeCoTaskMem(p);
            }
        }
    }
}

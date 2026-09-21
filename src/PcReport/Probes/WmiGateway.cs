using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Microforge.PcReport.Probes
{
    /// <summary>
    /// WMI・レジストリ・Win32 API の薄いラッパー。
    /// 失敗は例外のまま上へ返し、SystemProbe 側で ProbeResult に畳む。判定は一切しない。
    /// </summary>
    internal static class WmiGateway
    {
        internal const string Cimv2 = @"root\cimv2";
        internal const string StorageNs = @"root\Microsoft\Windows\Storage";
        internal const string SecurityCenter2 = @"root\SecurityCenter2";
        internal const string DefenderNs = @"root\Microsoft\Windows\Defender";
        internal const string TpmNs = @"root\cimv2\Security\MicrosoftTpm";

        // 応答しない WMI プロバイダで固まらないよう、必ず時間切れを入れる
        static readonly EnumerationOptions Opt = new EnumerationOptions
        {
            Timeout = TimeSpan.FromSeconds(15),
            ReturnImmediately = false
        };

        public static List<ManagementObject> Query(string ns, string wql)
        {
            var scope = new ManagementScope(ns);
            scope.Connect();
            using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(wql), Opt))
            {
                var list = new List<ManagementObject>();
                foreach (ManagementObject o in searcher.Get()) list.Add(o);
                return list;
            }
        }

        public static ManagementObject First(string ns, string wql)
        {
            var l = Query(ns, wql);
            return l.Count > 0 ? l[0] : null;
        }

        public static string Str(ManagementBaseObject o, string p)
        {
            if (o == null) return null;
            var v = o[p];
            if (v == null) return null;
            var s = v.ToString().Trim();
            return s.Length == 0 ? null : s;
        }

        public static long Long(ManagementBaseObject o, string p)
        {
            if (o == null || o[p] == null) return 0;
            try { return Convert.ToInt64(o[p]); } catch { return 0; }
        }

        public static int Int(ManagementBaseObject o, string p)
        {
            if (o == null || o[p] == null) return -1;
            try { return Convert.ToInt32(o[p]); } catch { return -1; }
        }

        public static int? IntOrNull(ManagementBaseObject o, string p)
        {
            if (o == null || o[p] == null) return null;
            try { return Convert.ToInt32(o[p]); } catch { return null; }
        }

        public static long? LongOrNull(ManagementBaseObject o, string p)
        {
            if (o == null || o[p] == null) return null;
            try { return Convert.ToInt64(o[p]); } catch { return null; }
        }

        public static bool Bool(ManagementBaseObject o, string p, bool fallback)
        {
            if (o == null || o[p] == null) return fallback;
            try { return Convert.ToBoolean(o[p]); } catch { return fallback; }
        }

        public static DateTime? Date(ManagementBaseObject o, string p)
        {
            var s = Str(o, p);
            if (string.IsNullOrEmpty(s)) return null;
            try { return ManagementDateTimeConverter.ToDateTime(s); } catch { return null; }
        }

        // ---- レジストリ -------------------------------------------------

        public static RegistryKey Hklm(string path)
        {
            return RegistryKey
                .OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(path, false);
        }

        public static object Reg(string path, string name)
        {
            using (var k = Hklm(path)) return k == null ? null : k.GetValue(name);
        }

        public static string RegStr(string path, string name)
        {
            var v = Reg(path, name);
            if (v == null) return null;
            var s = Convert.ToString(v);
            return string.IsNullOrEmpty(s) ? null : s;
        }

        public static int? RegInt(string path, string name)
        {
            var v = Reg(path, name);
            if (v == null) return null;
            try { return Convert.ToInt32(v); } catch { return null; }
        }

        // ---- Win32 API ---------------------------------------------------

        /// <summary>
        /// ファームウェア種別。レジストリ PEFirmwareType は通常起動では存在しないため
        /// （SPEC §7-1 ①、参考実装の誤り）、kernel32 の API を使う。
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetFirmwareType(out uint FirmwareType);

        /// <summary>0=Unknown 1=BIOS 2=UEFI。取れなければ null。</summary>
        public static uint? FirmwareType()
        {
            uint t;
            if (!GetFirmwareType(out t)) return null;
            return t;
        }

        // ---- 例外 → 日本語の理由 -----------------------------------------

        /// <summary>アクセス拒否かどうか（ProbeOutcome.Denied と Failed の振り分けに使う）。</summary>
        public static bool IsAccessDenied(Exception e)
        {
            var me = e as ManagementException;
            if (me != null) return me.ErrorCode == ManagementStatus.AccessDenied;

            if (e is UnauthorizedAccessException || e is System.Security.SecurityException) return true;

            var ce = e as COMException;
            if (ce != null)
            {
                var hr = (uint)ce.ErrorCode;
                // 0x80070005 = E_ACCESSDENIED、0x80041003 = WBEM_E_ACCESS_DENIED
                return hr == 0x80070005 || hr == 0x80041003;
            }
            return false;
        }

        /// <summary>例外を、見る人に分かる「取れなかった理由」に変換する。</summary>
        public static string Explain(Exception e)
        {
            if (IsAccessDenied(e)) return "管理者権限が必要です";

            var me = e as ManagementException;
            if (me != null)
            {
                switch (me.ErrorCode)
                {
                    case ManagementStatus.InvalidNamespace:
                    case ManagementStatus.InvalidClass:
                        return "この Windows には該当する情報源がありません";
                    case ManagementStatus.NotSupported:
                        return "この PC では対応していません";
                    case ManagementStatus.Timedout:
                        return "応答がなく時間切れになりました";
                }
                return "取得中にエラーが発生しました（" + me.ErrorCode + "）";
            }

            var ce = e as COMException;
            if (ce != null)
                return "取得中にエラーが発生しました（0x" + ((uint)ce.ErrorCode).ToString("X8") + "）";

            return "取得中にエラーが発生しました（" + e.GetType().Name + "）";
        }
    }
}

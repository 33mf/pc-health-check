using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microforge.PcReport.Rendering;
using Xunit;

namespace Microforge.PcReport.Tests
{
    /// <summary>
    /// AC5：個人を特定する情報が HTML に無い（SPEC §3 設計原則 1）。
    ///
    /// この PC の実際のユーザー名・PC 名・IP アドレス・ディスク/BIOS シリアルを
    /// 「テストの中でだけ」取得し、製品が生成した HTML にそれが含まれないことを確かめる。
    /// 製品側（Probes）はこれらを取得する口すら持っていない。
    /// </summary>
    public class PrivacyTests
    {
        /// <summary>実機を収集して HTML を作る。Windows 上でしか意味のないテスト。</summary>
        static string RenderLiveReport()
        {
            var report = ReportBuilder.BuildFromLiveSystem(new DateTime(2026, 9, 21, 19, 30, 0), "1.0.0");
            return HtmlReport.Render(report);
        }

        [Fact]
        public void 生成したHTMLにユーザー名とPC名が含まれない()
        {
            var html = RenderLiveReport();

            foreach (var secret in Identifiers())
                AssertAbsent(html, secret.Key, secret.Value);
        }

        [Fact]
        public void 生成したHTMLにIPアドレスが含まれない()
        {
            var html = RenderLiveReport();

            foreach (var ip in LocalIpAddresses())
                AssertAbsent(html, "IP アドレス", ip);

            // MAC アドレスも同じ扱い
            foreach (var mac in MacAddresses())
                AssertAbsent(html, "MAC アドレス", mac);
        }

        [Fact]
        public void 生成したHTMLにディスクとBIOSのシリアル番号が含まれない()
        {
            var html = RenderLiveReport();

            foreach (var serial in Serials())
                AssertAbsent(html, "シリアル番号", serial);
        }

        [Fact]
        public void 生成したHTMLにユーザープロファイルのパスが含まれない()
        {
            var html = RenderLiveReport();

            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(profile))
                AssertAbsent(html, "ユーザープロファイルのパス", profile);
        }

        [Fact]
        public void 通信しないことをHTMLに明記している()
        {
            Assert.Contains("どこにも送信されていません", RenderLiveReport());
        }

        // ---- ここから下はテスト専用の取得（製品コードには存在しない）--------

        /// <summary>
        /// 識別子が HTML に無いことを確かめる。落ちたときのメッセージには値そのものを入れない
        /// （CI のログなどに出ても困らないように）。種類・文字数・HTML の中の位置だけを出す。
        /// </summary>
        static void AssertAbsent(string html, string kind, string needle)
        {
            if (string.IsNullOrWhiteSpace(needle)) return;
            var at = html.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            Assert.False(at >= 0,
                kind + "（" + needle.Length + " 文字）が HTML の " + at + " 文字目（全 " + html.Length + " 文字）に含まれている");
        }

        static Dictionary<string, string> Identifiers()
        {
            var d = new Dictionary<string, string>
            {
                { "ユーザー名", Environment.UserName },
                { "PC 名", Environment.MachineName },
                { "ドメイン/ワークグループ名", Environment.UserDomainName },
                { "ホスト名", SafeHostName() }
            };
            // 短すぎる値は偶然一致しうるので、確認対象から外す（誤検知を実証にしない）
            return d.Where(kv => !string.IsNullOrWhiteSpace(kv.Value) && kv.Value.Length >= 4)
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        static string SafeHostName()
        {
            try { return Dns.GetHostName(); } catch { return null; }
        }

        static List<string> LocalIpAddresses()
        {
            var list = new List<string>();
            try
            {
                foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                {
                    if (IPAddress.IsLoopback(ip)) continue;
                    // 127.x や ::1 以外の、この PC に割り当てられているアドレス
                    if (ip.AddressFamily == AddressFamily.InterNetwork
                        || ip.AddressFamily == AddressFamily.InterNetworkV6)
                        list.Add(ip.ToString());
                }
            }
            catch { /* ネットワークが無くてもテストは成り立つ */ }
            return list.Where(s => s.Length >= 7).Distinct().ToList();
        }

        static List<string> MacAddresses()
        {
            var list = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    var mac = ni.GetPhysicalAddress();
                    if (mac == null) continue;
                    var bytes = mac.GetAddressBytes();
                    if (bytes.Length != 6) continue;
                    list.Add(string.Join("-", bytes.Select(b => b.ToString("X2"))));
                    list.Add(string.Join(":", bytes.Select(b => b.ToString("X2"))));
                }
            }
            catch { }
            return list.Distinct().ToList();
        }

        static readonly string[] PlaceholderSerials =
        {
            "to be filled by o.e.m.", "default string", "none", "system serial number",
            "0", "00000000", "not specified", "not available"
        };

        static List<string> Serials()
        {
            var list = new List<string>();
            AddSerials(list, @"root\cimv2", "SELECT SerialNumber FROM Win32_DiskDrive", "SerialNumber");
            AddSerials(list, @"root\cimv2", "SELECT SerialNumber FROM Win32_BIOS", "SerialNumber");
            AddSerials(list, @"root\cimv2", "SELECT SerialNumber FROM Win32_BaseBoard", "SerialNumber");
            AddSerials(list, @"root\Microsoft\Windows\Storage",
                "SELECT SerialNumber FROM MSFT_PhysicalDisk", "SerialNumber");

            return list
                // 5 文字未満や既知のダミー値は、偶然一致を招くだけなので確認対象から外す
                .Where(s => s.Length >= 5 && !PlaceholderSerials.Contains(s.ToLowerInvariant()))
                .Distinct()
                .ToList();
        }

        static void AddSerials(List<string> into, string ns, string wql, string prop)
        {
            try
            {
                var scope = new ManagementScope(ns);
                scope.Connect();
                using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(wql)))
                foreach (ManagementObject o in searcher.Get())
                {
                    var v = o[prop];
                    if (v == null) continue;
                    var s = v.ToString().Trim();
                    if (s.Length > 0) into.Add(s);
                }
            }
            catch { /* 取れない環境ではその項目を確認対象から外すだけ */ }
        }
    }
}

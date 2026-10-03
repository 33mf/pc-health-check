using System.Linq;
using System.Net;
using System.Text;
using Microforge.PcReport.Analysis;
using Microforge.PcReport.Model;

namespace Microforge.PcReport.Rendering
{
    /// <summary>
    /// HTML 1 ファイル。CSS・色・フォント指定はすべて埋め込みで、外部からは何も読み込まない。
    /// SPEC §2「通信しない」／AC6：出力に http:// https:// の外部参照を作らない。
    /// 印刷用 CSS（A4）を持ち、ブラウザの「印刷 → PDF に保存」で PDF にできる。
    /// </summary>
    public static class HtmlReport
    {
        static string H(string s)
        {
            return WebUtility.HtmlEncode(s ?? "");
        }

        static string Badge(Status s)
        {
            switch (s)
            {
                case Status.Ok: return "<span class=\"b ok\">問題なし</span>";
                case Status.Warn: return "<span class=\"b warn\">要確認</span>";
                case Status.Bad: return "<span class=\"b bad\">要対応</span>";
                case Status.Unavailable: return "<span class=\"b na\">取得できず</span>";
                default: return "<span class=\"b info\">情報</span>";
            }
        }

        public static string Render(Report r)
        {
            var all = r.Sections.SelectMany(s => s.Items).ToList();
            var bad = all.Where(i => i.Status == Status.Bad).ToList();
            var warn = all.Where(i => i.Status == Status.Warn).ToList();
            var na = all.Where(i => i.Status == Status.Unavailable).ToList();

            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang=\"ja\"><head><meta charset=\"utf-8\">");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            // 万一この HTML に外部参照が紛れ込んでも、ブラウザ側で読みに行かせない保険
            sb.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'\">");
            sb.Append("<title>").Append(ReportBuilder.ProductName).Append(" ").Append(H(r.GeneratedAt.ToString("yyyy-MM-dd"))).Append("</title>");
            sb.Append("<style>").Append(Css).Append("</style></head><body><main>");

            sb.Append("<header><div class=\"brand\">MicroForge</div><h1>").Append(ReportBuilder.ProductName).Append("</h1>");
            sb.Append("<p class=\"meta\">作成 ")
              .Append(H(Formatting.Date(r.GeneratedAt) + " " + r.GeneratedAt.ToString("HH:mm")))
              .Append("　／　")
              .Append(r.RanAsAdmin ? "管理者として実行" : "通常の権限で実行")
              .Append("</p></header>");

            // まず見るところ
            sb.Append("<section class=\"summary\"><h2>まず見るところ</h2>");
            if (bad.Count == 0 && warn.Count == 0)
            {
                sb.Append("<p class=\"allgood\">要対応・要確認の項目はありませんでした。</p>");
            }
            else
            {
                sb.Append("<ul>");
                foreach (var i in bad.Concat(warn))
                {
                    sb.Append("<li>").Append(Badge(i.Status))
                      .Append("<b>").Append(H(i.Label)).Append("</b>");
                    if (!string.IsNullOrEmpty(i.Note))
                        sb.Append("<br><span class=\"sn\">").Append(H(i.Note)).Append("</span>");
                    sb.Append("</li>");
                }
                sb.Append("</ul>");
            }
            if (na.Count > 0)
            {
                sb.Append("<p class=\"nanote\">取得できなかった項目が ").Append(na.Count)
                  .Append(" 件あります（下に理由を書いています）。")
                  .Append("<b>取得できなかった項目は「問題なし」という意味ではありません。</b></p>");
            }
            sb.Append("</section>");

            foreach (var s in r.Sections)
            {
                sb.Append("<section><h2>").Append(H(s.Title)).Append("</h2>");
                if (!string.IsNullOrEmpty(s.Lead))
                    sb.Append("<p class=\"lead\">").Append(H(s.Lead)).Append("</p>");
                sb.Append("<table>");
                foreach (var i in s.Items)
                {
                    sb.Append("<tr class=\"").Append(i.Status.ToString().ToLowerInvariant()).Append("\">")
                      .Append("<th>").Append(H(i.Label)).Append("</th><td>");
                    sb.Append(Badge(i.Status));
                    if (i.Status == Status.Unavailable)
                        sb.Append("<span class=\"v na\">取得できませんでした（").Append(H(i.Reason)).Append("）</span>");
                    else
                        sb.Append("<span class=\"v\">").Append(H(i.Value)).Append("</span>");
                    if (!string.IsNullOrEmpty(i.Note))
                        sb.Append("<div class=\"note\">").Append(H(i.Note)).Append("</div>");
                    if (!string.IsNullOrEmpty(i.Source))
                        sb.Append("<div class=\"src\">取得元：").Append(H(i.Source)).Append("</div>");
                    sb.Append("</td></tr>");
                }
                sb.Append("</table></section>");
            }

            sb.Append("<footer>")
              .Append("<p>このレポートはこの PC の中だけで作られ、どこにも送信されていません。")
              .Append("ユーザー名・PC 名・IP アドレス・シリアル番号は含みません。</p>")
              .Append("<p>ブラウザの「印刷」→「PDF に保存」で PDF にできます。</p>")
              .Append("<p class=\"ver\">").Append(ReportBuilder.ProductName).Append(" v").Append(H(r.ToolVersion)).Append("　MicroForge</p>")
              .Append("</footer>");
            sb.Append("</main></body></html>");
            return sb.ToString();
        }

        const string Css = @"
:root{--bg:#f6f7f9;--card:#fff;--ink:#1d2330;--sub:#5b6474;--line:#e3e6eb;
--ok:#1f7a4d;--okb:#e5f4ec;--warn:#9a5b00;--warnb:#fff3dc;--bad:#b3261e;--badb:#fde7e5;--na:#5b6474;--nab:#eceef2;--info:#2b5fb3;--infob:#e8effb}
@media (prefers-color-scheme:dark){:root{--bg:#15181e;--card:#1e222a;--ink:#e7e9ee;--sub:#9aa3b2;--line:#2e3440;
--okb:#173a2a;--ok:#7fd3a6;--warnb:#3d2d0f;--warn:#f2c46b;--badb:#40191a;--bad:#ff9d94;--nab:#2a2f38;--na:#aab2bf;--infob:#1a2a45;--info:#9cbcf2}}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--ink);font-family:'Yu Gothic UI','Meiryo UI','Segoe UI',sans-serif;font-size:15px;line-height:1.6}
main{max-width:860px;margin:0 auto;padding:24px 16px 48px}
header{margin-bottom:16px}.brand{font-size:12px;letter-spacing:.12em;color:var(--sub);text-transform:uppercase}
h1{font-size:26px;margin:2px 0 4px}.meta{color:var(--sub);margin:0;font-size:13px}
section{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:16px 18px;margin:14px 0}
h2{font-size:17px;margin:0 0 6px}.lead{color:var(--sub);margin:0 0 10px;font-size:13px}
.summary ul{list-style:none;padding:0;margin:8px 0 0}.summary li{padding:8px 0;border-top:1px solid var(--line)}
.summary li:first-child{border-top:0}.sn{color:var(--sub);font-size:13px;display:block;margin-top:2px}
.allgood{color:var(--ok);font-weight:600;margin:6px 0}.nanote{font-size:13px;color:var(--sub);margin:10px 0 0}
table{width:100%;border-collapse:collapse}th,td{text-align:left;vertical-align:top;padding:9px 6px;border-top:1px solid var(--line)}
tr:first-child th,tr:first-child td{border-top:0}th{width:30%;font-weight:600;font-size:14px}
.b{display:inline-block;font-size:11.5px;font-weight:700;padding:1px 8px;border-radius:999px;margin-right:8px;white-space:nowrap;vertical-align:1px}
.b.ok{background:var(--okb);color:var(--ok)}.b.warn{background:var(--warnb);color:var(--warn)}.b.bad{background:var(--badb);color:var(--bad)}
.b.na{background:var(--nab);color:var(--na)}.b.info{background:var(--infob);color:var(--info)}
.v{overflow-wrap:anywhere}.v.na{color:var(--sub)}.note{font-size:13px;margin-top:4px;overflow-wrap:anywhere}
.src{font-size:11px;color:var(--sub);margin-top:3px;word-break:break-all}
th{overflow-wrap:anywhere}
tr.warn .note{color:var(--warn)}tr.bad .note{color:var(--bad)}
footer{color:var(--sub);font-size:12px;margin-top:20px}footer p{margin:3px 0}.ver{margin-top:8px}
/* 狭い画面では表をやめて縦積みにする。table のまま cell だけ block にすると
   表の最小幅が画面を超えて右が切れるので、table/tbody/tr ごと block にする */
@media (max-width:600px){main{padding:16px 12px 32px}h1{font-size:22px}section{padding:12px 14px}
table,tbody,tr,th,td{display:block;width:100%;max-width:100%}
th,td{padding:4px 0;border-top:0}th{padding-top:10px}td{padding-bottom:10px}
tr+tr th{border-top:1px solid var(--line)}}
@media print{:root{--bg:#fff;--card:#fff;--ink:#000;--sub:#444;--line:#ccc}
body{font-size:10.5pt}main{max-width:none;padding:0}
section{break-inside:avoid;page-break-inside:avoid;border-color:#bbb;padding:10px 12px;margin:8px 0}
.src{display:none}.b{border:1px solid currentColor}@page{size:A4;margin:14mm}}
";
    }
}

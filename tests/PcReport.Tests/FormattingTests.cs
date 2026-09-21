using System;
using Microforge.PcReport.Analysis;
using Xunit;

namespace Microforge.PcReport.Tests
{
    /// <summary>
    /// AC2：日付の解釈は now を注入して確かめる。
    /// ここが DateTime.Now を見ていると、テストが「いつ走らせたか」で結果を変える（Lesson #19）。
    /// </summary>
    public class FormattingTests
    {
        [Theory]
        [InlineData("2026-09-21", "2026-09-21", 0)]
        [InlineData("2026-09-20", "2026-09-21", 1)]
        [InlineData("2026-06-23", "2026-09-21", 90)]
        // 時計のずれ等で未来日付になっても、マイナス日数は出さない
        [InlineData("2026-09-25", "2026-09-21", 0)]
        public void 経過日数は基準時刻から数える(string target, string now, int expected)
        {
            Assert.Equal(expected,
                Formatting.DaysAgo(DateTime.Parse(target), DateTime.Parse(now)));
        }

        [Fact]
        public void 時刻の違いでは日数が動かない()
        {
            // 同じ日の 00:05 と 23:55 は、どちらも「今日」
            var now = new DateTime(2026, 9, 21, 23, 55, 0);
            Assert.Equal(0, Formatting.DaysAgo(new DateTime(2026, 9, 21, 0, 5, 0), now));
        }

        [Theory]
        [InlineData("2026-09-21", "今日")]
        [InlineData("2026-09-20", "1 日前")]
        [InlineData("2026-09-01", "20 日前")]
        public void 何日前かを日本語にする(string target, string expected)
        {
            Assert.Equal(expected,
                Formatting.Ago(DateTime.Parse(target), new DateTime(2026, 9, 21)));
        }

        [Fact]
        public void 日付は和暦風の区切りで出す()
        {
            Assert.Equal("2026年9月21日", Formatting.Date(new DateTime(2026, 9, 21)));
            Assert.Equal("2026年9月21日（今日）",
                Formatting.DateWithAgo(new DateTime(2026, 9, 21), new DateTime(2026, 9, 21)));
        }

        [Theory]
        [InlineData(0L, "0 GB")]
        [InlineData(1073741824L, "1 GB")]
        [InlineData(2000000000000L, "1862.6 GB")]
        public void 容量はGBで丸める(long bytes, string expected)
        {
            Assert.Equal(expected, Formatting.Gb(bytes));
        }

        [Fact]
        public void 使用時間は三桁区切りで出す()
        {
            Assert.Equal("3,076", Formatting.Thousands(3076));
        }

        [Fact]
        public void 長いタイトルは丸める()
        {
            Assert.Equal("abc", Formatting.Shorten("abc", 10));
            Assert.Equal("abcde…", Formatting.Shorten("abcdefghij", 5));
        }
    }
}

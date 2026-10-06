using System;
using System.IO;
using System.Text;
using Xunit;

namespace HalcyonTrajectoryLogTool.Tests
{
    public class BinUtilTests
    {
        [Fact]
        public void ReadInt_IsLittleEndianAndAdvances()
        {
            var b = new byte[] { 0x78, 0x56, 0x34, 0x12, 0xFF, 0xFF, 0xFF, 0xFF };
            int p = 0;
            Assert.Equal(0x12345678, BinUtil.ReadInt(b, ref p));
            Assert.Equal(4, p);
            Assert.Equal(-1, BinUtil.ReadInt(b, ref p));
            Assert.Equal(8, p);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1024)]
        [InlineData(-123456)]
        [InlineData(int.MaxValue)]
        [InlineData(int.MinValue)]
        public void WriteInt_RoundTripsWithReadInt(int v)
        {
            var b = new byte[6];
            int p = 1;
            BinUtil.WriteInt(b, ref p, v);
            Assert.Equal(5, p);
            Assert.Equal(BitConverter.GetBytes(v), new[] { b[1], b[2], b[3], b[4] });
            p = 1;
            Assert.Equal(v, BinUtil.ReadInt(b, ref p));
            Assert.Equal(0, b[0]);
            Assert.Equal(0, b[5]);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(1e-30f)]
        [InlineData(180.04f)]
        [InlineData(-1.5e-7f)]
        [InlineData(float.MaxValue)]
        [InlineData(float.PositiveInfinity)]
        public void WriteFloat_RoundTripsWithReadFloat(float v)
        {
            var b = new byte[8];
            BinUtil.WriteFloat(b, 2, v);
            Assert.Equal(BitConverter.GetBytes(v), new[] { b[2], b[3], b[4], b[5] });
            Assert.Equal(BitConverter.GetBytes(v), BitConverter.GetBytes(BinUtil.ReadFloat(b, 2)));
        }

        [Fact]
        public void ReadFloat_NaNStaysNaN()
        {
            var b = new byte[4];
            BinUtil.WriteFloat(b, 0, float.NaN);
            Assert.True(float.IsNaN(BinUtil.ReadFloat(b, 0)));
        }

        [Fact]
        public void ReadString_Ascii_StopsAtNullAndTrims()
        {
            var b = new byte[16];
            Encoding.ASCII.GetBytes(" 5.1 ").CopyTo(b, 0);
            b[6] = (byte)'X'; // after the terminator: ignored
            bool wide;
            Assert.Equal("5.1", BinUtil.ReadString(b, 0, 16, out wide));
            Assert.False(wide);
        }

        [Fact]
        public void ReadString_Utf16_IsDetected()
        {
            var b = new byte[16];
            Encoding.Unicode.GetBytes("VOSTL").CopyTo(b, 0);
            bool wide;
            Assert.Equal("VOSTL", BinUtil.ReadString(b, 0, 16, out wide));
            Assert.True(wide);
        }

        [Fact]
        public void ReadString_FullFieldWithoutTerminator()
        {
            var b = Encoding.ASCII.GetBytes("ABCDEFGH");
            bool wide;
            Assert.Equal("ABCDEFGH", BinUtil.ReadString(b, 0, 8, out wide));
        }

        [Fact]
        public void ReadString_ShortFieldIsNeverWide()
        {
            var b = new byte[] { (byte)'A', 0, (byte)'B' };
            bool wide;
            Assert.Equal("A", BinUtil.ReadString(b, 0, 3, out wide));
            Assert.False(wide);
        }

        [Fact]
        public void WriteString_ClearsFieldAndKeepsTerminator()
        {
            var b = new byte[20];
            for (int i = 0; i < b.Length; i++) b[i] = 0xEE;
            BinUtil.WriteString(b, 2, 16, "4.0", false);
            Assert.Equal(0xEE, b[1]);
            Assert.Equal(new byte[] { (byte)'4', (byte)'.', (byte)'0', 0 }, new[] { b[2], b[3], b[4], b[5] });
            for (int i = 5; i < 18; i++) Assert.Equal(0, b[i]);
            Assert.Equal(0xEE, b[18]);
        }

        [Fact]
        public void WriteString_TruncatesToLeaveTerminator()
        {
            var b = new byte[4];
            BinUtil.WriteString(b, 0, 4, "ABCDEFG", false);
            Assert.Equal(new byte[] { (byte)'A', (byte)'B', (byte)'C', 0 }, b);

            var w = new byte[6];
            BinUtil.WriteString(w, 0, 6, "ABCD", true);
            bool wide;
            Assert.Equal("AB", BinUtil.ReadString(w, 0, 6, out wide));
            Assert.Equal(0, w[4]);
            Assert.Equal(0, w[5]);
        }

        [Fact]
        public void WriteString_WideRoundTrips()
        {
            var b = new byte[16];
            BinUtil.WriteString(b, 0, 16, "5.1", true);
            bool wide;
            Assert.Equal("5.1", BinUtil.ReadString(b, 0, 16, out wide));
            Assert.True(wide);
        }

        [Theory]
        [InlineData("abc\0def", "abc")]
        [InlineData("abc", "abc")]
        [InlineData("\0abc", "")]
        [InlineData("", "")]
        public void UpToNull(string input, string expected)
        {
            Assert.Equal(expected, BinUtil.UpToNull(input));
        }
    }

    public class Crc16Tests
    {
        // Check values for the ASCII string "123456789", from the CRC RevEng catalogue
        // (https://reveng.sourceforge.io/crc-catalogue/16.htm).
        [Theory]
        [InlineData(0, "CRC-16/CCITT-FALSE", 0x29B1)]
        [InlineData(1, "CRC-16/MCRF4XX", 0x6F91)]
        [InlineData(2, "CRC-16/GENIBUS", 0xD64E)]
        [InlineData(3, "CRC-16/X-25", 0x906E)]
        public void Variants_MatchCatalogueCheckValues(int index, string name, int check)
        {
            Crc16.Variant v = Crc16.Variants[index];
            Assert.Equal(name, v.Name);
            Assert.Equal((ushort)check, v.Compute(Encoding.ASCII.GetBytes("123456789"), 9));
        }

        [Fact]
        public void Compute_EmptyInputIsSeedXorOut()
        {
            Assert.Equal(0xFFFF, Crc16.Variants[0].Compute(new byte[0], 0));
            Assert.Equal(0x0000, Crc16.Variants[3].Compute(new byte[0], 0));
        }

        [Fact]
        public void Compute_OnlyUsesTheGivenLength()
        {
            byte[] data = Encoding.ASCII.GetBytes("123456789XYZ");
            Assert.Equal(0x29B1, Crc16.Variants[0].Compute(data, 9));
        }

        [Fact]
        public void Detect_FindsEachVariant()
        {
            byte[] data = Encoding.ASCII.GetBytes("trajectory log");
            foreach (var v in Crc16.Variants)
                Assert.Same(v, Crc16.Detect(data, data.Length, v.Compute(data, data.Length)));
        }

        [Fact]
        public void Detect_ReturnsNullWhenNothingMatches()
        {
            byte[] data = Encoding.ASCII.GetBytes("123456789");
            Assert.Null(Crc16.Detect(data, 9, 0x1234));
        }
    }

    public class FmtTests
    {
        [Theory]
        [InlineData(101.9f, "101.9")]
        [InlineData(180f, "180")]
        [InlineData(-0.5f, "-0.5")]
        [InlineData(1.23456789f, "1.234568")]
        public void Num_UsesSevenSignificantDigits(float v, string expected)
        {
            Assert.Equal(expected, Fmt.Num(v));
        }

        [Theory]
        [InlineData(101.9f)]
        [InlineData(0.1f)]
        [InlineData(50000.02f)]
        [InlineData(1.17549435E-38f)]
        public void Exact_RoundTripsTheFloat(float v)
        {
            Assert.Equal(v, float.Parse(Fmt.Exact(v), System.Globalization.CultureInfo.InvariantCulture));
        }

        [Fact]
        public void Exact_ShowsFullPrecision()
        {
            Assert.Equal("101.900002", Fmt.Exact(101.9f));
        }

        [Theory]
        [InlineData(1.23456, 3, "1.235")]
        [InlineData(0, 3, "0.000")]
        [InlineData(-2.5, 1, "-2.5")]
        public void Fixed(double v, int decimals, string expected)
        {
            Assert.Equal(expected, Fmt.Fixed(v, decimals));
        }

        [Theory]
        [InlineData(0, "00:00:00.000")]
        [InlineData(50000.25, "13:53:20.250")]
        [InlineData(86399.999, "23:59:59.999")]
        [InlineData(86400.5, "00:00:00.500")] // past midnight: wraps to the next day
        public void Clock_FormatsTimeOfDay(double v, string expected)
        {
            Assert.Equal(expected, Fmt.Clock(v));
        }

        [Theory]
        [InlineData(-0.001)]
        [InlineData(172800)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void Clock_OutOfRangeIsEmpty(double v)
        {
            Assert.Equal("", Fmt.Clock(v));
        }

        [Theory]
        [InlineData(null, "")]
        [InlineData("plain", "plain")]
        [InlineData("a,b", "\"a,b\"")]
        [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
        [InlineData("line\nbreak", "\"line\nbreak\"")]
        [InlineData("cr\rhere", "\"cr\rhere\"")]
        public void CsvField_QuotesWhenNeeded(string input, string expected)
        {
            Assert.Equal(expected, Csv.Field(input));
        }
    }

    public class FileUtilTests
    {
        [Theory]
        [InlineData("Field1_20260716142318.bin", 14, 23, 18)]
        [InlineData(@"C:\logs\Arc_20250101000000.bin", 0, 0, 0)]
        [InlineData("a_20250101010101_b_20250101235959.bin", 23, 59, 59)] // last stamp wins
        [InlineData("a_20250101101010_b_20251399000000.bin", 10, 10, 10)] // invalid last stamp skipped
        public void TryTimeFromName_FindsStamp(string name, int h, int m, int s)
        {
            TimeSpan t;
            Assert.True(FileUtil.TryTimeFromName(name, out t));
            Assert.Equal(new TimeSpan(h, m, s), t);
        }

        [Theory]
        [InlineData("Field1.bin")]
        [InlineData("Field1_2026071614231.bin")]   // 13 digits
        [InlineData("Field1_202607161423181.bin")] // 15 digits
        [InlineData("Field1_20261316142318.bin")]  // month 13
        [InlineData("")]
        public void TryTimeFromName_NoValidStamp(string name)
        {
            TimeSpan t;
            Assert.False(FileUtil.TryTimeFromName(name, out t));
            Assert.Equal(TimeSpan.Zero, t);
        }

        [Fact]
        public void TryTimeFromName_IgnoresFolderNames()
        {
            TimeSpan t;
            Assert.False(FileUtil.TryTimeFromName(Path.Combine("20260716142318", "log.bin"), out t));
        }

        [Fact]
        public void WriteAtomic_CreatesFoldersAndLeavesNoTempFile()
        {
            using (var dir = new TempDir())
            {
                string path = Path.Combine(dir.Path, "a", "b", "out.bin");
                FileUtil.WriteAtomic(path, new byte[] { 1, 2, 3 });
                Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
                Assert.False(File.Exists(path + ".tmp"));
            }
        }

        [Fact]
        public void WriteAtomic_ReplacesExistingFile()
        {
            using (var dir = new TempDir())
            {
                string path = dir.Write("out.bin", new byte[] { 9, 9, 9, 9 });
                FileUtil.WriteAtomic(path, new byte[] { 1 });
                Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(path));
            }
        }
    }
}

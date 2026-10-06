using System;
using System.IO;
using System.Linq;
using Xunit;

namespace HalcyonTrajectoryLogTool.Tests
{
    public class TrajectoryLogParseTests
    {
        [Fact]
        public void Parse_V51_ReadsHeader()
        {
            var b = LogBuilder.V51(7);
            b.SamplingIntervalMs = 20;
            b.Truncated = 1;
            b.MlcModel = 4;
            var log = LogBuilder.Parse(b.Build());

            Assert.Equal("5.1", log.Version);
            Assert.False(log.VersionWide);
            Assert.Equal(20, log.SamplingIntervalMs);
            Assert.Equal(17, log.NumAxes);
            Assert.Equal(LogBuilder.V51Axes, log.AxisIds);
            Assert.Equal(116, log.SamplesPerAxis[16]);
            Assert.Equal(3, log.AxisScale);
            Assert.Equal(2, log.NumSubbeamsField);
            Assert.Equal(1, log.Truncated);
            Assert.Equal(7, log.NumSnapshots);
            Assert.Equal(4, log.MlcModel);
            Assert.Equal(LogBuilder.MetaOffset(17), log.MetaOffset);
            Assert.Equal(b.Metadata, log.MetaDataText);
            Assert.True(log.HasMachineInfo);
            Assert.Equal(1, log.MachineSpecifier);
            Assert.Equal("H12345", log.MachineSerial);
            Assert.Equal(0, log.TimeAxisIndex);
            Assert.Equal(1024 + 2 * 560, log.DataOffset);
            Assert.Equal((16 + 116) * 2, log.FloatsPerSnapshot);
            Assert.Same(Crc16.Variants[0], log.CrcVariant);
            Assert.Null(log.CrcWarning);
        }

        [Fact]
        public void Parse_V40_HasNoTimeAxisOrMachineInfo()
        {
            var log = LogBuilder.Parse(LogBuilder.V40().Build());
            Assert.Equal("4.0", log.Version);
            Assert.Equal(-1, log.TimeAxisIndex);
            Assert.False(log.HasMachineInfo);
            Assert.Equal(0, log.MachineSpecifier);
            Assert.Equal("", log.MachineSerial);
        }

        [Fact]
        public void Parse_ReadsSubbeams()
        {
            var log = LogBuilder.Parse(LogBuilder.V51().Build());
            Assert.Equal(2, log.Subbeams.Count);
            Subbeam s = log.Subbeams[1];
            Assert.Equal(2, s.ControlPoint);
            Assert.Equal(50.5f, s.MU);
            Assert.Equal(12.25f, s.RadTime);
            Assert.Equal(1, s.Seq);
            Assert.Equal("Arc 2", s.Name);
        }

        [Fact]
        public void Parse_AxisFloatOffsetsFollowSamples()
        {
            var b = new LogBuilder().SetAxes(43, 1, 50, 40);
            b.SetSamples(50, 4);
            var log = LogBuilder.Parse(b.Build());
            Assert.Equal(new[] { 0, 2, 4, 12 }, log.AxisFloatOffset);
            Assert.Equal((1 + 1 + 4 + 1) * 2, log.FloatsPerSnapshot);
        }

        [Fact]
        public void ExpectedAndActual_ReadTheRightValues()
        {
            var b = LogBuilder.V51(4);
            var log = LogBuilder.Parse(b.Build());
            for (int s = 0; s < 4; s++)
                for (int a = 0; a < log.NumAxes; a++)
                    for (int k = 0; k < log.SamplesPerAxis[a]; k++)
                    {
                        Assert.Equal(LogBuilder.DefaultValue(s, log.AxisIds[a], k, false), log.Expected(s, a, k));
                        Assert.Equal(LogBuilder.DefaultValue(s, log.AxisIds[a], k, true), log.Actual(s, a, k));
                    }
        }

        [Fact]
        public void Parse_ZeroSnapshotsAndNoSubbeams()
        {
            var b = LogBuilder.V51(0);
            b.Subbeams.Clear();
            var log = LogBuilder.Parse(b.Build());
            Assert.Equal(0, log.NumSnapshots);
            Assert.Empty(log.Subbeams);
            Assert.Equal(1024, log.DataOffset);
        }

        [Fact]
        public void Parse_WideStrings()
        {
            var b = LogBuilder.V51();
            b.Wide = true;
            var log = LogBuilder.Parse(b.Build());
            Assert.Equal("5.1", log.Version);
            Assert.True(log.VersionWide);
            Assert.Equal("Arc 1", log.Subbeams[0].Name);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void Parse_DetectsCrcVariant(int variant)
        {
            var b = LogBuilder.V51();
            b.Crc = Crc16.Variants[variant];
            Assert.Same(Crc16.Variants[variant], LogBuilder.Parse(b.Build()).CrcVariant);
        }

        [Fact]
        public void Parse_BadCrcThrows()
        {
            var b = LogBuilder.V51();
            b.CorruptCrc = true;
            var ex = Assert.Throws<InvalidDataException>(() => LogBuilder.Parse(b.Build()));
            Assert.Contains("--ignore-crc", ex.Message);
        }

        [Fact]
        public void Parse_BadCrcIgnoredGivesWarningAndDefaultVariant()
        {
            var b = LogBuilder.V51();
            b.CorruptCrc = true;
            var log = TrajectoryLog.Parse(b.Build(), true);
            Assert.Contains("does not verify", log.CrcWarning);
            Assert.Same(Crc16.Variants[0], log.CrcVariant);
        }

        [Fact]
        public void Parse_TooShort()
        {
            var ex = Assert.Throws<InvalidDataException>(() => TrajectoryLog.Parse(new byte[1025], false));
            Assert.Contains("too short", ex.Message);
        }

        [Fact]
        public void Parse_WrongSignature()
        {
            var b = LogBuilder.V51();
            b.Signature = "NOTVOSTL";
            var ex = Assert.Throws<InvalidDataException>(() => LogBuilder.Parse(b.Build()));
            Assert.Contains("Signature", ex.Message);
        }

        [Theory]
        [InlineData("3.0")]
        [InlineData("5.0")]
        [InlineData("")]
        public void Parse_UnsupportedVersion(string version)
        {
            var b = LogBuilder.V51();
            b.Version = version;
            var ex = Assert.Throws<InvalidDataException>(() => LogBuilder.Parse(b.Build()));
            Assert.Contains("Unsupported version", ex.Message);
        }

        [Fact]
        public void Parse_WrongHeaderSize()
        {
            var b = LogBuilder.V51();
            b.HeaderSizeField = 2048;
            var ex = Assert.Throws<InvalidDataException>(() => LogBuilder.Parse(b.Build()));
            Assert.Contains("Header size", ex.Message);
        }

        [Fact]
        public void Parse_NoAxes()
        {
            var b = new LogBuilder().SetAxes();
            var ex = Assert.Throws<InvalidDataException>(() => LogBuilder.Parse(b.Build()));
            Assert.Contains("number of axes", ex.Message);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1001)]
        public void Parse_ImplausibleSamplesPerAxis(int samples)
        {
            var b = new LogBuilder { NumSnapshots = 0 }.SetAxes(43, 1);
            b.SetSamples(1, samples);
            var ex = Assert.Throws<InvalidDataException>(() => LogBuilder.Parse(b.Build()));
            Assert.Contains("samples-per-axis", ex.Message);
        }

        [Fact]
        public void Parse_TwentySixAxesFit_TwentySevenOverflow()
        {
            var ids = Enumerable.Range(100, 26).ToArray();
            Assert.Equal(26, LogBuilder.Parse(new LogBuilder { NumSnapshots = 1 }.SetAxes(ids).Build()).NumAxes);

            var tooMany = Enumerable.Range(100, 27).ToArray();
            var ex = Assert.Throws<InvalidDataException>(() => LogBuilder.Parse(new LogBuilder { NumSnapshots = 1 }.SetAxes(tooMany).Build()));
            Assert.Contains("overflow", ex.Message);
        }

        [Fact]
        public void Parse_NegativeSnapshotCount()
        {
            var b = LogBuilder.V51(0);
            b.NumSnapshotsField = -1;
            var ex = Assert.Throws<InvalidDataException>(() => LogBuilder.Parse(b.Build()));
            Assert.Contains("Negative", ex.Message);
        }

        [Fact]
        public void Parse_LengthMismatch()
        {
            var b = LogBuilder.V51(3);
            b.NumSnapshotsField = 4;
            var ex = Assert.Throws<InvalidDataException>(() => LogBuilder.Parse(b.Build()));
            Assert.Contains("does not match header", ex.Message);
        }

        [Fact]
        public void Parse_KeepsRawBytes()
        {
            byte[] bytes = LogBuilder.V51().Build();
            Assert.Same(bytes, LogBuilder.Parse(bytes).Raw);
        }
    }

    public class TrajectoryLogVersionTests
    {
        [Fact]
        public void ReadVersion_ReadsOnlyTheFirst32Bytes()
        {
            using (var dir = new TempDir())
            {
                byte[] full = LogBuilder.V51().Build();
                var head = new byte[32];
                Array.Copy(full, head, 32);
                string path = dir.Write("head.bin", head);
                Assert.Equal("5.1", TrajectoryLog.ReadVersion(path));
                Assert.True(TrajectoryLog.IsVersion51(path));
                Assert.False(TrajectoryLog.IsVersion4(path));
            }
        }

        [Fact]
        public void ReadVersion_V40()
        {
            using (var dir = new TempDir())
            {
                string path = dir.Write("v4.bin", LogBuilder.V40().Build());
                Assert.Equal("4.0", TrajectoryLog.ReadVersion(path));
                Assert.True(TrajectoryLog.IsVersion4(path));
                Assert.False(TrajectoryLog.IsVersion51(path));
            }
        }

        [Fact]
        public void ReadVersion_DoesNotValidateTheVersionValue()
        {
            using (var dir = new TempDir())
            {
                var b = LogBuilder.V51();
                b.Version = "9.9";
                string path = dir.Write("v9.bin", b.Build());
                Assert.Equal("9.9", TrajectoryLog.ReadVersion(path));
            }
        }

        [Fact]
        public void ReadVersion_TooShort()
        {
            using (var dir = new TempDir())
            {
                string path = dir.Write("short.bin", new byte[31]);
                var ex = Assert.Throws<InvalidDataException>(() => TrajectoryLog.ReadVersion(path));
                Assert.Contains("31 bytes", ex.Message);
            }
        }

        [Fact]
        public void ReadVersion_WrongSignature()
        {
            using (var dir = new TempDir())
            {
                string path = dir.Write("bad.bin", new byte[64]);
                Assert.Throws<InvalidDataException>(() => TrajectoryLog.ReadVersion(path));
            }
        }
    }

    public class TrajectoryLogNameTests
    {
        [Theory]
        [InlineData(0, "CollRtn", "deg")]
        [InlineData(1, "GantryRtn", "deg")]
        [InlineData(2, "Y1", "cm")]
        [InlineData(3, "Y2", "cm")]
        [InlineData(4, "X1", "cm")]
        [InlineData(5, "X2", "cm")]
        [InlineData(6, "CouchVrt", "cm")]
        [InlineData(7, "CouchLng", "cm")]
        [InlineData(8, "CouchLat", "cm")]
        [InlineData(9, "CouchRtn", "deg")]
        [InlineData(10, "CouchPit", "deg")]
        [InlineData(11, "CouchRol", "deg")]
        [InlineData(40, "MU", "MU")]
        [InlineData(41, "BeamHold", "")]
        [InlineData(42, "ControlPoint", "")]
        [InlineData(43, "Time", "s")]
        [InlineData(50, "MLC", "cm")]
        [InlineData(99, "Axis99", "")]
        public void AxisNameAndUnit(int id, string name, string unit)
        {
            Assert.Equal(name, TrajectoryLog.AxisName(id));
            Assert.Equal(unit, TrajectoryLog.AxisUnit(id));
        }

        [Theory]
        [InlineData(9, 1, 180f)]
        [InlineData(9, 2, 0f)]
        [InlineData(9, 3, 180f)]
        [InlineData(10, 1, 0f)]
        [InlineData(10, 3, 0f)]
        [InlineData(11, 2, 0f)]
        [InlineData(11, 3, 0f)]
        public void CouchZeroPosition(int axisId, int scale, float expected)
        {
            Assert.Equal(expected, TrajectoryLog.CouchZeroPosition(axisId, scale));
        }

        [Theory]
        [InlineData(1, "Machine scale (couch in machine representation)")]
        [InlineData(2, "Modified IEC 61217")]
        [InlineData(3, "Machine scale (couch in isocentric representation)")]
        [InlineData(0, "Unknown")]
        [InlineData(4, "Unknown")]
        public void AxisScaleName(int scale, string expected)
        {
            Assert.Equal(expected, TrajectoryLog.AxisScaleName(scale));
        }

        [Theory]
        [InlineData(0f, "Normal")]
        [InlineData(1f, "Freeze")]
        [InlineData(2f, "Hold")]
        [InlineData(3f, "Disabled")]
        [InlineData(1.6f, "Hold")]
        [InlineData(4f, "")]
        [InlineData(-1f, "")]
        public void DoseServoName(float v, string expected)
        {
            Assert.Equal(expected, TrajectoryLog.DoseServoName(v));
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HalcyonTrajectoryLogTool.Tests
{
    public class LogComparerTests
    {
        static CompareResult Compare(LogBuilder a, LogBuilder b, double tolerance = 0, bool ignoreVersion = false, int maxDiffs = 10)
        {
            return LogComparer.Compare(LogBuilder.Parse(a.Build()), LogBuilder.Parse(b.Build()), tolerance, ignoreVersion, maxDiffs);
        }

        static LogBuilder WithValue(LogBuilder b, int snapshot, int axisId, int sample, bool actual, float value)
        {
            var inner = b.Value;
            b.Value = (s, id, k, act) => s == snapshot && id == axisId && k == sample && act == actual ? value : inner(s, id, k, act);
            return b;
        }

        [Fact]
        public void IdenticalBytes()
        {
            var r = Compare(LogBuilder.V51(), LogBuilder.V51());
            Assert.True(r.ByteIdentical);
            Assert.True(r.Same);
            Assert.Empty(r.Differences);
        }

        [Fact]
        public void SameContent_DifferentCrcVariant()
        {
            var b = LogBuilder.V51();
            b.Crc = Crc16.Variants[2];
            var r = Compare(LogBuilder.V51(), b);
            Assert.False(r.ByteIdentical);
            Assert.True(r.Same);
            Assert.Contains("CRC variant CRC-16/CCITT-FALSE vs CRC-16/GENIBUS", r.Info);
        }

        [Fact]
        public void HeaderFieldDifferences()
        {
            var a = LogBuilder.V51();
            var b = LogBuilder.V51();
            b.SamplingIntervalMs = 10;
            b.AxisScale = 1;
            b.Truncated = 1;
            b.MlcModel = 3;
            b.Metadata = "other";
            b.MachineSpecifier = 2;
            b.Serial = "X";
            var r = Compare(a, b);
            Assert.False(r.Same);
            Assert.Contains("Sampling interval (ms): 20 vs 10", r.Differences);
            Assert.Contains("Axis scale: 3 vs 1", r.Differences);
            Assert.Contains("Truncated: 0 vs 1", r.Differences);
            Assert.Contains("MLC model: 2 vs 3", r.Differences);
            Assert.Contains(r.Differences, d => d.StartsWith("Metadata: '") && d.EndsWith(" vs 'other'"));
            Assert.Contains("Machine specifier: 1 vs 2", r.Differences);
            Assert.Contains("Machine serial: 'H12345' vs 'X'", r.Differences);
            Assert.Equal(7, r.DifferenceCount);
        }

        [Fact]
        public void SnapshotAndSubbeamCounts()
        {
            var b = LogBuilder.V51(3);
            b.Subbeams.RemoveAt(1);
            var r = Compare(LogBuilder.V51(5), b);
            Assert.Contains("Number of subbeams: 2 vs 1", r.Differences);
            Assert.Contains("Number of snapshots: 5 vs 3", r.Differences);
        }

        [Fact]
        public void SubbeamDifferences()
        {
            var b = LogBuilder.V51();
            b.Subbeams[1] = new Subbeam { ControlPoint = 3, MU = 51f, RadTime = 13f, Seq = 2, Name = "Other" };
            var r = Compare(LogBuilder.V51(), b);
            Assert.Contains("Subbeam 2 control point: 2 vs 3", r.Differences);
            Assert.Contains("Subbeam 2 MU: 50.5 vs 51", r.Differences);
            Assert.Contains("Subbeam 2 radiation time: 12.25 vs 13", r.Differences);
            Assert.Contains("Subbeam 2 sequence: 1 vs 2", r.Differences);
            Assert.Contains("Subbeam 2 name: 'Arc 2' vs 'Other'", r.Differences);
            Assert.Equal(5, r.DifferenceCount);
        }

        [Fact]
        public void SubbeamFloats_WithinTolerance()
        {
            var b = LogBuilder.V51();
            b.Subbeams[0].MU = 100.001f;
            Assert.False(Compare(LogBuilder.V51(), b).Same);
            Assert.True(Compare(LogBuilder.V51(), b, 0.01).Same);
        }

        [Fact]
        public void ValueDifference_ReportedPerAxis()
        {
            var b = WithValue(LogBuilder.V51(), 2, 1, 0, true, 999f);
            var r = Compare(LogBuilder.V51(), b);
            Assert.Equal(1, r.DifferenceCount);
            Assert.StartsWith("GantryRtn: 1 value(s) differ in 1 snapshot(s), max |difference| ", r.Differences[0]);
            Assert.Contains("  snapshot 3, GantryRtn actual: 102.5 vs 999", r.Differences);
        }

        [Fact]
        public void MlcDifference_NamesTheLeaf()
        {
            var b = WithValue(LogBuilder.V51(), 0, 50, 2, false, -1f);
            var r = Compare(LogBuilder.V51(), b);
            Assert.Contains(r.Differences, d => d.StartsWith("  snapshot 1, MLC_Leaf001 expected:"));
        }

        [Fact]
        public void MultiSampleAxis_NamesTheSample()
        {
            var a = new LogBuilder { NumSnapshots = 1 }.SetAxes(43, 1).SetSamples(1, 3);
            var b = WithValue(new LogBuilder { NumSnapshots = 1 }.SetAxes(43, 1).SetSamples(1, 3), 0, 1, 1, false, 7f);
            var r = Compare(a, b);
            Assert.Contains(r.Differences, d => d.StartsWith("  snapshot 1, GantryRtn_2 expected:"));
        }

        [Fact]
        public void Tolerance()
        {
            var b = WithValue(LogBuilder.V51(), 1, 0, 0, false, LogBuilder.DefaultValue(1, 0, 0, false) + 0.004f);
            Assert.False(Compare(LogBuilder.V51(), b).Same);
            Assert.True(Compare(LogBuilder.V51(), b, 0.01).Same);
            Assert.False(Compare(LogBuilder.V51(), b, 0.001).Same);
        }

        [Fact]
        public void NaN_EqualsNaN()
        {
            var a = WithValue(LogBuilder.V51(), 0, 1, 0, false, float.NaN);
            var b = WithValue(LogBuilder.V51(), 0, 1, 0, false, float.NaN);
            b.Crc = Crc16.Variants[1]; // so the files are not byte-identical
            Assert.True(Compare(a, b).Same);
        }

        [Fact]
        public void NaN_DiffersFromNumber()
        {
            var b = WithValue(LogBuilder.V51(), 0, 1, 0, false, float.NaN);
            Assert.False(Compare(LogBuilder.V51(), b, 1e9).Same);
        }

        [Fact]
        public void MaxDiffs_LimitsListedValues()
        {
            var b = LogBuilder.V51(5);
            b.Value = (s, id, k, act) => id == 0 ? -5f : LogBuilder.DefaultValue(s, id, k, act);
            var r = Compare(LogBuilder.V51(5), b, 0, false, 3);
            Assert.Equal(10, r.DifferenceCount); // 5 snapshots x expected + actual
            Assert.Equal(1 + 3, r.Differences.Count);
            Assert.Contains("value differences listed up to --max-diffs 3", r.Info);
        }

        [Fact]
        public void MaxDiffsZero_ListsSummaryOnly()
        {
            var b = WithValue(LogBuilder.V51(), 0, 0, 0, true, -1f);
            var r = Compare(LogBuilder.V51(), b, 0, false, 0);
            Assert.Single(r.Differences);
            Assert.Empty(r.Info);
        }

        [Fact]
        public void V51VsV40_DiffersWithoutIgnoreVersion()
        {
            var v5 = LogBuilder.V51();
            var v4 = LogBuilder.V40();
            var r = Compare(v5, v4);
            Assert.Contains("Version: 5.1 vs 4.0", r.Differences);
            Assert.Contains(r.Differences, d => d.StartsWith("Axes: [Time, ControlPoint"));
        }

        [Fact]
        public void V51VsV40_SameWithIgnoreVersion()
        {
            var v4 = LogBuilder.V40();
            v4.MachineSpecifier = 0;
            var r = Compare(LogBuilder.V51(), v4, 0, true);
            Assert.True(r.Same, string.Join("\n", r.Differences));
            Assert.Contains("version 5.1 vs 4.0 ignored", r.Info);
            Assert.Contains("axis order differs, ignored", r.Info);
            Assert.Contains("time axis only in A, ignored", r.Info);
        }

        [Fact]
        public void IgnoreVersion_StillComparesValues()
        {
            var v4 = WithValue(LogBuilder.V40(), 0, 6, 0, false, -1f);
            var r = Compare(LogBuilder.V51(), v4, 0, true);
            Assert.False(r.Same);
            Assert.Contains(r.Differences, d => d.StartsWith("CouchVrt: 1 value(s) differ"));
        }

        [Fact]
        public void IgnoreVersion_DifferentAxisSetIsReported()
        {
            var v4 = LogBuilder.V40().RemoveAxes(9);
            var r = Compare(LogBuilder.V51(), v4, 0, true);
            Assert.False(r.Same);
            Assert.Contains(r.Differences, d => d.StartsWith("Axes: ["));
        }

        [Fact]
        public void AxisOrder_MattersWithoutIgnoreVersion()
        {
            var a = new LogBuilder { NumSnapshots = 1 }.SetAxes(43, 0, 1);
            var b = new LogBuilder { NumSnapshots = 1 }.SetAxes(43, 1, 0);
            var r = Compare(a, b);
            Assert.Contains("Axes: [Time, CollRtn, GantryRtn] vs [Time, GantryRtn, CollRtn]", r.Differences);
            Assert.Equal(1, r.DifferenceCount); // values match once axes are matched by id
        }

        [Fact]
        public void SamplesPerAxisDiffer()
        {
            var a = new LogBuilder { NumSnapshots = 1 }.SetAxes(43, 50).SetSamples(50, 4);
            var b = new LogBuilder { NumSnapshots = 1 }.SetAxes(43, 50).SetSamples(50, 6);
            var r = Compare(a, b);
            Assert.Contains("MLC samples per snapshot: 4 vs 6", r.Differences);
        }

        [Fact]
        public void OnlyCommonSnapshotsAreCompared()
        {
            var r = Compare(LogBuilder.V51(5), LogBuilder.V51(3));
            Assert.Equal(new List<string> { "Number of snapshots: 5 vs 3" }, r.Differences);
        }

        [Fact]
        public void Same_IsDifferenceCountZero()
        {
            var r = new CompareResult();
            Assert.True(r.Same);
            r.DifferenceCount = 1;
            Assert.False(r.Same);
        }
    }
}

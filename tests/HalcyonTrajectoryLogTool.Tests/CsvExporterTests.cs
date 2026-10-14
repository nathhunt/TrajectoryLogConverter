using System;
using System.Linq;
using Xunit;

namespace HalcyonTrajectoryLogTool.Tests
{
    public class CsvExporterTests
    {
        static string[][] Csv(LogBuilder b, Options opt = null)
        {
            string csv = CsvExporter.DataCsv(LogBuilder.Parse(b.Build()), opt ?? new Options());
            Assert.EndsWith("\r\n", csv);
            return csv.Split(new[] { "\r\n" }, StringSplitOptions.None).Where(l => l.Length > 0).Select(l => l.Split(',')).ToArray();
        }

        static string Cell(string[][] rows, int row, string column)
        {
            int c = Array.IndexOf(rows[0], column);
            Assert.True(c >= 0, "column " + column + " not found");
            return rows[row][c];
        }

        [Fact]
        public void V51_Header()
        {
            string[] h = Csv(LogBuilder.V51())[0];
            Assert.Equal(new[] { "Snapshot", "ElapsedTime_s", "ClockTime", "Subbeam", "ControlPoint",
                                 "MU_Expected", "MU_Actual", "BeamHold_Expected", "BeamHold_Actual", "DoseServoState",
                                 "GantryRtn_deg_Expected", "GantryRtn_deg_Actual", "CollRtn_deg_Expected", "CollRtn_deg_Actual",
                                 "CouchVrt_cm_Expected" },
                         h.Take(15));
            Assert.DoesNotContain("Time_s_Expected", h);
            Assert.DoesNotContain(h, c => c.StartsWith("Y1") || c.StartsWith("X2")); // jaws have no data
            Assert.Equal("MLC_Bank1_cm_Expected", h.First(c => c.StartsWith("MLC")));
            Assert.Contains("MLC_Leaf001_cm_Actual", h);
            Assert.Equal("MLC_Leaf114_cm_Actual", h.Last());
        }

        [Fact]
        public void V51_RowCountAndWidth()
        {
            string[][] rows = Csv(LogBuilder.V51(4));
            Assert.Equal(5, rows.Length);
            Assert.All(rows, r => Assert.Equal(rows[0].Length, r.Length));
        }

        [Fact]
        public void V51_TimeColumns()
        {
            string[][] rows = Csv(LogBuilder.V51(3));
            Assert.Equal("1", Cell(rows, 1, "Snapshot"));
            Assert.Equal("0.000", Cell(rows, 1, "ElapsedTime_s"));
            Assert.Equal("0.039", Cell(rows, 3, "ElapsedTime_s")); // 50000.04f - 50000f in float steps
            Assert.Equal("13:53:20.000", Cell(rows, 1, "ClockTime"));
        }

        [Fact]
        public void ElapsedTime_AcrossMidnight()
        {
            var b = LogBuilder.V51(2);
            b.Value = (s, id, k, act) => id == 43 ? (act ? 0f : (s == 0 ? 86399f : 1f)) : LogBuilder.DefaultValue(s, id, k, act);
            string[][] rows = Csv(b);
            Assert.Equal("2.000", Cell(rows, 2, "ElapsedTime_s"));
            Assert.Equal("00:00:01.000", Cell(rows, 2, "ClockTime"));
        }

        [Fact]
        public void V40_ElapsedTimeFromSamplingInterval()
        {
            var b = LogBuilder.V40(3);
            b.SamplingIntervalMs = 50;
            string[][] rows = Csv(b);
            Assert.DoesNotContain("ClockTime", rows[0]);
            Assert.Equal("0.100", Cell(rows, 3, "ElapsedTime_s"));
            Assert.Equal("CollRtn_deg_Expected", rows[0][3]); // v4.0 order, after Snapshot, ElapsedTime_s, Subbeam
        }

        [Fact]
        public void Values_ExpectedAndActual()
        {
            string[][] rows = Csv(LogBuilder.V51(3));
            Assert.Equal("102", Cell(rows, 3, "GantryRtn_deg_Expected"));
            Assert.Equal("102.5", Cell(rows, 3, "GantryRtn_deg_Actual"));
            Assert.Equal("4002", Cell(rows, 3, "MU_Expected"));
            Assert.Equal("5000.115", Cell(rows, 1, "MLC_Leaf114_cm_Expected"));
        }

        [Fact]
        public void ControlPoint_SingleColumn()
        {
            string[][] rows = Csv(LogBuilder.V51(5));
            Assert.DoesNotContain("ControlPoint_Expected", rows[0]);
            Assert.Equal("2", Cell(rows, 5, "ControlPoint"));
        }

        [Fact]
        public void Subbeam_FromControlPoint()
        {
            // Control point = snapshot / 2; subbeam 2 starts at control point 2.
            string[][] rows = Csv(LogBuilder.V51(6));
            Assert.Equal(new[] { "1", "1", "1", "1", "2", "2" }, Enumerable.Range(1, 6).Select(r => Cell(rows, r, "Subbeam")));
        }

        [Fact]
        public void Subbeam_ColumnLeftOutWithoutSubbeams()
        {
            var b = LogBuilder.V51();
            b.Subbeams.Clear();
            Assert.DoesNotContain("Subbeam", Csv(b)[0]);
        }

        [Fact]
        public void Subbeam_ColumnLeftOutWithoutControlPointAxis()
        {
            Assert.DoesNotContain("Subbeam", Csv(LogBuilder.V51().RemoveAxes(42))[0]);
        }

        [Fact]
        public void DoseServoState()
        {
            string[][] rows = Csv(LogBuilder.V51(4));
            Assert.Equal(new[] { "Normal", "Freeze", "Hold", "Disabled" }, Enumerable.Range(1, 4).Select(r => Cell(rows, r, "DoseServoState")));
        }

        [Fact]
        public void NoValueSample_IsEmptyCell()
        {
            var b = LogBuilder.V51(3);
            b.Value = (s, id, k, act) => id == 1 && s == 1 && act ? float.MaxValue
                                        : id == 0 && s == 2 && !act ? float.NaN
                                        : LogBuilder.DefaultValue(s, id, k, act);
            string[][] rows = Csv(b);
            Assert.Equal("", Cell(rows, 2, "GantryRtn_deg_Actual"));
            Assert.Equal("101", Cell(rows, 2, "GantryRtn_deg_Expected"));
            Assert.Equal("", Cell(rows, 3, "CollRtn_deg_Expected"));
        }

        [Fact]
        public void JawWithSomeData_IsKept()
        {
            var b = LogBuilder.V51(3);
            b.Value = (s, id, k, act) => id == 2 && s == 1 ? 5f : LogBuilder.DefaultValue(s, id, k, act);
            string[][] rows = Csv(b);
            Assert.Equal("", Cell(rows, 1, "Y1_cm_Expected"));
            Assert.Equal("5", Cell(rows, 2, "Y1_cm_Actual"));
            Assert.DoesNotContain("Y2_cm_Expected", rows[0]);
        }

        [Fact]
        public void NoMlc()
        {
            string[] h = Csv(LogBuilder.V51(), new Options { NoMlc = true })[0];
            Assert.DoesNotContain(h, c => c.StartsWith("MLC"));
            Assert.Equal("CouchRol_deg_Actual", h.Last());
        }

        [Fact]
        public void ActualOnly()
        {
            string[][] rows = Csv(LogBuilder.V51(2), new Options { ActualOnly = true });
            Assert.DoesNotContain(rows[0], c => c.EndsWith("_Expected"));
            Assert.Contains("GantryRtn_deg_Actual", rows[0]);
            Assert.Contains("ControlPoint", rows[0]);
            Assert.Equal("101.5", Cell(rows, 2, "GantryRtn_deg_Actual"));
            Assert.All(rows, r => Assert.Equal(rows[0].Length, r.Length));
        }

        [Fact]
        public void MultiSampleAxis_NumberedColumns()
        {
            var b = new LogBuilder { NumSnapshots = 1 }.SetAxes(43, 1).SetSamples(1, 2);
            string[] h = Csv(b)[0];
            Assert.Equal(new[] { "GantryRtn_deg_1_Expected", "GantryRtn_deg_1_Actual", "GantryRtn_deg_2_Expected", "GantryRtn_deg_2_Actual" },
                         h.Skip(3));
        }

        [Fact]
        public void UnknownAxis_NoUnit()
        {
            var b = new LogBuilder { NumSnapshots = 1 }.SetAxes(43, 77);
            Assert.Contains("Axis77_Expected", Csv(b)[0]);
        }

        [Fact]
        public void ZeroSnapshots_HeaderOnlyWithAllAxes()
        {
            string[][] rows = Csv(LogBuilder.V51(0));
            Assert.Single(rows);
            Assert.Contains("Y1_cm_Expected", rows[0]);
        }

        [Fact]
        public void AxesWithData()
        {
            var log = LogBuilder.Parse(LogBuilder.V51(3).Build());
            bool[] has = CsvExporter.AxesWithData(log);
            for (int a = 0; a < log.NumAxes; a++)
            {
                int id = log.AxisIds[a];
                Assert.Equal(!(id >= 2 && id <= 5), has[a]);
            }
        }

        static string[] FullLines(LogBuilder b)
        {
            string csv = CsvExporter.FullCsv(LogBuilder.Parse(b.Build()), null);
            Assert.EndsWith("\r\n", csv);
            return csv.Split(new[] { "\r\n" }, StringSplitOptions.None);
        }

        static string[] FullSnapshotHeader(string[] lines)
        {
            return lines.First(l => l.StartsWith("Snapshot,")).Split(',');
        }

        [Fact]
        public void Full_EveryAxisInFileOrder()
        {
            string[] h = FullSnapshotHeader(FullLines(LogBuilder.V51()));
            Assert.Equal(new[] { "Snapshot", "Time(43)_Exp", "Time(43)_Act", "ControlPoint(42)_Exp", "ControlPoint(42)_Act",
                                 "MU(40)_Exp", "MU(40)_Act" }, h.Take(7));
            Assert.Contains("Y1(2)_Exp", h);                 // jaws kept although they hold no value
            Assert.Contains("CouchRtn(9)_Act", h);
            Assert.Contains("CouchPit(10)_Exp", h);
            Assert.Contains("CouchRol(11)_Act", h);
            Assert.Equal("MLC(50)[1]_Exp", h.First(c => c.StartsWith("MLC")));
            Assert.Equal("MLC(50)[116]_Act", h.Last());
            Assert.Equal(1 + (LogBuilder.V51Axes.Length - 1 + LogBuilder.MlcSamples) * 2, h.Length);
        }

        [Fact]
        public void Full_ValuesExactAsStored()
        {
            var b = LogBuilder.V51(3);
            b.Value = (s, id, k, act) => id == 1 && s == 2 ? (act ? 101.899994f : 0.1f) : LogBuilder.DefaultValue(s, id, k, act);
            string[] lines = FullLines(b);
            string[] h = FullSnapshotHeader(lines);
            int start = Array.IndexOf(lines, string.Join(",", h));
            string[][] rows = lines.Skip(start + 1).Take(3).Select(l => l.Split(',')).ToArray();
            Assert.All(rows, r => Assert.Equal(h.Length, r.Length));
            Assert.Equal(new[] { "1", "2", "3" }, rows.Select(r => r[0]));
            Assert.Equal("101.899994", rows[2][Array.IndexOf(h, "GantryRtn(1)_Act")]);
            Assert.Equal("0.100000001", rows[2][Array.IndexOf(h, "GantryRtn(1)_Exp")]);
            Assert.Equal("3.40282347E+38", rows[0][Array.IndexOf(h, "Y1(2)_Exp")]);
            Assert.Equal("50000", rows[0][Array.IndexOf(h, "Time(43)_Exp")]);
            Assert.Equal("", lines[start + 4]);              // blank line ends the snapshot section
        }

        [Fact]
        public void Full_HeaderSubbeamsAndCrc()
        {
            string[] lines = FullLines(LogBuilder.V51(2));
            Assert.Equal("TRAJECTORY LOG FULL DUMP", lines[0]);
            Assert.Contains("0,Signature,VOSTL", lines);
            Assert.Contains("16,Version,5.1", lines);
            Assert.Contains("36,Sampling interval (ms),20", lines);
            Assert.Contains("40,Number of axes,17", lines);
            Assert.Contains("1,43,1,(Time)", lines);
            Assert.Contains("17,50,116,(MLC)", lines);
            Assert.Contains(lines, l => l.EndsWith(",Axis scale,3   (Machine scale (couch in isocentric representation))"));
            Assert.Contains(lines, l => l.EndsWith(",Number of snapshots,2"));
            Assert.Contains(",,Patient ID:\tPAT001", lines);
            Assert.Contains(",,Plan Name:\tPlan1", lines);
            Assert.Contains(lines, l => l.EndsWith(",Machine serial number,H12345   (hex: 48 31 32 33 34 35)"));
            Assert.Contains("1,1024,0,100,30,0,Arc 1,\"(32 bytes, all 0x00)\"", lines);
            Assert.Contains("2,1584,2,50.5,12.25,1,Arc 2,\"(32 bytes, all 0x00)\"", lines);
            Assert.Contains(lines, l => l.Contains(",CRC (stored),0x"));
            Assert.Contains(lines, l => l.Contains(",CRC check,\"(verifies, "));
        }

        [Fact]
        public void Full_CellsWithCommasAreQuoted()
        {
            var b = LogBuilder.V51(1);
            b.Metadata = "Plan Name:\tA, \"B\"\r\n";
            Assert.Contains(",,\"Plan Name:\tA, \"\"B\"\"\"", FullLines(b));
        }

        [Theory]
        [InlineData(0, 116, "MLC_Bank1")]
        [InlineData(1, 116, "MLC_Bank2")]
        [InlineData(2, 116, "MLC_Leaf001")]
        [InlineData(115, 116, "MLC_Leaf114")]
        [InlineData(2, 62, "MLC_Leaf01")]
        [InlineData(61, 62, "MLC_Leaf60")]
        [InlineData(101, 102, "MLC_Leaf100")]
        public void MlcColumn(int k, int total, string expected)
        {
            Assert.Equal(expected, CsvExporter.MlcColumn(k, total));
        }
    }
}

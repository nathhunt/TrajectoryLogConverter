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

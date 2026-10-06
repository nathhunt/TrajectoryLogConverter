using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace HalcyonTrajectoryLogTool.Tests
{
    public class V4ConverterTests
    {
        static TrajectoryLog ToV4(LogBuilder b, Options opt, out List<string> notes)
        {
            notes = new List<string>();
            var src = TrajectoryLog.Parse(b.Build(), opt.IgnoreCrc);
            return LogBuilder.Parse(V4Converter.Convert(src, opt, notes));
        }

        static TrajectoryLog ToV4(LogBuilder b, Options opt = null)
        {
            List<string> notes;
            return ToV4(b, opt ?? new Options(), out notes);
        }

        [Fact]
        public void Header_VersionAxesAndCounts()
        {
            var b = LogBuilder.V51(6);
            var v4 = ToV4(b);
            Assert.Equal("4.0", v4.Version);
            Assert.Equal(LogBuilder.V40Axes, v4.AxisIds);
            Assert.Equal(-1, v4.TimeAxisIndex);
            Assert.Equal(116, v4.SamplesPerAxis[v4.NumAxes - 1]);
            Assert.Equal(6, v4.NumSnapshots);
            Assert.Equal(b.SamplingIntervalMs, v4.SamplingIntervalMs);
            Assert.Equal(3, v4.AxisScale);
            Assert.Equal(b.MlcModel, v4.MlcModel);
            Assert.Equal(b.Metadata, v4.MetaDataText);
        }

        [Fact]
        public void Body_ValuesFollowTheirAxis()
        {
            var b = LogBuilder.V51(4);
            var src = LogBuilder.Parse(b.Build());
            var v4 = ToV4(b);
            for (int a = 0; a < v4.NumAxes; a++)
            {
                int ia = Array.IndexOf(src.AxisIds, v4.AxisIds[a]);
                for (int s = 0; s < 4; s++)
                    for (int k = 0; k < v4.SamplesPerAxis[a]; k++)
                    {
                        Assert.Equal(src.Expected(s, ia, k), v4.Expected(s, a, k));
                        Assert.Equal(src.Actual(s, ia, k), v4.Actual(s, a, k));
                    }
            }
        }

        [Fact]
        public void Output_LengthDropsTheTimeAxis()
        {
            var b = LogBuilder.V51(10);
            var notes = new List<string>();
            byte[] input = b.Build();
            byte[] output = V4Converter.Convert(LogBuilder.Parse(input), new Options(), notes);
            Assert.Equal(input.Length - 10 * 2 * 4, output.Length);
        }

        [Fact]
        public void Subbeams_CopiedVerbatim()
        {
            var b = LogBuilder.V51();
            byte[] input = b.Build();
            byte[] output = V4Converter.Convert(LogBuilder.Parse(input), new Options(), new List<string>());
            Assert.Equal(input.Skip(1024).Take(2 * 560), output.Skip(1024).Take(2 * 560));
        }

        [Fact]
        public void HeaderReservedBytesAreZero()
        {
            byte[] output = V4Converter.Convert(LogBuilder.Parse(LogBuilder.V51().Build()), new Options { KeepMachineInfo = true }, new List<string>());
            int end = LogBuilder.MetaOffset(16) + 745 + 7;
            Assert.All(output.Skip(end).Take(1024 - end), x => Assert.Equal(0, x));
        }

        [Fact]
        public void MachineInfo_ZeroedByDefault()
        {
            List<string> notes;
            var v4 = ToV4(LogBuilder.V51(), new Options(), out notes);
            Assert.Equal(0, v4.MachineSpecifier);
            Assert.Equal("", v4.MachineSerial);
            Assert.Contains(notes, n => n.Contains("Machine specifier 1 / serial 'H12345' removed"));
        }

        [Fact]
        public void MachineInfo_Kept()
        {
            List<string> notes;
            var v4 = ToV4(LogBuilder.V51(), new Options { KeepMachineInfo = true }, out notes);
            Assert.Equal(1, v4.MachineSpecifier);
            Assert.Equal("H12345", v4.MachineSerial);
            Assert.DoesNotContain(notes, n => n.Contains("removed from header"));
        }

        [Fact]
        public void AxisScale_KeptWithNote()
        {
            List<string> notes;
            var v4 = ToV4(LogBuilder.V51(), new Options(), out notes);
            Assert.Equal(3, v4.AxisScale);
            Assert.Contains(notes, n => n.Contains("Axis scale kept at 3"));
        }

        [Fact]
        public void AxisScale_OverrideWarns()
        {
            List<string> notes;
            var v4 = ToV4(LogBuilder.V51(), new Options { AxisScale = 1 }, out notes);
            Assert.Equal(1, v4.AxisScale);
            Assert.Contains(notes, n => n.StartsWith("WARNING: axis-scale flag changed 3 -> 1"));
        }

        [Fact]
        public void AxisScale_OverrideToSameValueDoesNotWarn()
        {
            List<string> notes;
            ToV4(LogBuilder.V51(), new Options { AxisScale = 3 }, out notes);
            Assert.DoesNotContain(notes, n => n.StartsWith("WARNING"));
        }

        [Fact]
        public void Notes_SummaryAndReorder()
        {
            List<string> notes;
            ToV4(LogBuilder.V51(3), new Options(), out notes);
            Assert.Contains(notes, n => n.StartsWith("17 axes -> 16, 2 subbeam(s), 3 snapshots"));
            Assert.Contains(notes, n => n.StartsWith("Axes reordered to the v4.0 order: CollRtn, GantryRtn, Y1"));
        }

        [Fact]
        public void AlreadyInV4Order_NoReorderNote()
        {
            var b = LogBuilder.V51();
            b.SetAxes(new[] { 43 }.Concat(LogBuilder.V40Axes).ToArray());
            List<string> notes;
            var v4 = ToV4(b, new Options(), out notes);
            Assert.Equal(LogBuilder.V40Axes, v4.AxisIds);
            Assert.DoesNotContain(notes, n => n.StartsWith("Axes reordered"));
        }

        [Fact]
        public void TimeAxisInTheMiddle_IsRemoved()
        {
            var b = new LogBuilder { NumSnapshots = 3 }.SetAxes(0, 1, 43, 40);
            var v4 = ToV4(b);
            Assert.Equal(new[] { 0, 1, 40 }, v4.AxisIds);
            Assert.Equal(LogBuilder.DefaultValue(2, 40, 0, true), v4.Actual(2, 2, 0));
        }

        [Fact]
        public void NoTimeAxis_Noted()
        {
            var b = new LogBuilder { NumSnapshots = 2 }.SetAxes(0, 1, 40);
            List<string> notes;
            var v4 = ToV4(b, new Options(), out notes);
            Assert.Equal(new[] { 0, 1, 40 }, v4.AxisIds);
            Assert.Contains(notes, n => n.StartsWith("No time axis (43) found"));
        }

        [Fact]
        public void UnknownAxes_FollowInInputOrder()
        {
            var b = new LogBuilder { NumSnapshots = 2 }.SetAxes(43, 77, 42, 1, 66, 0);
            var v4 = ToV4(b);
            Assert.Equal(new[] { 0, 1, 42, 77, 66 }, v4.AxisIds);
            Assert.Equal(LogBuilder.DefaultValue(1, 66, 0, false), v4.Expected(1, 4, 0));
        }

        [Fact]
        public void MultiSampleAxes_CopiedWhole()
        {
            var b = new LogBuilder { NumSnapshots = 3 }.SetAxes(43, 50, 1);
            b.SetSamples(50, 6);
            var v4 = ToV4(b);
            Assert.Equal(new[] { 1, 50 }, v4.AxisIds);
            Assert.Equal(new[] { 1, 6 }, v4.SamplesPerAxis);
            for (int k = 0; k < 6; k++)
                Assert.Equal(LogBuilder.DefaultValue(2, 50, k, true), v4.Actual(2, 1, k));
        }

        [Fact]
        public void DropCouchRotations()
        {
            List<string> notes;
            var v4 = ToV4(LogBuilder.V51(), new Options { DropCouchRotations = true }, out notes);
            Assert.DoesNotContain(9, v4.AxisIds);
            Assert.DoesNotContain(10, v4.AxisIds);
            Assert.DoesNotContain(11, v4.AxisIds);
            Assert.Equal(13, v4.NumAxes);
            Assert.Contains(notes, n => n == "Couch axes removed: CouchRtn, CouchPit, CouchRol.");
            Assert.Contains(notes, n => n.StartsWith("17 axes -> 13"));
        }

        [Fact]
        public void DropCouchRotations_NoneToRemove()
        {
            List<string> notes;
            ToV4(LogBuilder.V51().RemoveAxes(9, 10, 11), new Options { DropCouchRotations = true }, out notes);
            Assert.Contains(notes, n => n.StartsWith("No couch rotation / pitch / roll axes to remove"));
        }

        [Fact]
        public void CrcVariant_IsPreserved()
        {
            var b = LogBuilder.V51();
            b.Crc = Crc16.Variants[3];
            List<string> notes;
            var v4 = ToV4(b, new Options(), out notes);
            Assert.Same(Crc16.Variants[3], v4.CrcVariant);
            Assert.Contains(notes, n => n.Contains("Input CRC matched CRC-16/X-25"));
        }

        [Fact]
        public void BadInputCrc_WrittenWithDefaultVariant()
        {
            var b = LogBuilder.V51();
            b.CorruptCrc = true;
            List<string> notes;
            var v4 = ToV4(b, new Options { IgnoreCrc = true }, out notes);
            Assert.Same(Crc16.Variants[0], v4.CrcVariant);
            Assert.Contains(notes, n => n.StartsWith("WARNING: Input CRC") && n.Contains("CRC-16/CCITT-FALSE"));
        }

        [Fact]
        public void WideVersionString_StaysWide()
        {
            var b = LogBuilder.V51();
            b.Wide = true;
            var v4 = ToV4(b);
            Assert.Equal("4.0", v4.Version);
            Assert.True(v4.VersionWide);
        }

        [Fact]
        public void ZeroSnapshots()
        {
            var v4 = ToV4(LogBuilder.V51(0));
            Assert.Equal(0, v4.NumSnapshots);
            Assert.Equal(16, v4.NumAxes);
        }
    }

    public class V5ConverterTests
    {
        static TrajectoryLog ToV5(LogBuilder b, Options opt, string path, out List<string> notes)
        {
            notes = new List<string>();
            var src = TrajectoryLog.Parse(b.Build(), opt.IgnoreCrc);
            return LogBuilder.Parse(V5Converter.Convert(src, opt, path, notes));
        }

        static TrajectoryLog ToV5(LogBuilder b, Options opt = null, string path = "log.bin")
        {
            List<string> notes;
            return ToV5(b, opt ?? new Options(), path, out notes);
        }

        [Fact]
        public void Header_VersionAndAxisOrder()
        {
            var v5 = ToV5(LogBuilder.V40(4));
            Assert.Equal("5.1", v5.Version);
            Assert.Equal(LogBuilder.V51Axes, v5.AxisIds);
            Assert.Equal(0, v5.TimeAxisIndex);
            Assert.Equal(1, v5.SamplesPerAxis[0]);
            Assert.Equal(116, v5.SamplesPerAxis[16]);
            Assert.Equal(4, v5.NumSnapshots);
        }

        [Fact]
        public void Body_ValuesFollowTheirAxis()
        {
            var b = LogBuilder.V40(4);
            var src = LogBuilder.Parse(b.Build());
            var v5 = ToV5(b);
            for (int a = 1; a < v5.NumAxes; a++)
            {
                int ia = Array.IndexOf(src.AxisIds, v5.AxisIds[a]);
                for (int s = 0; s < 4; s++)
                    for (int k = 0; k < v5.SamplesPerAxis[a]; k++)
                    {
                        Assert.Equal(src.Expected(s, ia, k), v5.Expected(s, a, k));
                        Assert.Equal(src.Actual(s, ia, k), v5.Actual(s, a, k));
                    }
            }
        }

        [Fact]
        public void AlreadyHasTimeAxis_Throws()
        {
            var src = LogBuilder.Parse(LogBuilder.V51().Build());
            var ex = Assert.Throws<InvalidDataException>(() => V5Converter.Convert(src, new Options(), "x.bin", new List<string>()));
            Assert.Contains("already has a time axis", ex.Message);
        }

        [Fact]
        public void Time_FromFileNameStamp()
        {
            List<string> notes;
            var v5 = ToV5(LogBuilder.V40(3), new Options(), "Field1_20260716142318.bin", out notes);
            double start = 14 * 3600 + 23 * 60 + 18;
            for (int s = 0; s < 3; s++)
            {
                Assert.Equal((float)(start + s * 0.02), v5.Expected(s, 0, 0));
                Assert.Equal(0f, v5.Actual(s, 0, 0));
            }
            Assert.Contains(notes, n => n.StartsWith("Time axis generated: 14:23:18.000 (file name) + snapshot x 20 ms"));
        }

        [Fact]
        public void Time_StartTimeOptionWinsOverFileName()
        {
            List<string> notes;
            var v5 = ToV5(LogBuilder.V40(2), new Options { StartTime = 3600 }, "Field1_20260716142318.bin", out notes);
            Assert.Equal(3600f, v5.Expected(0, 0, 0));
            Assert.Equal(3600.02f, v5.Expected(1, 0, 0));
            Assert.Contains(notes, n => n.Contains("01:00:00.000 (--start-time)"));
        }

        [Fact]
        public void Time_DefaultsToMidnight()
        {
            List<string> notes;
            var v5 = ToV5(LogBuilder.V40(2), new Options(), "Field1.bin", out notes);
            Assert.Equal(0f, v5.Expected(0, 0, 0));
            Assert.Equal(0.02f, v5.Expected(1, 0, 0));
            Assert.Contains(notes, n => n.Contains("(no time stamp in file name)"));
        }

        [Fact]
        public void Time_UsesSamplingInterval()
        {
            var b = LogBuilder.V40(3);
            b.SamplingIntervalMs = 100;
            var v5 = ToV5(b, new Options { StartTime = 10 });
            Assert.Equal(10.2f, v5.Expected(2, 0, 0));
        }

        [Fact]
        public void Time_WrapsAtMidnight()
        {
            var v5 = ToV5(LogBuilder.V40(3), new Options { StartTime = 86399.98 });
            Assert.Equal((float)86399.98, v5.Expected(0, 0, 0));
            Assert.Equal(0f, v5.Expected(1, 0, 0), 3);
            Assert.Equal(0.02f, v5.Expected(2, 0, 0), 3);
        }

        [Fact]
        public void Time_FromCsvExactColumns()
        {
            using (var dir = new TempDir())
            {
                string csv = dir.File("t.csv");
                File.WriteAllText(csv, "Snapshot,SecondsSinceMidnight,Clock,Expected,Actual\r\n" +
                                       "1,100.000,00:01:40.000,100.012345,1.5\r\n" +
                                       "\r\n" +
                                       "2,100.020,00:01:40.020,100.032345,2.5\r\n");
                List<string> notes;
                var v5 = ToV5(LogBuilder.V40(2), new Options { TimeFrom = csv }, "log.bin", out notes);
                Assert.Equal(100.012345f, v5.Expected(0, 0, 0));
                Assert.Equal(1.5f, v5.Actual(0, 0, 0));
                Assert.Equal(100.032345f, v5.Expected(1, 0, 0));
                Assert.Equal(2.5f, v5.Actual(1, 0, 0));
                Assert.Contains(notes, n => n == "Time axis restored from " + csv);
            }
        }

        [Fact]
        public void Time_FromOldCsvWithoutExactColumns()
        {
            using (var dir = new TempDir())
            {
                string csv = dir.File("t.csv");
                File.WriteAllText(csv, "Snapshot,SecondsSinceMidnight,Clock\n1,100.000,00:01:40.000\n2,100.020,00:01:40.020\n");
                var v5 = ToV5(LogBuilder.V40(2), new Options { TimeFrom = csv });
                Assert.Equal(100f, v5.Expected(0, 0, 0));
                Assert.Equal(100.02f, v5.Expected(1, 0, 0));
                Assert.Equal(0f, v5.Actual(1, 0, 0));
            }
        }

        [Fact]
        public void Time_CsvRowCountMismatch()
        {
            using (var dir = new TempDir())
            {
                string csv = dir.File("t.csv");
                File.WriteAllText(csv, "header\n1,1\n");
                var src = LogBuilder.Parse(LogBuilder.V40(2).Build());
                var ex = Assert.Throws<InvalidDataException>(() => V5Converter.Convert(src, new Options { TimeFrom = csv }, "x.bin", new List<string>()));
                Assert.Contains("1 rows but the log has 2 snapshots", ex.Message);
            }
        }

        [Fact]
        public void Time_CsvInvalidRow()
        {
            using (var dir = new TempDir())
            {
                string csv = dir.File("t.csv");
                File.WriteAllText(csv, "header\n1,1\n2,abc\n");
                var src = LogBuilder.Parse(LogBuilder.V40(2).Build());
                var ex = Assert.Throws<InvalidDataException>(() => V5Converter.Convert(src, new Options { TimeFrom = csv }, "x.bin", new List<string>()));
                Assert.Contains("row 3 is not valid", ex.Message);
            }
        }

        [Fact]
        public void MachineSpecifier_SetWhenZeroed()
        {
            List<string> notes;
            var v5 = ToV5(LogBuilder.V40(), new Options(), "log.bin", out notes);
            Assert.Equal(1, v5.MachineSpecifier);
            Assert.Equal("", v5.MachineSerial);
            Assert.Contains(notes, n => n.StartsWith("Machine specifier set to 1"));
        }

        [Fact]
        public void MachineInfo_CopiedWhenPresent()
        {
            var b = LogBuilder.V40();
            b.MachineSpecifier = 2;
            b.Serial = "ABC";
            List<string> notes;
            var v5 = ToV5(b, new Options(), "log.bin", out notes);
            Assert.Equal(2, v5.MachineSpecifier);
            Assert.Equal("ABC", v5.MachineSerial);
            Assert.DoesNotContain(notes, n => n.StartsWith("Machine specifier set"));
        }

        [Fact]
        public void Serial_OptionReplacesSerial()
        {
            var b = LogBuilder.V40();
            b.MachineSpecifier = 1;
            b.Serial = "OLDSER";
            var v5 = ToV5(b, new Options { Serial = "NEW" });
            Assert.Equal("NEW", v5.MachineSerial);
            Assert.Equal(1, v5.MachineSpecifier);
        }

        [Fact]
        public void Metadata_Copied()
        {
            var b = LogBuilder.V40();
            b.Metadata = "Patient ID:\tXYZ\r\n";
            Assert.Equal("Patient ID:\tXYZ\r\n", ToV5(b).MetaDataText);
        }

        [Fact]
        public void AxisScale3_NoWarning()
        {
            List<string> notes;
            ToV5(LogBuilder.V40(), new Options(), "log.bin", out notes);
            Assert.DoesNotContain(notes, n => n.StartsWith("WARNING"));
        }

        [Fact]
        public void AxisScaleNot3_Warns()
        {
            var b = LogBuilder.V40();
            b.AxisScale = 1;
            List<string> notes;
            var v5 = ToV5(b, new Options(), "log.bin", out notes);
            Assert.Equal(1, v5.AxisScale);
            Assert.Contains(notes, n => n.StartsWith("WARNING: axis scale kept at 1"));
        }

        [Fact]
        public void AxisScaleOverride_Warns()
        {
            List<string> notes;
            var v5 = ToV5(LogBuilder.V40(), new Options { AxisScale = 2 }, "log.bin", out notes);
            Assert.Equal(2, v5.AxisScale);
            Assert.Contains(notes, n => n.StartsWith("WARNING: axis-scale flag changed 3 -> 2"));
        }

        [Theory]
        [InlineData(1, 180f)]
        [InlineData(2, 0f)]
        [InlineData(3, 180f)]
        public void AddCouchRotations_AtZeroPosition(int scale, float rotation)
        {
            var b = LogBuilder.V40(3).RemoveAxes(9, 10, 11);
            b.AxisScale = scale;
            List<string> notes;
            var v5 = ToV5(b, new Options { AddCouchRotations = true }, "log.bin", out notes);
            Assert.Equal(LogBuilder.V51Axes, v5.AxisIds);
            int rtn = Array.IndexOf(v5.AxisIds, 9), pit = Array.IndexOf(v5.AxisIds, 10), rol = Array.IndexOf(v5.AxisIds, 11);
            for (int s = 0; s < 3; s++)
            {
                Assert.Equal(rotation, v5.Expected(s, rtn, 0));
                Assert.Equal(rotation, v5.Actual(s, rtn, 0));
                Assert.Equal(0f, v5.Expected(s, pit, 0));
                Assert.Equal(0f, v5.Actual(s, rol, 0));
            }
            // Axes after the added ones still have their own values.
            int mlc = Array.IndexOf(v5.AxisIds, 50);
            Assert.Equal(LogBuilder.DefaultValue(2, 50, 115, true), v5.Actual(2, mlc, 115));
            Assert.Contains(notes, n => n.StartsWith("Couch axes added at the zero position for axis scale " + scale));
        }

        [Fact]
        public void AddCouchRotations_UsesOverriddenAxisScale()
        {
            var b = LogBuilder.V40(1).RemoveAxes(9);
            var v5 = ToV5(b, new Options { AddCouchRotations = true, AxisScale = 2 });
            Assert.Equal(0f, v5.Expected(0, Array.IndexOf(v5.AxisIds, 9), 0));
        }

        [Fact]
        public void AddCouchRotations_OnlyMissingOnes()
        {
            var b = LogBuilder.V40(2).RemoveAxes(10);
            List<string> notes;
            var v5 = ToV5(b, new Options { AddCouchRotations = true }, "log.bin", out notes);
            Assert.Equal(LogBuilder.DefaultValue(1, 9, 0, false), v5.Expected(1, Array.IndexOf(v5.AxisIds, 9), 0));
            Assert.Equal(0f, v5.Expected(1, Array.IndexOf(v5.AxisIds, 10), 0));
            Assert.Contains(notes, n => n == "Couch axes added at the zero position for axis scale 3: CouchPit = 0.");
        }

        [Fact]
        public void AddCouchRotations_AlreadyPresent()
        {
            List<string> notes;
            var v5 = ToV5(LogBuilder.V40(), new Options { AddCouchRotations = true }, "log.bin", out notes);
            Assert.Equal(17, v5.NumAxes);
            Assert.Contains(notes, n => n.StartsWith("Couch rotation / pitch / roll axes already present"));
        }

        [Fact]
        public void WithoutAddCouchRotations_MissingAxesStayMissing()
        {
            var v5 = ToV5(LogBuilder.V40().RemoveAxes(9, 10, 11));
            Assert.Equal(14, v5.NumAxes);
            Assert.DoesNotContain(9, v5.AxisIds);
        }

        [Fact]
        public void UnknownAxes_FollowInInputOrder()
        {
            var b = new LogBuilder { Version = "4.0", NumSnapshots = 2 }.SetAxes(77, 0, 66, 42);
            var v5 = ToV5(b);
            Assert.Equal(new[] { 43, 42, 0, 77, 66 }, v5.AxisIds);
        }

        [Fact]
        public void NoRoomForTimeAxis_Throws()
        {
            var b = new LogBuilder { Version = "4.0", NumSnapshots = 1 }.SetAxes(Enumerable.Range(100, 26).ToArray());
            var src = LogBuilder.Parse(b.Build());
            var ex = Assert.Throws<InvalidDataException>(() => V5Converter.Convert(src, new Options(), "x.bin", new List<string>()));
            Assert.Contains("No room", ex.Message);
        }

        [Fact]
        public void CrcVariant_IsPreserved()
        {
            var b = LogBuilder.V40();
            b.Crc = Crc16.Variants[1];
            Assert.Same(Crc16.Variants[1], ToV5(b).CrcVariant);
        }

        [Fact]
        public void Notes_Summary()
        {
            List<string> notes;
            ToV5(LogBuilder.V40(3), new Options(), "log.bin", out notes);
            Assert.Contains(notes, n => n.StartsWith("16 axes -> 17, 2 subbeam(s), 3 snapshots"));
            Assert.Contains(notes, n => n.StartsWith("Axes reordered to the v5.1 order: Time, ControlPoint, MU, BeamHold, GantryRtn, CollRtn"));
        }
    }

    public class RoundTripTests
    {
        [Fact]
        public void V51_ToV4_ToV5_IsByteIdentical()
        {
            using (var dir = new TempDir())
            {
                var b = LogBuilder.V51(25);
                b.Value = (s, id, k, act) => id == 43
                    ? (act ? 0f : 50000.123f + s * 0.0217f) // irregular times: only an exact restore reproduces them
                    : LogBuilder.DefaultValue(s, id, k, act);
                byte[] original = b.Build();
                string input = dir.Write("Field_20260716142318.bin", original);
                string mid = dir.Sub("v4"), output = dir.Sub("rt");

                var opt4 = Options.Parse(new[] { "to-v4", input, "-o", mid, "--time-csv", "--keep-machine-info" });
                Program.RunToV4(input, false, opt4);
                string v4 = Path.Combine(mid, "Field_20260716142318.bin");
                string csv = Path.Combine(mid, "Field_20260716142318_time.csv");
                Assert.True(File.Exists(csv));

                var opt5 = Options.Parse(new[] { "to-v5", v4, "-o", output, "--time-from", csv });
                Program.RunToV5(v4, false, opt5);
                byte[] roundTrip = File.ReadAllBytes(Path.Combine(output, "Field_20260716142318.bin"));
                Assert.Equal(original, roundTrip);
            }
        }

        [Fact]
        public void V40_ToV5_ToV4_IsByteIdentical()
        {
            var b = LogBuilder.V40(12);
            byte[] original = b.Build();
            byte[] v5 = V5Converter.Convert(LogBuilder.Parse(original), new Options(), "Field.bin", new List<string>());
            byte[] v4 = V4Converter.Convert(LogBuilder.Parse(v5), new Options(), new List<string>());
            Assert.Equal(original, v4);
        }

        [Fact]
        public void V40_DropAndAddCouchRotations_RoundTrip()
        {
            // v4.0 without couch rotations -> v5.1 with couch axes added -> v4.0 with them dropped.
            var b = LogBuilder.V40(4).RemoveAxes(9, 10, 11);
            byte[] original = b.Build();
            byte[] v5 = V5Converter.Convert(LogBuilder.Parse(original), new Options { AddCouchRotations = true }, "x.bin", new List<string>());
            Assert.Equal(17, LogBuilder.Parse(v5).NumAxes);
            byte[] v4 = V4Converter.Convert(LogBuilder.Parse(v5), new Options { DropCouchRotations = true }, new List<string>());
            Assert.Equal(original, v4);
        }

        [Fact]
        public void V51_ToV4_ComparesSameIgnoringVersion()
        {
            byte[] original = LogBuilder.V51(8).Build();
            byte[] v4 = V4Converter.Convert(LogBuilder.Parse(original), new Options(), new List<string>());
            var r = LogComparer.Compare(LogBuilder.Parse(original), LogBuilder.Parse(v4), 0, true);
            Assert.True(r.Same, string.Join("\n", r.Differences));
        }
    }
}

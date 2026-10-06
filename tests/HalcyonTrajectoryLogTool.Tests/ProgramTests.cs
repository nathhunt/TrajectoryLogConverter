using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace HalcyonTrajectoryLogTool.Tests
{
    /// <summary>The per-file commands (Program.Run*) and their output files.</summary>
    public class ProgramRunTests
    {
        static Options Opt(params string[] args) { return Options.Parse(args); }

        [Fact]
        public void ToV4_DefaultOutputIsV40Subfolder()
        {
            using (var dir = new TempDir())
            {
                string input = dir.Write("Field_20260716142318.bin", LogBuilder.V51().Build());
                List<string> notes = Program.RunToV4(input, false, Opt("to-v4", input));
                string output = Path.Combine(dir.Path, "v4.0", "Field_20260716142318.bin");
                Assert.True(File.Exists(output));
                Assert.Equal("4.0", TrajectoryLog.ReadVersion(output));
                Assert.Equal("OK   Field_20260716142318.bin -> " + output, notes[0]);
                Assert.All(notes.Skip(1), n => Assert.StartsWith("     ", n));
            }
        }

        [Fact]
        public void ToV4_OutputFile()
        {
            using (var dir = new TempDir())
            {
                string input = dir.Write("in.bin", LogBuilder.V51().Build());
                string output = dir.File("renamed.bin");
                Program.RunToV4(input, false, Opt("to-v4", input, "-o", output));
                Assert.True(File.Exists(output));
            }
        }

        [Fact]
        public void ToV4_OutputExistingFolder()
        {
            using (var dir = new TempDir())
            {
                string input = dir.Write("in.bin", LogBuilder.V51().Build());
                string outDir = dir.Sub("out");
                Program.RunToV4(input, false, Opt("to-v4", input, "-o", outDir));
                Assert.True(File.Exists(Path.Combine(outDir, "in.bin")));
            }
        }

        [Fact]
        public void ToV4_OutputWithTrailingSeparatorIsAFolder()
        {
            using (var dir = new TempDir())
            {
                string input = dir.Write("in.bin", LogBuilder.V51().Build());
                string outDir = dir.File("new") + Path.DirectorySeparatorChar;
                Program.RunToV4(input, false, Opt("to-v4", input, "-o", outDir));
                Assert.True(File.Exists(Path.Combine(dir.File("new"), "in.bin")));
            }
        }

        [Fact]
        public void ToV4_FolderInputTreatsOutputAsFolder()
        {
            using (var dir = new TempDir())
            {
                string input = dir.Write("in.bin", LogBuilder.V51().Build());
                string outDir = dir.File("notyet");
                Program.RunToV4(input, true, Opt("to-v4", dir.Path, "-o", outDir));
                Assert.True(File.Exists(Path.Combine(outDir, "in.bin")));
            }
        }

        [Fact]
        public void ToV4_RefusesToOverwriteInput()
        {
            using (var dir = new TempDir())
            {
                string input = dir.Write("in.bin", LogBuilder.V51().Build());
                var ex = Assert.Throws<IOException>(() => Program.RunToV4(input, false, Opt("to-v4", input, "-o", input, "--overwrite")));
                Assert.Contains("overwrite the input", ex.Message);
            }
        }

        [Fact]
        public void ToV4_ExistingOutputNeedsOverwrite()
        {
            using (var dir = new TempDir())
            {
                string input = dir.Write("in.bin", LogBuilder.V51().Build());
                string output = dir.Write("out.bin", new byte[] { 1 });
                var ex = Assert.Throws<IOException>(() => Program.RunToV4(input, false, Opt("to-v4", input, "-o", output)));
                Assert.Contains("--overwrite", ex.Message);
                Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(output));

                Program.RunToV4(input, false, Opt("to-v4", input, "-o", output, "--overwrite"));
                Assert.Equal("4.0", TrajectoryLog.ReadVersion(output));
            }
        }

        [Fact]
        public void ToV4_SkipsV40()
        {
            using (var dir = new TempDir())
            {
                string input = dir.Write("in.bin", LogBuilder.V40().Build());
                var ex = Assert.Throws<SkipException>(() => Program.RunToV4(input, false, Opt("to-v4", input)));
                Assert.Equal("already version 4.0", ex.Message);
                Assert.False(Directory.Exists(dir.File("v4.0")));
            }
        }

        [Fact]
        public void ToV4_TimeCsv()
        {
            using (var dir = new TempDir())
            {
                var b = LogBuilder.V51(3);
                b.Value = (s, id, k, act) => id == 43 ? (act ? 0.25f : 45296.5f + s * 0.02f) : LogBuilder.DefaultValue(s, id, k, act);
                string input = dir.Write("in.bin", b.Build());
                List<string> notes = Program.RunToV4(input, false, Opt("to-v4", input, "--time-csv"));
                string csv = Path.Combine(dir.Path, "v4.0", "in_time.csv");
                string[] lines = File.ReadAllLines(csv);
                Assert.Equal("Snapshot,SecondsSinceMidnight,Clock,Expected,Actual", lines[0]);
                Assert.Equal("1,45296.500,12:34:56.500,45296.5,0.25", lines[1]);
                Assert.Equal(4, lines.Length);
                Assert.Equal(45296.5f + 2 * 0.02f, float.Parse(lines[3].Split(',')[3], System.Globalization.CultureInfo.InvariantCulture));
                Assert.Contains(notes, n => n.Contains("Time axis saved to " + csv));
            }
        }

        [Fact]
        public void ToV4_TimeCsvSkippedWithoutTimeAxis()
        {
            using (var dir = new TempDir())
            {
                string input = dir.Write("in.bin", new LogBuilder { NumSnapshots = 2 }.SetAxes(0, 1).Build());
                Program.RunToV4(input, false, Opt("to-v4", input, "--time-csv"));
                Assert.False(File.Exists(Path.Combine(dir.Path, "v4.0", "in_time.csv")));
            }
        }

        [Fact]
        public void ToV5_DefaultOutputAndSkip()
        {
            using (var dir = new TempDir())
            {
                string input = dir.Write("in.bin", LogBuilder.V40().Build());
                List<string> notes = Program.RunToV5(input, false, Opt("to-v5", input));
                Assert.Equal("5.1", TrajectoryLog.ReadVersion(Path.Combine(dir.Path, "v5.1", "in.bin")));
                Assert.StartsWith("OK   in.bin -> ", notes[0]);

                string v5 = dir.Write("v5.bin", LogBuilder.V51().Build());
                Assert.Throws<SkipException>(() => Program.RunToV5(v5, false, Opt("to-v5", v5)));
            }
        }

        [Fact]
        public void ToCsv_DefaultOutputNextToInput()
        {
            using (var dir = new TempDir())
            {
                string input = dir.Write("Field.bin", LogBuilder.V51(4).Build());
                List<string> notes = Program.RunToCsv(input, Opt("to-csv", input));
                string csv = dir.File("Field.csv");
                Assert.Equal(5, File.ReadAllLines(csv).Length);
                Assert.Equal("OK   Field.bin (v5.1) -> " + csv, notes[0]);
                Assert.Equal("     4 snapshots, 17 axes, 2 subbeam(s)", notes[1]);
                Assert.Contains("     Axes with no data left out: Y1, Y2, X1, X2", notes);
            }
        }

        [Fact]
        public void ToCsv_OutputFolderCreated()
        {
            using (var dir = new TempDir())
            {
                string input = dir.Write("Field.bin", LogBuilder.V40().Build());
                string outDir = dir.File(Path.Combine("csv", "deep"));
                Program.RunToCsv(input, Opt("to-csv", input, "-o", outDir));
                Assert.True(File.Exists(Path.Combine(outDir, "Field.csv")));
            }
        }

        [Fact]
        public void ToCsv_ExistingOutputNeedsOverwrite()
        {
            using (var dir = new TempDir())
            {
                string input = dir.Write("Field.bin", LogBuilder.V51().Build());
                dir.Write("Field.csv", new byte[0]);
                Assert.Throws<IOException>(() => Program.RunToCsv(input, Opt("to-csv", input)));
                Program.RunToCsv(input, Opt("to-csv", input, "--overwrite"));
                Assert.StartsWith("Snapshot,", File.ReadAllText(dir.File("Field.csv"), Encoding.UTF8).TrimStart('﻿'));
            }
        }

        [Fact]
        public void ToCsv_CrcWarningNoted()
        {
            using (var dir = new TempDir())
            {
                var b = LogBuilder.V51();
                b.CorruptCrc = true;
                string input = dir.Write("bad.bin", b.Build());
                Assert.Throws<InvalidDataException>(() => Program.RunToCsv(input, Opt("to-csv", input)));
                List<string> notes = Program.RunToCsv(input, Opt("to-csv", input, "--ignore-crc"));
                Assert.Contains(notes, n => n.StartsWith("     WARNING: Input CRC"));
            }
        }

        [Fact]
        public void Version_ReportsAndChecks()
        {
            using (var dir = new TempDir())
            {
                string input = dir.Write("a.bin", LogBuilder.V40().Build());
                Assert.Equal(new List<string> { "v4.0  a.bin" }, Program.RunVersion(input, Opt("version", input)));
                Assert.Equal(new List<string> { "v4.0  a.bin" }, Program.RunVersion(input, Opt("version", input, "--expect", "4")));
                var ex = Assert.Throws<InvalidDataException>(() => Program.RunVersion(input, Opt("version", input, "--expect", "5.1")));
                Assert.Equal("version 4.0, expected 5.1", ex.Message);
            }
        }

        [Fact]
        public void Compare_Notes()
        {
            using (var dir = new TempDir())
            {
                string a = dir.Write("a.bin", LogBuilder.V51().Build());
                string same = dir.Write("same.bin", LogBuilder.V51().Build());
                var vb = LogBuilder.V51();
                vb.Crc = Crc16.Variants[1];
                string variant = dir.Write("variant.bin", vb.Build());
                var db = LogBuilder.V51();
                db.AxisScale = 1;
                string diff = dir.Write("diff.bin", db.Build());

                bool isSame;
                List<string> notes = Program.RunCompare(a, same, Opt("compare", a, same), out isSame);
                Assert.True(isSame);
                Assert.Equal("SAME a.bin (v5.1) vs same.bin (v5.1): byte-for-byte identical", notes[0]);

                notes = Program.RunCompare(a, variant, Opt("compare", a, variant), out isSame);
                Assert.True(isSame);
                Assert.Equal("SAME a.bin (v5.1) vs variant.bin (v5.1): same content", notes[0]);
                Assert.Contains(notes, n => n.StartsWith("     (CRC variant"));

                notes = Program.RunCompare(a, diff, Opt("compare", a, diff), out isSame);
                Assert.False(isSame);
                Assert.Equal("DIFF a.bin (v5.1) vs diff.bin (v5.1): 1 difference(s)", notes[0]);
                Assert.Equal("     Axis scale: 3 vs 1", notes[1]);
            }
        }

        [Fact]
        public void Compare_CrcWarnings()
        {
            using (var dir = new TempDir())
            {
                var b = LogBuilder.V51();
                b.CorruptCrc = true;
                string a = dir.Write("a.bin", b.Build());
                string c = dir.Write("c.bin", LogBuilder.V51().Build());
                bool isSame;
                List<string> notes = Program.RunCompare(a, c, Opt("compare", a, c, "--ignore-crc"), out isSame);
                Assert.Contains(notes, n => n.StartsWith("     WARNING A:"));
                Assert.DoesNotContain(notes, n => n.StartsWith("     WARNING B:"));
            }
        }
    }

    /// <summary>Program.Main: console output and exit codes.</summary>
    public class ProgramMainTests
    {
        static int RunMain(out ConsoleCapture output, params string[] args)
        {
            output = new ConsoleCapture();
            try { return Program.Main(args); }
            finally { output.Dispose(); }
        }

        [Fact]
        public void NoArguments_PrintsUsage()
        {
            ConsoleCapture c;
            Assert.Equal(2, RunMain(out c));
            Assert.Contains("Halcyon / Ethos trajectory log tool", c.Out);
        }

        [Fact]
        public void BadArguments_Exit2()
        {
            ConsoleCapture c;
            Assert.Equal(2, RunMain(out c, "to-v4", "in.bin", "--bogus"));
            Assert.Contains("Error: Unknown option --bogus", c.Err);
            Assert.Contains("Halcyon / Ethos trajectory log tool", c.Out);
        }

        [Fact]
        public void MissingInput_Exit1()
        {
            using (var dir = new TempDir())
            {
                ConsoleCapture c;
                Assert.Equal(1, RunMain(out c, "to-v4", dir.File("missing.bin")));
                Assert.Contains("Input not found", c.Err);
            }
        }

        [Fact]
        public void EmptyFolder_Exit1()
        {
            using (var dir = new TempDir())
            {
                ConsoleCapture c;
                Assert.Equal(1, RunMain(out c, "to-csv", dir.Path));
                Assert.Contains("No .bin files found", c.Err);
            }
        }

        [Fact]
        public void Folder_ConvertsEveryLogAndSkipsV40()
        {
            using (var dir = new TempDir())
            {
                string input = dir.Sub("in");
                File.WriteAllBytes(Path.Combine(input, "b.bin"), LogBuilder.V51().Build());
                File.WriteAllBytes(Path.Combine(input, "a.bin"), LogBuilder.V51().Build());
                File.WriteAllBytes(Path.Combine(input, "old.bin"), LogBuilder.V40().Build());
                File.WriteAllText(Path.Combine(input, "notes.txt"), "not a log");
                string output = dir.File("out");

                ConsoleCapture c;
                Assert.Equal(0, RunMain(out c, "to-v4", input, "-o", output));
                Assert.True(File.Exists(Path.Combine(output, "a.bin")));
                Assert.True(File.Exists(Path.Combine(output, "b.bin")));
                Assert.False(File.Exists(Path.Combine(output, "old.bin")));
                Assert.Contains("SKIP old.bin: already version 4.0", c.Out);
                Assert.True(c.Out.IndexOf("OK   a.bin") < c.Out.IndexOf("OK   b.bin"), "files are processed in name order");
            }
        }

        [Fact]
        public void Folder_OneBadFileGivesExit1ButOthersAreConverted()
        {
            using (var dir = new TempDir())
            {
                File.WriteAllBytes(dir.File("bad.bin"), new byte[100]);
                File.WriteAllBytes(dir.File("good.bin"), LogBuilder.V51().Build());
                ConsoleCapture c;
                Assert.Equal(1, RunMain(out c, "to-csv", dir.Path));
                Assert.Contains("FAIL bad.bin: File is too short", c.Err);
                Assert.True(File.Exists(dir.File("good.csv")));
            }
        }

        [Fact]
        public void ToV5_File()
        {
            using (var dir = new TempDir())
            {
                string input = dir.Write("in.bin", LogBuilder.V40().Build());
                ConsoleCapture c;
                Assert.Equal(0, RunMain(out c, "to-v5", input, "--start-time", "08:00:00", "--serial", "SN1"));
                var v5 = LogBuilder.Parse(File.ReadAllBytes(Path.Combine(dir.Path, "v5.1", "in.bin")));
                Assert.Equal(28800f, v5.Expected(0, 0, 0));
                Assert.Equal("SN1", v5.MachineSerial);
            }
        }

        [Fact]
        public void Version_ExpectMismatch_Exit1()
        {
            using (var dir = new TempDir())
            {
                dir.Write("a.bin", LogBuilder.V40().Build());
                dir.Write("b.bin", LogBuilder.V51().Build());
                ConsoleCapture c;
                Assert.Equal(1, RunMain(out c, "version", dir.Path, "--expect", "5.1"));
                Assert.Contains("FAIL a.bin: version 4.0, expected 5.1", c.Err);
                Assert.Contains("v5.1  b.bin", c.Out);

                Assert.Equal(0, RunMain(out c, "version", dir.Path));
            }
        }

        [Fact]
        public void Compare_Files()
        {
            using (var dir = new TempDir())
            {
                string a = dir.Write("a.bin", LogBuilder.V51().Build());
                string b = dir.Write("b.bin", LogBuilder.V51().Build());
                var db = LogBuilder.V51();
                db.Truncated = 1;
                string d = dir.Write("d.bin", db.Build());
                ConsoleCapture c;
                Assert.Equal(0, RunMain(out c, "compare", a, b));
                Assert.StartsWith("SAME", c.Out);
                Assert.Equal(1, RunMain(out c, "compare", a, d));
                Assert.StartsWith("DIFF", c.Out);
            }
        }

        [Fact]
        public void Compare_MissingInput_Exit1()
        {
            using (var dir = new TempDir())
            {
                string a = dir.Write("a.bin", LogBuilder.V51().Build());
                ConsoleCapture c;
                Assert.Equal(1, RunMain(out c, "compare", a, dir.File("nope.bin")));
                Assert.Contains("Input not found", c.Err);
            }
        }

        [Fact]
        public void Compare_FileAgainstFolder_Exit2()
        {
            using (var dir = new TempDir())
            {
                string a = dir.Write("a.bin", LogBuilder.V51().Build());
                ConsoleCapture c;
                Assert.Equal(2, RunMain(out c, "compare", a, dir.Path));
                Assert.Contains("two files or two folders", c.Err);
            }
        }

        [Fact]
        public void Compare_UnreadableFile_Exit1()
        {
            using (var dir = new TempDir())
            {
                string a = dir.Write("a.bin", LogBuilder.V51().Build());
                string b = dir.Write("b.bin", new byte[10]);
                ConsoleCapture c;
                Assert.Equal(1, RunMain(out c, "compare", a, b));
                Assert.Contains("FAIL a.bin", c.Err);
            }
        }

        [Fact]
        public void Compare_Folders()
        {
            using (var dir = new TempDir())
            {
                string fa = dir.Sub("A"), fb = dir.Sub("B");
                File.WriteAllBytes(Path.Combine(fa, "same.bin"), LogBuilder.V51().Build());
                File.WriteAllBytes(Path.Combine(fb, "same.bin"), LogBuilder.V51().Build());
                ConsoleCapture c;
                Assert.Equal(0, RunMain(out c, "compare", fa, fb));
                Assert.Contains("1 same, 0 different, 0 missing / failed", c.Out);

                var db = LogBuilder.V51();
                db.MlcModel = 9;
                File.WriteAllBytes(Path.Combine(fa, "diff.bin"), LogBuilder.V51().Build());
                File.WriteAllBytes(Path.Combine(fb, "diff.bin"), db.Build());
                File.WriteAllBytes(Path.Combine(fa, "onlyA.bin"), LogBuilder.V51().Build());
                Assert.Equal(1, RunMain(out c, "compare", fa, fb));
                Assert.Contains("MISSING onlyA.bin: only in " + fa, c.Out);
                Assert.Contains("1 same, 1 different, 1 missing / failed", c.Out);
            }
        }

        [Fact]
        public void Compare_EmptyFolders_Exit1()
        {
            using (var dir = new TempDir())
            {
                ConsoleCapture c;
                Assert.Equal(1, RunMain(out c, "compare", dir.Sub("A"), dir.Sub("B")));
                Assert.Contains("No .bin files found", c.Err);
            }
        }

        [Fact]
        public void Compare_V51AgainstConvertedV40()
        {
            using (var dir = new TempDir())
            {
                string input = dir.Write("log.bin", LogBuilder.V51(10).Build());
                ConsoleCapture c;
                Assert.Equal(0, RunMain(out c, "to-v4", input));
                string v4 = Path.Combine(dir.Path, "v4.0", "log.bin");
                Assert.Equal(1, RunMain(out c, "compare", input, v4));
                Assert.Equal(0, RunMain(out c, "compare", input, v4, "--ignore-version"));
                Assert.Contains("SAME log.bin (v5.1) vs log.bin (v4.0): same content", c.Out);
            }
        }
    }
}

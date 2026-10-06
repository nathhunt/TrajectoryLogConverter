using System;
using Xunit;

namespace HalcyonTrajectoryLogTool.Tests
{
    public class OptionsTests
    {
        static Options Parse(params string[] args) { return Options.Parse(args); }

        static void Rejects(string messagePart, params string[] args)
        {
            var ex = Assert.Throws<ArgumentException>(() => Options.Parse(args));
            Assert.Contains(messagePart, ex.Message);
        }

        [Theory]
        [InlineData("to-v4", Command.ToV4)]
        [InlineData("TO-V4", Command.ToV4)]
        [InlineData("to-csv", Command.ToCsv)]
        [InlineData("to-v5", Command.ToV5)]
        [InlineData("version", Command.Version)]
        public void Commands(string cmd, Command expected)
        {
            Options o = Parse(cmd, "in.bin");
            Assert.Equal(expected, o.Command);
            Assert.Equal("in.bin", o.Input);
        }

        [Fact]
        public void Compare_TakesTwoInputs()
        {
            Options o = Parse("compare", "a.bin", "b.bin");
            Assert.Equal(Command.Compare, o.Command);
            Assert.Equal("a.bin", o.Input);
            Assert.Equal("b.bin", o.Input2);
        }

        [Fact]
        public void Defaults()
        {
            Options o = Parse("to-v4", "in.bin");
            Assert.Null(o.Output);
            Assert.False(o.Overwrite);
            Assert.False(o.IgnoreCrc);
            Assert.False(o.KeepMachineInfo);
            Assert.Null(o.AxisScale);
            Assert.False(o.TimeCsv);
            Assert.False(o.DropCouchRotations);
            Assert.Null(o.TimeFrom);
            Assert.Null(o.StartTime);
            Assert.Null(o.Serial);
            Assert.False(o.AddCouchRotations);
            Assert.False(o.NoMlc);
            Assert.False(o.ActualOnly);
            Assert.Null(o.ExpectVersion);
            Assert.False(o.IgnoreVersion);
            Assert.Equal(0, o.Tolerance);
            Assert.Equal(10, o.MaxDiffs);
        }

        [Theory]
        [InlineData()]
        [InlineData("-h")]
        [InlineData("--help")]
        [InlineData("/?")]
        [InlineData("to-v4", "in.bin", "--help")]
        public void Help_ReturnsNull(params string[] args)
        {
            Assert.Null(Options.Parse(args));
        }

        [Fact]
        public void UnknownCommand() { Rejects("First argument must be a command", "convert", "in.bin"); }

        [Fact]
        public void UnknownOption() { Rejects("Unknown option --bogus", "to-v4", "in.bin", "--bogus"); }

        [Fact]
        public void NoInput() { Rejects("No input given", "to-v4"); }

        [Fact]
        public void TwoInputs() { Rejects("Only one input path", "to-v4", "a.bin", "b.bin"); }

        [Fact]
        public void CompareOneInput() { Rejects("compare needs two inputs", "compare", "a.bin"); }

        [Fact]
        public void CompareThreeInputs() { Rejects("exactly two inputs", "compare", "a", "b", "c"); }

        [Fact]
        public void EmptyArgumentsAreSkipped()
        {
            Options o = Parse("to-v4", "", "in.bin", "");
            Assert.Equal("in.bin", o.Input);
        }

        [Theory]
        [InlineData("-o")]
        [InlineData("--output")]
        public void Output(string flag)
        {
            Assert.Equal("out", Parse("to-v4", "in.bin", flag, "out").Output);
            Assert.Equal("out", Parse("to-v5", "in.bin", flag, "out").Output);
            Assert.Equal("out", Parse("to-csv", "in.bin", flag, "out").Output);
        }

        [Fact]
        public void OptionNeedsValue() { Rejects("-o needs a value", "to-v4", "in.bin", "-o"); }

        [Fact]
        public void CommonFlags()
        {
            Options o = Parse("to-csv", "--overwrite", "in.bin", "--ignore-crc");
            Assert.True(o.Overwrite);
            Assert.True(o.IgnoreCrc);
        }

        [Fact]
        public void ToV4Flags()
        {
            Options o = Parse("to-v4", "in.bin", "--keep-machine-info", "--time-csv", "--drop-couch-rotations", "--axis-scale", "2");
            Assert.True(o.KeepMachineInfo);
            Assert.True(o.TimeCsv);
            Assert.True(o.DropCouchRotations);
            Assert.Equal(2, o.AxisScale);
        }

        [Fact]
        public void ToV5Flags()
        {
            Options o = Parse("to-v5", "in.bin", "--time-from", "t.csv", "--serial", "ABC123", "--add-couch-rotations", "--axis-scale", "1");
            Assert.Equal("t.csv", o.TimeFrom);
            Assert.Equal("ABC123", o.Serial);
            Assert.True(o.AddCouchRotations);
            Assert.Equal(1, o.AxisScale);
        }

        [Fact]
        public void ToCsvFlags()
        {
            Options o = Parse("to-csv", "in.bin", "--no-mlc", "--actual-only");
            Assert.True(o.NoMlc);
            Assert.True(o.ActualOnly);
        }

        [Fact]
        public void CompareFlags()
        {
            Options o = Parse("compare", "a", "b", "--ignore-version", "--tolerance", "1e-3", "--max-diffs", "0");
            Assert.True(o.IgnoreVersion);
            Assert.Equal(0.001, o.Tolerance, 12);
            Assert.Equal(0, o.MaxDiffs);
        }

        [Theory]
        [InlineData("to-csv", "--keep-machine-info", "to-v4")]
        [InlineData("to-v5", "--time-csv", "to-v4")]
        [InlineData("to-v5", "--drop-couch-rotations", "to-v4")]
        [InlineData("to-v4", "--add-couch-rotations", "to-v5")]
        [InlineData("to-v4", "--no-mlc", "to-csv")]
        [InlineData("to-v4", "--actual-only", "to-csv")]
        [InlineData("to-v4", "--ignore-version", "compare")]
        public void FlagForWrongCommand(string cmd, string flag, string allowed)
        {
            Rejects(flag + " only applies to " + allowed, cmd, "in.bin", flag);
        }

        [Theory]
        [InlineData("to-csv", "--axis-scale", "3", "to-v4 / to-v5")]
        [InlineData("to-v4", "--time-from", "t.csv", "to-v5")]
        [InlineData("to-v4", "--start-time", "10:00:00", "to-v5")]
        [InlineData("to-v4", "--serial", "X", "to-v5")]
        [InlineData("to-v4", "--expect", "4.0", "version")]
        [InlineData("to-v4", "--tolerance", "1", "compare")]
        [InlineData("to-v4", "--max-diffs", "1", "compare")]
        [InlineData("version", "-o", "out", "to-v4 / to-v5 / to-csv")]
        public void ValueOptionForWrongCommand(string cmd, string flag, string value, string allowed)
        {
            Rejects(flag + " only applies to " + allowed, cmd, "in.bin", flag, value);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("4")]
        [InlineData("x")]
        public void AxisScale_Invalid(string v) { Rejects("--axis-scale must be 1, 2 or 3", "to-v4", "in.bin", "--axis-scale", v); }

        [Theory]
        [InlineData("1234567")]
        [InlineData("ab\tc")]
        [InlineData("é")]
        public void Serial_Invalid(string v) { Rejects("--serial must be up to 6 ASCII", "to-v5", "in.bin", "--serial", v); }

        [Theory]
        [InlineData("123456")]
        [InlineData("A")]
        [InlineData(" ~")]
        public void Serial_Valid(string v) { Assert.Equal(v, Parse("to-v5", "in.bin", "--serial", v).Serial); }

        [Fact]
        public void Serial_EmptyIsAllowed()
        {
            // An empty argument is skipped where an option or input is expected, but not as an option's value.
            Assert.Equal("", Parse("to-v5", "in.bin", "--serial", "").Serial);
        }

        [Theory]
        [InlineData("0", 0)]
        [InlineData("45296.5", 45296.5)]
        [InlineData("86399.999", 86399.999)]
        [InlineData("12:34:56", 45296)]
        [InlineData("12:34:56.5", 45296.5)]
        [InlineData("00:00:00", 0)]
        [InlineData("23:59:59.999", 86399.999)]
        public void StartTime_Valid(string v, double expected)
        {
            Assert.Equal(expected, Parse("to-v5", "in.bin", "--start-time", v).StartTime.Value, 6);
        }

        [Theory]
        [InlineData("-1")]
        [InlineData("86400")]
        [InlineData("1.00:00:00")]
        [InlineData("24:00:00")]
        [InlineData("noon")]
        public void StartTime_Invalid(string v)
        {
            Rejects("--start-time must be", "to-v5", "in.bin", "--start-time", v);
        }

        [Theory]
        [InlineData("4", "4.0")]
        [InlineData("4.0", "4.0")]
        [InlineData("v4.0", "4.0")]
        [InlineData(" V5 ", "5.1")]
        [InlineData("5.1", "5.1")]
        public void Expect_Normalised(string v, string expected)
        {
            Assert.Equal(expected, Parse("version", "in.bin", "--expect", v).ExpectVersion);
        }

        [Theory]
        [InlineData("5.0")]
        [InlineData("6")]
        public void Expect_Invalid(string v) { Rejects("--expect must be 4.0 or 5.1", "version", "in.bin", "--expect", v); }

        [Theory]
        [InlineData("-1")]
        [InlineData("NaN")]
        [InlineData("abc")]
        public void Tolerance_Invalid(string v) { Rejects("--tolerance must be", "compare", "a", "b", "--tolerance", v); }

        [Theory]
        [InlineData("-1")]
        [InlineData("1.5")]
        public void MaxDiffs_Invalid(string v) { Rejects("--max-diffs must be", "compare", "a", "b", "--max-diffs", v); }

        [Theory]
        [InlineData(Command.ToV4, "to-v4")]
        [InlineData(Command.ToCsv, "to-csv")]
        [InlineData(Command.ToV5, "to-v5")]
        [InlineData(Command.Version, "version")]
        [InlineData(Command.Compare, "compare")]
        public void CommandName(Command c, string expected)
        {
            Assert.Equal(expected, Options.CommandName(c));
        }

        [Fact]
        public void PrintUsage_ListsEveryCommand()
        {
            using (var c = new ConsoleCapture())
            {
                Options.PrintUsage();
                foreach (string cmd in new[] { "to-v4", "to-csv", "to-v5", "version", "compare" })
                    Assert.Contains("HalcyonTrajectoryLogTool " + cmd, c.Out);
            }
        }
    }
}

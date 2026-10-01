// HalcyonTrajectoryLogTool.cs
//
// Command-line tool for Halcyon / Ethos trajectory logs (.bin):
//
//   to-v4   Convert a version 5.1 log (HAL 5.0) to the version 4.0 layout (HAL 2.0 - 4.0 MR1)
//           so it can be read by tools that only understand 4.0 (e.g. DoseLab).
//   to-csv  Export a version 4.0 or 5.1 log to human-readable CSV:
//             <name>.csv          one row per 20 ms snapshot, expected + actual for every axis
//
// Validation / testing commands (command line only, not in the GUI):
//
//   to-v5   Convert a version 4.0 log to the version 5.1 layout (adds the time axis).
//   version Quick check of whether a log is version 4.0 or 5.1 (reads the first 32 bytes only).
//   compare Compare two logs (or two folders of logs) value by value: header, subbeams, snapshots.
//
// Based on "Halcyon and Ethos Radiotherapy System Trajectory Log File Specification"
//   P1069495-001-A (May 2025)  -> version 4.0 layout
//   P1069495-002-B (May 2026)  -> version 5.1 layout
//
// ---------------------------------------------------------------------------------------------
// Usage
//   HalcyonTrajectoryLogTool to-v4    <input.bin | folder> [-o <file | folder>] [options]
//   HalcyonTrajectoryLogTool to-csv   <input.bin | folder> [-o <folder>]        [options]
//   HalcyonTrajectoryLogTool to-v5    <input.bin | folder> [-o <file | folder>] [options]
//   HalcyonTrajectoryLogTool version  <input.bin | folder> [--expect <4.0|5.1>]
//   HalcyonTrajectoryLogTool compare  <a.bin | folderA> <b.bin | folderB>      [options]
//
// Common options
//   -o, --output <path>     to-v4 : output file, or folder. Default: "v4.0" subfolder next to
//                                   the input, ORIGINAL file name kept (DoseLab reads the
//                                   treatment date from the "_yyyyMMddHHmmss.bin" file name).
//                           to-v5 : as to-v4, default "v5.1" subfolder.
//                           to-csv: output folder. Default: same folder as the input.
//   --overwrite             Overwrite existing output files.
//   --ignore-crc            Process even if the input CRC does not verify.
//
// to-v4 options
//   --keep-machine-info     Keep machine specifier + serial number bytes in the header.
//   --axis-scale <1|2|3>    Header axis-scale value to write (axis values are NOT converted).
//   --time-csv              Save the removed v5.1 time axis to "<output>_time.csv".
//
// to-v5 options
//   --time-from <csv>       Restore the time axis from a "_time.csv" written by to-v4 --time-csv.
//   --start-time <t>        Clock time of the first snapshot (hh:mm:ss[.fff] or seconds since
//                           midnight). Default: time of the "_yyyyMMddHHmmss" stamp in the file
//                           name, else 00:00:00.
//   --serial <text>         Machine serial number to write (last 6 characters of the serial).
//   --axis-scale <1|2|3>    Header axis-scale value to write (axis values are NOT converted).
//
// to-csv options
//   --no-mlc                Leave the 116 MLC column pairs out of the data CSV.
//   --actual-only           Only write actual values (no expected columns).
//
// version options
//   --expect <4.0|5.1>      Report a failure (exit code 1) for any log of a different version.
//
// compare options
//   --ignore-version        Ignore what differs between 4.0 and 5.1 by design: the version string,
//                           the time axis (43), the axis order and the machine specifier / serial
//                           number.
//   --tolerance <x>         Treat values within x of each other as equal. Default 0 (exact).
//   --max-diffs <n>         Number of individual value differences to list. Default 10.
//
// Exit code: 0 = all files OK (compare: all the same), 1 = at least one failure or difference,
//            2 = bad arguments.
// ---------------------------------------------------------------------------------------------
// What to-v4 changes (per P1069495-002-B, "Introduction"):
//   1. Version string "5.1" -> "4.0".
//   2. Time axis (enum 43) removed from the axis enumeration, samples-per-axis arrays, axis
//      count, and from every snapshot.
//   3. Machine specifier (1 byte) + serial number (6 bytes) are zeroed unless
//      --keep-machine-info. The 5.1 spec lists them as new in 5.1, although the header table
//      of the 4.0 spec (P1069495-001-A) also has them.
//   4. Axis scale copied unchanged (5.1 writes 3, which is also a legal 4.0 value).
//   Header rebuilt, subbeams copied verbatim, CRC recomputed.
//
// What to-v5 changes (the reverse of to-v4; for validation and testing):
//   1. Version string "4.0" -> "5.1".
//   2. Time axis (enum 43, 1 sample) added, and the axes put in the order HAL 5.0 writes them:
//      Time, ControlPoint, MU, BeamHold, Gantry, Coll, Y1, Y2, X1, X2, couch (6 - 11), MLC.
//      The spec does not give the order; it is taken from machine logs. v4.0 logs do not record
//      clock time, so it is either restored from a to-v4 "_time.csv" (exact round trip) or
//      generated as start time + snapshot index x sampling interval. Per the spec the time is
//      seconds since midnight in the "expected" record and the "actual" record is empty, so
//      generated times write 0 as the actual value. Generated times leave out beam pauses.
//   3. Machine specifier + serial number bytes copied as they are. If the specifier is 0
//      (TrueBeam) and the serial number is empty - i.e. zeroed by to-v4 - the specifier is set to
//      1 (Halcyon / Ethos). --serial sets the serial number.
//   4. Axis scale copied unchanged unless --axis-scale (HAL 5.0 writes 3; the axis values are
//      NOT converted, so a v4.0 log with axis scale 1 or 2 keeps that value and gets a warning).
//   Header rebuilt, subbeams copied verbatim, CRC recomputed.
//
// CSV notes:
//   * Values are in the axis scale given in the header: cm for linear axes, degrees for
//     rotations, MU for dose. Axes that hold the "no value" marker (float max) in every
//     snapshot - the jaws, which Halcyon does not have - are left out (and listed in the
//     console output); any single "no value" sample elsewhere is written as an empty cell.
//   * Control point and time have no separate expected/actual value, so they get one column.
//   * Time: v5.1 logs record clock time (seconds since midnight); v4.0 logs do not, so the
//     elapsed time is computed as snapshot index x sampling interval (beam pauses are not
//     recorded, so this can under-count real time).
//   * MLC: 2 bank values followed by 114 leaves. The spec does not define the leaf order;
//     in logs examined, leaves 1-57 are one bank and 58-114 the other (leaf n faces leaf n+57),
//     with the first 29 and last 28 of each bank behaving as separate layers. Columns are
//     therefore numbered by position in the file (Leaf001..Leaf114) rather than named.
//
// Build / run:
//   VS / VS Code:    TrajectoryLogConverter.sln, project HalcyonTrajectoryLogTool
//   .NET 10+:        dotnet run HalcyonTrajectoryLogTool.cs -- to-csv <input>
//   .NET project:    drop this file into a console project
//   Mono / .NET Fx:  csc HalcyonTrajectoryLogTool.cs (or mcs), then run the .exe
//   C# 7.x compatible, so it also compiles inside an ESAPI / .NET Framework 4.x project.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HalcyonTrajectoryLogTool
{
    // =============================================================================================
    // Entry point / command-line handling
    // =============================================================================================
    public static class Program
    {
        public static int Main(string[] args)
        {
            Options opt;
            try { opt = Options.Parse(args); }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine("Error: " + ex.Message);
                Console.Error.WriteLine();
                Options.PrintUsage();
                return 2;
            }
            if (opt == null) { Options.PrintUsage(); return 2; }

            if (opt.Command == Command.Compare) return CompareMain(opt);

            // Work out the list of input files
            List<string> inputs;
            bool folderInput;
            if (!TryListInputs(opt.Input, out inputs, out folderInput)) return 1;

            int failures = 0;
            foreach (string input in inputs)
            {
                try
                {
                    List<string> notes;
                    switch (opt.Command)
                    {
                        case Command.ToV4: notes = RunToV4(input, folderInput, opt); break;
                        case Command.ToV5: notes = RunToV5(input, folderInput, opt); break;
                        case Command.Version: notes = RunVersion(input, opt); break;
                        default: notes = RunToCsv(input, opt); break;
                    }
                    foreach (string line in notes) Console.WriteLine(line);
                }
                catch (SkipException ex)
                {
                    Console.WriteLine("SKIP " + Path.GetFileName(input) + ": " + ex.Message);
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.Error.WriteLine("FAIL " + Path.GetFileName(input) + ": " + ex.Message);
                }
            }
            return failures == 0 ? 0 : 1;
        }

        static bool TryListInputs(string path, out List<string> inputs, out bool folderInput)
        {
            inputs = new List<string>();
            folderInput = Directory.Exists(path);
            if (folderInput)
                inputs.AddRange(Directory.GetFiles(path, "*.bin").OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            else if (File.Exists(path))
                inputs.Add(path);
            else
            {
                Console.Error.WriteLine("Input not found: " + path);
                return false;
            }
            if (inputs.Count == 0) { Console.Error.WriteLine("No .bin files found in " + path); return false; }
            return true;
        }

        /// <summary>Output file for to-v4 / to-v5; checks it neither is the input nor exists (unless --overwrite).</summary>
        static string OutputFile(string input, bool folderInput, Options opt, string defaultSubfolder)
        {
            string name = Path.GetFileName(input);
            string output;
            if (opt.Output == null)
                output = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(input)), defaultSubfolder, name);
            else if (folderInput || Directory.Exists(opt.Output) || opt.Output.EndsWith(Path.DirectorySeparatorChar.ToString()))
                output = Path.Combine(opt.Output, name);
            else
                output = opt.Output;

            if (string.Equals(Path.GetFullPath(output), Path.GetFullPath(input), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Output would overwrite the input; choose a different folder.");
            if (File.Exists(output) && !opt.Overwrite)
                throw new IOException("Output exists (use --overwrite): " + output);
            return output;
        }

        public static List<string> RunToV4(string input, bool folderInput, Options opt)
        {
            string name = Path.GetFileName(input);
            string output = OutputFile(input, folderInput, opt, "v4.0");

            var log = TrajectoryLog.Parse(File.ReadAllBytes(input), opt.IgnoreCrc);
            if (log.Version == "4.0") throw new SkipException("already version 4.0");

            var notes = new List<string>();
            byte[] converted = V4Converter.Convert(log, opt, notes);
            FileUtil.WriteAtomic(output, converted);

            if (opt.TimeCsv && log.TimeAxisIndex >= 0)
            {
                string csvPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output)),
                                              Path.GetFileNameWithoutExtension(output) + "_time.csv");
                // Expected / Actual hold the exact float values, so to-v5 --time-from can restore them.
                var sb = new StringBuilder("Snapshot,SecondsSinceMidnight,Clock,Expected,Actual\r\n");
                for (int s = 0; s < log.NumSnapshots; s++)
                {
                    float t = log.Expected(s, log.TimeAxisIndex, 0);
                    sb.Append(s + 1).Append(',').Append(Fmt.Fixed(t, 3)).Append(',').Append(Fmt.Clock(t))
                      .Append(',').Append(Fmt.Exact(t)).Append(',').Append(Fmt.Exact(log.Actual(s, log.TimeAxisIndex, 0)))
                      .Append("\r\n");
                }
                FileUtil.WriteAtomic(csvPath, Encoding.UTF8.GetBytes(sb.ToString()));
                notes.Add("Time axis saved to " + csvPath);
            }

            notes.Insert(0, "OK   " + name + " -> " + output);
            return notes.Select((n, i) => i == 0 ? n : "     " + n).ToList();
        }

        public static List<string> RunToV5(string input, bool folderInput, Options opt)
        {
            string name = Path.GetFileName(input);
            string output = OutputFile(input, folderInput, opt, "v5.1");

            var log = TrajectoryLog.Parse(File.ReadAllBytes(input), opt.IgnoreCrc);
            if (log.Version == "5.1") throw new SkipException("already version 5.1");

            var notes = new List<string>();
            byte[] converted = V5Converter.Convert(log, opt, input, notes);
            FileUtil.WriteAtomic(output, converted);

            notes.Insert(0, "OK   " + name + " -> " + output);
            return notes.Select((n, i) => i == 0 ? n : "     " + n).ToList();
        }

        public static List<string> RunToCsv(string input, Options opt)
        {
            string outDir = opt.Output ?? Path.GetDirectoryName(Path.GetFullPath(input));
            Directory.CreateDirectory(outDir);
            string baseName = Path.GetFileNameWithoutExtension(input);
            string dataPath = Path.Combine(outDir, baseName + ".csv");
            if (!opt.Overwrite && File.Exists(dataPath))
                throw new IOException("Output exists (use --overwrite): " + dataPath);

            var log = TrajectoryLog.Parse(File.ReadAllBytes(input), opt.IgnoreCrc);
            FileUtil.WriteAtomic(dataPath, Encoding.UTF8.GetBytes(CsvExporter.DataCsv(log, opt)));

            var notes = new List<string>
            {
                "OK   " + Path.GetFileName(input) + " (v" + log.Version + ") -> " + dataPath,
                "     " + string.Format(CultureInfo.InvariantCulture, "{0} snapshots, {1} axes, {2} subbeam(s)",
                                        log.NumSnapshots, log.NumAxes, log.Subbeams.Count)
            };
            bool[] hasData = CsvExporter.AxesWithData(log);
            string empty = string.Join(", ", Enumerable.Range(0, log.NumAxes).Where(i => !hasData[i])
                                                   .Select(i => TrajectoryLog.AxisName(log.AxisIds[i])));
            if (empty.Length > 0) notes.Add("     Axes with no data left out: " + empty);
            if (log.CrcWarning != null) notes.Add("     WARNING: " + log.CrcWarning);
            return notes;
        }

        /// <summary>Quick version check from the file's first 32 bytes; throws if it is not the --expect version.</summary>
        public static List<string> RunVersion(string input, Options opt)
        {
            string version = TrajectoryLog.ReadVersion(input);
            if (opt.ExpectVersion != null && version != opt.ExpectVersion)
                throw new InvalidDataException("version " + version + ", expected " + opt.ExpectVersion);
            return new List<string> { "v" + version + "  " + Path.GetFileName(input) };
        }

        /// <summary>Compares two logs. The first line starts with SAME or DIFF; <paramref name="same"/> gives the outcome.</summary>
        public static List<string> RunCompare(string fileA, string fileB, Options opt, out bool same)
        {
            var a = TrajectoryLog.Parse(File.ReadAllBytes(fileA), opt.IgnoreCrc);
            var b = TrajectoryLog.Parse(File.ReadAllBytes(fileB), opt.IgnoreCrc);
            CompareResult r = LogComparer.Compare(a, b, opt.Tolerance, opt.IgnoreVersion, opt.MaxDiffs);
            same = r.Same;

            string pair = Path.GetFileName(fileA) + " (v" + a.Version + ") vs " + Path.GetFileName(fileB) + " (v" + b.Version + ")";
            var notes = new List<string>();
            if (r.ByteIdentical) notes.Add("SAME " + pair + ": byte-for-byte identical");
            else if (r.Same) notes.Add("SAME " + pair + ": same content");
            else notes.Add("DIFF " + pair + ": " + r.DifferenceCount.ToString("N0", CultureInfo.InvariantCulture) + " difference(s)");
            foreach (string d in r.Differences) notes.Add("     " + d);
            foreach (string i in r.Info) notes.Add("     (" + i + ")");
            if (a.CrcWarning != null) notes.Add("     WARNING A: " + a.CrcWarning);
            if (b.CrcWarning != null) notes.Add("     WARNING B: " + b.CrcWarning);
            return notes;
        }

        static int CompareMain(Options opt)
        {
            bool dirA = Directory.Exists(opt.Input), dirB = Directory.Exists(opt.Input2);
            foreach (string p in new[] { opt.Input, opt.Input2 })
                if (!Directory.Exists(p) && !File.Exists(p)) { Console.Error.WriteLine("Input not found: " + p); return 1; }
            if (dirA != dirB) { Console.Error.WriteLine("compare needs two files or two folders."); return 2; }

            // Pairs of files to compare; folders are matched by file name (top level only).
            var pairs = new List<KeyValuePair<string, string>>();
            int failures = 0;
            if (!dirA)
                pairs.Add(new KeyValuePair<string, string>(opt.Input, opt.Input2));
            else
            {
                var namesA = Directory.GetFiles(opt.Input, "*.bin").Select(Path.GetFileName).ToList();
                var namesB = new HashSet<string>(Directory.GetFiles(opt.Input2, "*.bin").Select(Path.GetFileName),
                                                 StringComparer.OrdinalIgnoreCase);
                foreach (string n in namesA.Union(namesB, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    bool inA = namesA.Contains(n, StringComparer.OrdinalIgnoreCase), inB = namesB.Contains(n);
                    if (inA && inB) pairs.Add(new KeyValuePair<string, string>(Path.Combine(opt.Input, n), Path.Combine(opt.Input2, n)));
                    else
                    {
                        failures++;
                        Console.WriteLine("MISSING " + n + ": only in " + (inA ? opt.Input : opt.Input2));
                    }
                }
                if (pairs.Count == 0 && failures == 0) { Console.Error.WriteLine("No .bin files found."); return 1; }
            }

            int same = 0, different = 0;
            foreach (var pair in pairs)
            {
                try
                {
                    bool isSame;
                    foreach (string line in RunCompare(pair.Key, pair.Value, opt, out isSame)) Console.WriteLine(line);
                    if (isSame) same++; else different++;
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.Error.WriteLine("FAIL " + Path.GetFileName(pair.Key) + ": " + ex.Message);
                }
            }
            if (dirA)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0} same, {1} different, {2} missing / failed", same, different, failures));
            return different == 0 && failures == 0 ? 0 : 1;
        }
    }

    public enum Command { ToV4, ToCsv, ToV5, Version, Compare }

    public sealed class SkipException : Exception
    {
        public SkipException(string message) : base(message) { }
    }

    public sealed class Options
    {
        public Command Command;
        public string Input;
        public string Input2;                // compare: second log / folder
        public string Output;
        public bool Overwrite;
        public bool IgnoreCrc;
        // to-v4
        public bool KeepMachineInfo;
        public int? AxisScale;               // also to-v5
        public bool TimeCsv;
        // to-v5
        public string TimeFrom;
        public double? StartTime;            // seconds since midnight
        public string Serial;
        // to-csv
        public bool NoMlc;
        public bool ActualOnly;
        // version
        public string ExpectVersion;
        // compare
        public bool IgnoreVersion;
        public double Tolerance;
        public int MaxDiffs = 10;

        public static Options Parse(string[] args)
        {
            if (args.Length == 0) return null;
            var o = new Options();
            string cmd = args[0].ToLowerInvariant();
            if (cmd == "-h" || cmd == "--help" || cmd == "/?") return null;
            if (cmd == "to-v4") o.Command = Command.ToV4;
            else if (cmd == "to-csv") o.Command = Command.ToCsv;
            else if (cmd == "to-v5") o.Command = Command.ToV5;
            else if (cmd == "version") o.Command = Command.Version;
            else if (cmd == "compare") o.Command = Command.Compare;
            else throw new ArgumentException("First argument must be a command: to-v4, to-csv, to-v5, version or compare");

            var inputs = new List<string>();
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "-h": case "--help": case "/?": return null;
                    case "-o": case "--output": RequireCmd(o, a, Command.ToV4, Command.ToV5, Command.ToCsv); o.Output = Next(args, ref i, a); break;
                    case "--overwrite": o.Overwrite = true; break;
                    case "--ignore-crc": o.IgnoreCrc = true; break;
                    case "--keep-machine-info": RequireCmd(o, a, Command.ToV4); o.KeepMachineInfo = true; break;
                    case "--time-csv": RequireCmd(o, a, Command.ToV4); o.TimeCsv = true; break;
                    case "--axis-scale":
                        RequireCmd(o, a, Command.ToV4, Command.ToV5);
                        int s;
                        if (!int.TryParse(Next(args, ref i, a), out s) || s < 1 || s > 3)
                            throw new ArgumentException("--axis-scale must be 1, 2 or 3");
                        o.AxisScale = s; break;
                    case "--time-from": RequireCmd(o, a, Command.ToV5); o.TimeFrom = Next(args, ref i, a); break;
                    case "--start-time":
                        RequireCmd(o, a, Command.ToV5);
                        o.StartTime = ParseClock(Next(args, ref i, a));
                        break;
                    case "--serial":
                        RequireCmd(o, a, Command.ToV5);
                        o.Serial = Next(args, ref i, a);
                        if (o.Serial.Length > 6 || o.Serial.Any(c => c < 32 || c > 126))
                            throw new ArgumentException("--serial must be up to 6 ASCII characters");
                        break;
                    case "--no-mlc": RequireCmd(o, a, Command.ToCsv); o.NoMlc = true; break;
                    case "--actual-only": RequireCmd(o, a, Command.ToCsv); o.ActualOnly = true; break;
                    case "--expect":
                        RequireCmd(o, a, Command.Version);
                        o.ExpectVersion = NormaliseVersion(Next(args, ref i, a));
                        break;
                    case "--ignore-version": RequireCmd(o, a, Command.Compare); o.IgnoreVersion = true; break;
                    case "--tolerance":
                        RequireCmd(o, a, Command.Compare);
                        if (!double.TryParse(Next(args, ref i, a), NumberStyles.Float, CultureInfo.InvariantCulture, out o.Tolerance)
                            || o.Tolerance < 0 || double.IsNaN(o.Tolerance))
                            throw new ArgumentException("--tolerance must be a number >= 0");
                        break;
                    case "--max-diffs":
                        RequireCmd(o, a, Command.Compare);
                        if (!int.TryParse(Next(args, ref i, a), out o.MaxDiffs) || o.MaxDiffs < 0)
                            throw new ArgumentException("--max-diffs must be a whole number >= 0");
                        break;
                    default:
                        if (a.StartsWith("-")) throw new ArgumentException("Unknown option " + a);
                        inputs.Add(a); break;
                }
            }
            int needed = o.Command == Command.Compare ? 2 : 1;
            if (inputs.Count < needed) throw new ArgumentException(needed == 2 ? "compare needs two inputs" : "No input given");
            if (inputs.Count > needed) throw new ArgumentException(needed == 2 ? "compare takes exactly two inputs" : "Only one input path is allowed");
            o.Input = inputs[0];
            if (needed == 2) o.Input2 = inputs[1];
            return o;
        }

        static void RequireCmd(Options o, string name, params Command[] allowed)
        {
            if (Array.IndexOf(allowed, o.Command) < 0)
                throw new ArgumentException(name + " only applies to " + string.Join(" / ", allowed.Select(CommandName)));
        }

        public static string CommandName(Command c)
        {
            switch (c)
            {
                case Command.ToV4: return "to-v4";
                case Command.ToCsv: return "to-csv";
                case Command.ToV5: return "to-v5";
                case Command.Version: return "version";
                default: return "compare";
            }
        }

        static string NormaliseVersion(string v)
        {
            string s = v.Trim().TrimStart('v', 'V');
            if (s == "4" || s == "4.0") return "4.0";
            if (s == "5" || s == "5.1") return "5.1";
            throw new ArgumentException("--expect must be 4.0 or 5.1");
        }

        /// <summary>"hh:mm:ss[.fff]" or a number of seconds since midnight.</summary>
        static double ParseClock(string v)
        {
            double sec;
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out sec) && sec >= 0 && sec < 86400)
                return sec;
            TimeSpan ts;
            if (v.Contains(":") && TimeSpan.TryParse(v, CultureInfo.InvariantCulture, out ts) && ts >= TimeSpan.Zero && ts.TotalDays < 1)
                return ts.TotalSeconds;
            throw new ArgumentException("--start-time must be hh:mm:ss[.fff] or seconds since midnight (0 - 86399)");
        }

        static string Next(string[] args, ref int i, string name)
        {
            if (i + 1 >= args.Length) throw new ArgumentException(name + " needs a value");
            return args[++i];
        }

        public static void PrintUsage()
        {
            Console.WriteLine("Halcyon / Ethos trajectory log tool");
            Console.WriteLine();
            Console.WriteLine("  HalcyonTrajectoryLogTool to-v4  <input.bin | folder> [-o <file|folder>] [options]");
            Console.WriteLine("      Convert v5.1 logs to v4.0. Default output: 'v4.0' subfolder, same file name.");
            Console.WriteLine("      --keep-machine-info   keep machine specifier + serial number");
            Console.WriteLine("      --axis-scale <1|2|3>  header axis-scale value (axis data NOT converted)");
            Console.WriteLine("      --time-csv            save the removed time axis to <output>_time.csv");
            Console.WriteLine();
            Console.WriteLine("  HalcyonTrajectoryLogTool to-csv <input.bin | folder> [-o <folder>] [options]");
            Console.WriteLine("      Export v4.0 / v5.1 logs to <name>.csv.");
            Console.WriteLine("      --no-mlc              leave out MLC columns");
            Console.WriteLine("      --actual-only         write actual values only");
            Console.WriteLine();
            Console.WriteLine("  Validation / testing:");
            Console.WriteLine();
            Console.WriteLine("  HalcyonTrajectoryLogTool to-v5  <input.bin | folder> [-o <file|folder>] [options]");
            Console.WriteLine("      Convert v4.0 logs to v5.1. Default output: 'v5.1' subfolder, same file name.");
            Console.WriteLine("      --time-from <csv>     restore the time axis from a to-v4 --time-csv file");
            Console.WriteLine("      --start-time <t>      first snapshot clock time, hh:mm:ss[.fff] (default: from file name)");
            Console.WriteLine("      --serial <text>       machine serial number (last 6 characters)");
            Console.WriteLine("      --axis-scale <1|2|3>  header axis-scale value (axis data NOT converted)");
            Console.WriteLine();
            Console.WriteLine("  HalcyonTrajectoryLogTool version <input.bin | folder> [--expect <4.0|5.1>]");
            Console.WriteLine("      Print the version of each log; --expect fails any log of another version.");
            Console.WriteLine();
            Console.WriteLine("  HalcyonTrajectoryLogTool compare <a.bin | folderA> <b.bin | folderB> [options]");
            Console.WriteLine("      Compare header, subbeams and snapshots. Folders are matched by file name.");
            Console.WriteLine("      --ignore-version      ignore version string, time axis, axis order and machine info");
            Console.WriteLine("      --tolerance <x>       values within x count as equal (default 0 = exact)");
            Console.WriteLine("      --max-diffs <n>       individual value differences to list (default 10)");
            Console.WriteLine();
            Console.WriteLine("  Common: --overwrite, --ignore-crc");
        }
    }

    // =============================================================================================
    // Parsed trajectory log (v4.0 and v5.1)
    // =============================================================================================
    public sealed class Subbeam
    {
        public int ControlPoint;
        public float MU;
        public float RadTime;
        public int Seq;
        public string Name;
    }

    public sealed class TrajectoryLog
    {
        public const int HeaderSize = 1024;
        public const int SignatureBytes = 16;
        public const int VersionBytes = 16;
        public const int MetaDataBytes = 745;
        public const int SubbeamBytes = 560;
        public const int CrcBytes = 2;
        public const int TimeAxisId = 43;

        public byte[] Raw;
        public string Version;
        public bool VersionWide;
        public int SamplingIntervalMs;
        public int NumAxes;
        public int[] AxisIds;
        public int[] SamplesPerAxis;
        public int AxisScale;
        public int NumSubbeamsField;
        public int Truncated;
        public int NumSnapshots;
        public int MlcModel;
        public int MetaOffset;
        public string MetaDataText;
        public bool HasMachineInfo;          // v5.1 only
        public int MachineSpecifier;
        public string MachineSerial;
        public List<Subbeam> Subbeams = new List<Subbeam>();
        public int DataOffset;               // start of snapshot data
        public int FloatsPerSnapshot;        // (sum of samples) * 2
        public int[] AxisFloatOffset;        // float offset of each axis within a snapshot
        public int TimeAxisIndex = -1;
        public Crc16.Variant CrcVariant;
        public string CrcWarning;

        public static TrajectoryLog Parse(byte[] src, bool ignoreCrc)
        {
            var log = new TrajectoryLog { Raw = src };
            if (src.Length < HeaderSize + CrcBytes)
                throw new InvalidDataException("File is too short to be a trajectory log (" + src.Length + " bytes).");

            bool wide;
            string signature = BinUtil.ReadString(src, 0, SignatureBytes, out wide);
            if (signature != "VOSTL")
                throw new InvalidDataException("Signature is '" + signature + "', expected 'VOSTL'.");
            log.Version = BinUtil.ReadString(src, SignatureBytes, VersionBytes, out log.VersionWide);
            if (log.Version != "4.0" && log.Version != "5.1")
                throw new InvalidDataException("Unsupported version '" + log.Version + "' (4.0 and 5.1 are supported).");

            int p = SignatureBytes + VersionBytes;
            int headerSize = BinUtil.ReadInt(src, ref p);
            log.SamplingIntervalMs = BinUtil.ReadInt(src, ref p);
            log.NumAxes = BinUtil.ReadInt(src, ref p);
            if (headerSize != HeaderSize)
                throw new InvalidDataException("Header size is " + headerSize + ", expected 1024.");
            if (log.NumAxes <= 0 || log.NumAxes > 64)
                throw new InvalidDataException("Implausible number of axes: " + log.NumAxes);

            log.AxisIds = new int[log.NumAxes];
            for (int i = 0; i < log.NumAxes; i++) log.AxisIds[i] = BinUtil.ReadInt(src, ref p);
            log.SamplesPerAxis = new int[log.NumAxes];
            for (int i = 0; i < log.NumAxes; i++)
            {
                log.SamplesPerAxis[i] = BinUtil.ReadInt(src, ref p);
                if (log.SamplesPerAxis[i] <= 0 || log.SamplesPerAxis[i] > 1000)
                    throw new InvalidDataException("Implausible samples-per-axis value " + log.SamplesPerAxis[i] +
                                                   " for axis " + log.AxisIds[i] + ".");
            }

            log.AxisScale = BinUtil.ReadInt(src, ref p);
            log.NumSubbeamsField = BinUtil.ReadInt(src, ref p);
            log.Truncated = BinUtil.ReadInt(src, ref p);
            log.NumSnapshots = BinUtil.ReadInt(src, ref p);
            log.MlcModel = BinUtil.ReadInt(src, ref p);
            log.MetaOffset = p;
            if (log.MetaOffset + MetaDataBytes + 7 > HeaderSize)
                throw new InvalidDataException("Header fields overflow 1024 bytes; too many axes?");
            if (log.NumSubbeamsField < 0 || log.NumSnapshots < 0)
                throw new InvalidDataException("Negative subbeam or snapshot count.");
            log.MetaDataText = BinUtil.UpToNull(Encoding.UTF8.GetString(src, log.MetaOffset, MetaDataBytes));

            int mi = log.MetaOffset + MetaDataBytes;
            log.MachineSpecifier = src[mi];
            log.MachineSerial = BinUtil.UpToNull(Encoding.ASCII.GetString(src, mi + 1, 6));
            log.HasMachineInfo = log.Version == "5.1";

            log.FloatsPerSnapshot = log.SamplesPerAxis.Sum() * 2;
            log.AxisFloatOffset = new int[log.NumAxes];
            for (int i = 1; i < log.NumAxes; i++)
                log.AxisFloatOffset[i] = log.AxisFloatOffset[i - 1] + log.SamplesPerAxis[i - 1] * 2;
            log.TimeAxisIndex = Array.IndexOf(log.AxisIds, TimeAxisId);

            long expectedLength = (long)HeaderSize + (long)log.NumSubbeamsField * SubbeamBytes
                                  + (long)log.NumSnapshots * log.FloatsPerSnapshot * 4 + CrcBytes;
            if (src.Length != expectedLength)
                throw new InvalidDataException("File length " + src.Length + " does not match header (expected " +
                                               expectedLength + ").");

            for (int k = 0; k < log.NumSubbeamsField; k++)
            {
                int o = HeaderSize + k * SubbeamBytes;
                int q = o;
                var sb = new Subbeam();
                sb.ControlPoint = BinUtil.ReadInt(src, ref q);
                sb.MU = BinUtil.ReadFloat(src, q); q += 4;
                sb.RadTime = BinUtil.ReadFloat(src, q); q += 4;
                sb.Seq = BinUtil.ReadInt(src, ref q);
                bool w;
                sb.Name = BinUtil.ReadString(src, q, 512, out w);
                log.Subbeams.Add(sb);
            }
            log.DataOffset = HeaderSize + log.NumSubbeamsField * SubbeamBytes;

            ushort storedCrc = (ushort)(src[src.Length - 2] | (src[src.Length - 1] << 8));
            log.CrcVariant = Crc16.Detect(src, src.Length - CrcBytes, storedCrc);
            if (log.CrcVariant == null)
            {
                string msg = string.Format("Input CRC 0x{0:X4} does not verify - file may be corrupt.", storedCrc);
                if (!ignoreCrc) throw new InvalidDataException(msg + " (use --ignore-crc to force)");
                log.CrcWarning = msg;
                log.CrcVariant = Crc16.Variants[0];
            }
            return log;
        }

        /// <summary>
        /// Quick version test: reads only the signature and version string (first 32 bytes), so it
        /// does not check the rest of the file. Returns e.g. "4.0" or "5.1".
        /// </summary>
        public static string ReadVersion(string path)
        {
            var head = new byte[SignatureBytes + VersionBytes];
            int read = 0;
            using (var fs = File.OpenRead(path))
                while (read < head.Length)
                {
                    int n = fs.Read(head, read, head.Length - read);
                    if (n == 0) throw new InvalidDataException("File is too short to be a trajectory log (" + read + " bytes).");
                    read += n;
                }
            bool wide;
            string signature = BinUtil.ReadString(head, 0, SignatureBytes, out wide);
            if (signature != "VOSTL")
                throw new InvalidDataException("Signature is '" + signature + "', expected 'VOSTL'.");
            return BinUtil.ReadString(head, SignatureBytes, VersionBytes, out wide);
        }

        public static bool IsVersion4(string path) { return ReadVersion(path) == "4.0"; }
        public static bool IsVersion51(string path) { return ReadVersion(path) == "5.1"; }

        int ValueOffset(int snapshot, int axisIndex, int sample)
        {
            return DataOffset + (snapshot * FloatsPerSnapshot + AxisFloatOffset[axisIndex] + sample * 2) * 4;
        }

        /// <summary>Expected value; values are stored as (expected, actual) pairs per sample.</summary>
        public float Expected(int snapshot, int axisIndex, int sample)
        {
            return BinUtil.ReadFloat(Raw, ValueOffset(snapshot, axisIndex, sample));
        }

        public float Actual(int snapshot, int axisIndex, int sample)
        {
            return BinUtil.ReadFloat(Raw, ValueOffset(snapshot, axisIndex, sample) + 4);
        }

        public static string AxisName(int id)
        {
            switch (id)
            {
                case 0: return "CollRtn";
                case 1: return "GantryRtn";
                case 2: return "Y1";
                case 3: return "Y2";
                case 4: return "X1";
                case 5: return "X2";
                case 6: return "CouchVrt";
                case 7: return "CouchLng";
                case 8: return "CouchLat";
                case 9: return "CouchRtn";
                case 10: return "CouchPit";
                case 11: return "CouchRol";
                case 40: return "MU";
                case 41: return "BeamHold";
                case 42: return "ControlPoint";
                case 43: return "Time";
                case 50: return "MLC";
                default: return "Axis" + id;
            }
        }

        public static string AxisUnit(int id)
        {
            if (id == 0 || id == 1 || id == 9 || id == 10 || id == 11) return "deg";
            if ((id >= 2 && id <= 8) || id == 50) return "cm";
            if (id == 40) return "MU";
            if (id == 43) return "s";
            return "";
        }

        public static string AxisScaleName(int s)
        {
            switch (s)
            {
                case 1: return "Machine scale (couch in machine representation)";
                case 2: return "Modified IEC 61217";
                case 3: return "Machine scale (couch in isocentric representation)";
                default: return "Unknown";
            }
        }

        public static string DoseServoName(float v)
        {
            switch ((int)Math.Round(v))
            {
                case 0: return "Normal";
                case 1: return "Freeze";
                case 2: return "Hold";
                case 3: return "Disabled";
                default: return "";
            }
        }
    }

    // =============================================================================================
    // v5.1 -> v4.0
    // =============================================================================================
    public static class V4Converter
    {
        public static byte[] Convert(TrajectoryLog log, Options opt, List<string> notes)
        {
            byte[] src = log.Raw;
            int timeIdx = log.TimeAxisIndex;
            if (timeIdx < 0) notes.Add("No time axis (43) found; axis data copied unchanged.");
            if (log.CrcWarning != null) notes.Add("WARNING: " + log.CrcWarning + " Output CRC written as " + log.CrcVariant.Name + ".");
            else if (log.CrcVariant != Crc16.Variants[0])
                notes.Add("Input CRC matched " + log.CrcVariant.Name + "; the same variant is used for the output.");

            int[] keepIdx = Enumerable.Range(0, log.NumAxes).Where(i => i != timeIdx).ToArray();
            int newAxisScale = opt.AxisScale ?? log.AxisScale;

            // Header
            var header = new byte[TrajectoryLog.HeaderSize]; // zero-filled => reserved bytes are 0
            Buffer.BlockCopy(src, 0, header, 0, TrajectoryLog.SignatureBytes);
            BinUtil.WriteString(header, TrajectoryLog.SignatureBytes, TrajectoryLog.VersionBytes, "4.0", log.VersionWide);
            int q = TrajectoryLog.SignatureBytes + TrajectoryLog.VersionBytes;
            BinUtil.WriteInt(header, ref q, TrajectoryLog.HeaderSize);
            BinUtil.WriteInt(header, ref q, log.SamplingIntervalMs);
            BinUtil.WriteInt(header, ref q, keepIdx.Length);
            foreach (int i in keepIdx) BinUtil.WriteInt(header, ref q, log.AxisIds[i]);
            foreach (int i in keepIdx) BinUtil.WriteInt(header, ref q, log.SamplesPerAxis[i]);
            BinUtil.WriteInt(header, ref q, newAxisScale);
            BinUtil.WriteInt(header, ref q, log.NumSubbeamsField);
            BinUtil.WriteInt(header, ref q, log.Truncated);
            BinUtil.WriteInt(header, ref q, log.NumSnapshots);
            BinUtil.WriteInt(header, ref q, log.MlcModel);
            Buffer.BlockCopy(src, log.MetaOffset, header, q, TrajectoryLog.MetaDataBytes);
            q += TrajectoryLog.MetaDataBytes;
            if (opt.KeepMachineInfo)
                Buffer.BlockCopy(src, log.MetaOffset + TrajectoryLog.MetaDataBytes, header, q, 7);

            // Body
            int snapBytes = log.FloatsPerSnapshot * 4;
            int timeBytes = timeIdx >= 0 ? log.SamplesPerAxis[timeIdx] * 2 * 4 : 0;
            int newSnapBytes = snapBytes - timeBytes;
            long outLength = (long)TrajectoryLog.HeaderSize + (long)log.NumSubbeamsField * TrajectoryLog.SubbeamBytes
                             + (long)log.NumSnapshots * newSnapBytes + TrajectoryLog.CrcBytes;
            if (outLength > int.MaxValue) throw new InvalidDataException("Output would exceed 2 GB.");
            var dst = new byte[outLength];

            Buffer.BlockCopy(header, 0, dst, 0, TrajectoryLog.HeaderSize);
            Buffer.BlockCopy(src, TrajectoryLog.HeaderSize, dst, TrajectoryLog.HeaderSize,
                             log.NumSubbeamsField * TrajectoryLog.SubbeamBytes);

            int dstPos = log.DataOffset;
            if (timeIdx < 0)
            {
                Buffer.BlockCopy(src, log.DataOffset, dst, dstPos, log.NumSnapshots * snapBytes);
            }
            else
            {
                int before = log.AxisFloatOffset[timeIdx] * 4;
                int after = snapBytes - before - timeBytes;
                for (int s = 0; s < log.NumSnapshots; s++)
                {
                    int sp = log.DataOffset + s * snapBytes;
                    Buffer.BlockCopy(src, sp, dst, dstPos, before);
                    dstPos += before;
                    Buffer.BlockCopy(src, sp + before + timeBytes, dst, dstPos, after);
                    dstPos += after;
                }
            }

            int crcPos = dst.Length - TrajectoryLog.CrcBytes;
            ushort crc = log.CrcVariant.Compute(dst, crcPos);
            dst[crcPos] = (byte)(crc & 0xFF);
            dst[crcPos + 1] = (byte)(crc >> 8);

            notes.Add(string.Format(CultureInfo.InvariantCulture,
                "{0} axes -> {1}, {2} subbeam(s), {3} snapshots, {4:N0} -> {5:N0} bytes",
                log.NumAxes, keepIdx.Length, log.NumSubbeamsField, log.NumSnapshots, src.Length, dst.Length));
            if (log.AxisScale == 3 && newAxisScale == 3)
                notes.Add("Axis scale kept at 3 (machine scale, isocentric couch) - valid in v4.0.");
            if (opt.AxisScale.HasValue && opt.AxisScale.Value != log.AxisScale)
                notes.Add(string.Format("WARNING: axis-scale flag changed {0} -> {1}; axis values were NOT converted.",
                                        log.AxisScale, newAxisScale));
            if (!opt.KeepMachineInfo)
                notes.Add(string.Format("Machine specifier {0} / serial '{1}' removed from header.",
                                        log.MachineSpecifier, log.MachineSerial));
            return dst;
        }
    }

    // =============================================================================================
    // v4.0 -> v5.1 (validation / testing)
    // =============================================================================================
    public static class V5Converter
    {
        /// <summary>
        /// Axis order of HAL 5.0 (v5.1) logs, after the time axis, as found in machine logs; the spec
        /// does not state it. v4.0 logs use 0 - 11, 40, 41, 42, 50.
        /// </summary>
        public static readonly int[] V51AxisOrder = { 42, 40, 41, 1, 0, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 50 };

        /// <param name="sourcePath">Input file; its "_yyyyMMddHHmmss" stamp gives the default start time.</param>
        public static byte[] Convert(TrajectoryLog log, Options opt, string sourcePath, List<string> notes)
        {
            byte[] src = log.Raw;
            if (log.TimeAxisIndex >= 0) throw new InvalidDataException("Log already has a time axis (43).");
            if (log.CrcWarning != null) notes.Add("WARNING: " + log.CrcWarning + " Output CRC written as " + log.CrcVariant.Name + ".");
            else if (log.CrcVariant != Crc16.Variants[0])
                notes.Add("Input CRC matched " + log.CrcVariant.Name + "; the same variant is used for the output.");

            float[] expTime, actTime;
            TimeValues(log, opt, sourcePath, notes, out expTime, out actTime);

            // Axes in the order HAL 5.0 writes them; any other axes follow in their input order.
            // srcAxis[j] = input axis index of output axis j, or -1 for the new time axis.
            var srcAxis = new List<int> { -1 };
            foreach (int id in V51AxisOrder)
            {
                int i = Array.IndexOf(log.AxisIds, id);
                if (i >= 0) srcAxis.Add(i);
            }
            for (int i = 0; i < log.NumAxes; i++)
                if (!srcAxis.Contains(i)) srcAxis.Add(i);
            var axisIds = srcAxis.Select(i => i < 0 ? TrajectoryLog.TimeAxisId : log.AxisIds[i]).ToList();
            var samples = srcAxis.Select(i => i < 0 ? 1 : log.SamplesPerAxis[i]).ToList();
            bool reordered = !srcAxis.Skip(1).SequenceEqual(Enumerable.Range(0, log.NumAxes));
            int newAxisScale = opt.AxisScale ?? log.AxisScale;

            // Header
            var header = new byte[TrajectoryLog.HeaderSize]; // zero-filled => reserved bytes are 0
            Buffer.BlockCopy(src, 0, header, 0, TrajectoryLog.SignatureBytes);
            BinUtil.WriteString(header, TrajectoryLog.SignatureBytes, TrajectoryLog.VersionBytes, "5.1", log.VersionWide);
            int q = TrajectoryLog.SignatureBytes + TrajectoryLog.VersionBytes;
            BinUtil.WriteInt(header, ref q, TrajectoryLog.HeaderSize);
            BinUtil.WriteInt(header, ref q, log.SamplingIntervalMs);
            BinUtil.WriteInt(header, ref q, axisIds.Count);
            foreach (int id in axisIds) BinUtil.WriteInt(header, ref q, id);
            foreach (int n in samples) BinUtil.WriteInt(header, ref q, n);
            BinUtil.WriteInt(header, ref q, newAxisScale);
            BinUtil.WriteInt(header, ref q, log.NumSubbeamsField);
            BinUtil.WriteInt(header, ref q, log.Truncated);
            BinUtil.WriteInt(header, ref q, log.NumSnapshots);
            BinUtil.WriteInt(header, ref q, log.MlcModel);
            if (q + TrajectoryLog.MetaDataBytes + 7 > TrajectoryLog.HeaderSize)
                throw new InvalidDataException("No room in the 1024-byte header for another axis.");
            // Metadata + machine specifier / serial number.
            Buffer.BlockCopy(src, log.MetaOffset, header, q, TrajectoryLog.MetaDataBytes + 7);
            int mi = q + TrajectoryLog.MetaDataBytes;
            if (log.MachineSpecifier == 0 && log.MachineSerial.Length == 0)
            {
                header[mi] = 1; // 0 would mean TrueBeam
                notes.Add("Machine specifier set to 1 (Halcyon / Ethos); the input had none.");
            }
            if (opt.Serial != null)
            {
                Array.Clear(header, mi + 1, 6);
                byte[] serial = Encoding.ASCII.GetBytes(opt.Serial);
                Buffer.BlockCopy(serial, 0, header, mi + 1, serial.Length);
            }

            // Body
            int snapBytes = log.FloatsPerSnapshot * 4;
            const int timeBytes = 2 * 4;
            long outLength = (long)TrajectoryLog.HeaderSize + (long)log.NumSubbeamsField * TrajectoryLog.SubbeamBytes
                             + (long)log.NumSnapshots * (snapBytes + timeBytes) + TrajectoryLog.CrcBytes;
            if (outLength > int.MaxValue) throw new InvalidDataException("Output would exceed 2 GB.");
            var dst = new byte[outLength];

            Buffer.BlockCopy(header, 0, dst, 0, TrajectoryLog.HeaderSize);
            Buffer.BlockCopy(src, TrajectoryLog.HeaderSize, dst, TrajectoryLog.HeaderSize,
                             log.NumSubbeamsField * TrajectoryLog.SubbeamBytes);

            int dstPos = log.DataOffset;
            for (int s = 0; s < log.NumSnapshots; s++)
            {
                int sp = log.DataOffset + s * snapBytes;
                foreach (int i in srcAxis)
                {
                    if (i < 0)
                    {
                        BinUtil.WriteFloat(dst, dstPos, expTime[s]);
                        BinUtil.WriteFloat(dst, dstPos + 4, actTime[s]);
                        dstPos += timeBytes;
                        continue;
                    }
                    int bytes = log.SamplesPerAxis[i] * 2 * 4;
                    Buffer.BlockCopy(src, sp + log.AxisFloatOffset[i] * 4, dst, dstPos, bytes);
                    dstPos += bytes;
                }
            }

            int crcPos = dst.Length - TrajectoryLog.CrcBytes;
            ushort crc = log.CrcVariant.Compute(dst, crcPos);
            dst[crcPos] = (byte)(crc & 0xFF);
            dst[crcPos + 1] = (byte)(crc >> 8);

            notes.Add(string.Format(CultureInfo.InvariantCulture,
                "{0} axes -> {1}, {2} subbeam(s), {3} snapshots, {4:N0} -> {5:N0} bytes",
                log.NumAxes, axisIds.Count, log.NumSubbeamsField, log.NumSnapshots, src.Length, dst.Length));
            if (reordered)
                notes.Add("Axes reordered to the v5.1 order: " + string.Join(", ", axisIds.Select(TrajectoryLog.AxisName)) + ".");
            if (opt.AxisScale.HasValue && opt.AxisScale.Value != log.AxisScale)
                notes.Add(string.Format("WARNING: axis-scale flag changed {0} -> {1}; axis values were NOT converted.",
                                        log.AxisScale, newAxisScale));
            else if (newAxisScale != 3)
                notes.Add("WARNING: axis scale kept at " + newAxisScale + "; HAL 5.0 v5.1 logs use 3 (couch values are not converted).");
            return dst;
        }

        /// <summary>Time axis values per snapshot: from --time-from, else start time + index x interval.</summary>
        static void TimeValues(TrajectoryLog log, Options opt, string sourcePath, List<string> notes,
                               out float[] expected, out float[] actual)
        {
            int n = log.NumSnapshots;
            expected = new float[n];
            actual = new float[n];

            if (opt.TimeFrom != null)
            {
                string[] lines = File.ReadAllLines(opt.TimeFrom).Skip(1).Where(l => l.Trim().Length > 0).ToArray();
                if (lines.Length != n)
                    throw new InvalidDataException("Time CSV has " + lines.Length + " rows but the log has " + n + " snapshots: " + opt.TimeFrom);
                for (int s = 0; s < n; s++)
                {
                    string[] f = lines[s].Split(',');
                    float e, a;
                    double sec;
                    // Expected / Actual columns (exact values) if present, else SecondsSinceMidnight.
                    if (f.Length >= 5 && float.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out e)
                                      && float.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out a))
                    {
                        expected[s] = e; actual[s] = a;
                    }
                    else if (f.Length >= 2 && double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out sec))
                        expected[s] = (float)sec; // actual stays 0 ("empty" per the spec)
                    else
                        throw new InvalidDataException("Time CSV row " + (s + 2) + " is not valid: " + lines[s]);
                }
                notes.Add("Time axis restored from " + opt.TimeFrom);
                return;
            }

            double start;
            string from;
            TimeSpan stamp;
            if (opt.StartTime.HasValue) { start = opt.StartTime.Value; from = "--start-time"; }
            else if (FileUtil.TryTimeFromName(sourcePath, out stamp)) { start = stamp.TotalSeconds; from = "file name"; }
            else { start = 0; from = "no time stamp in file name"; }
            for (int s = 0; s < n; s++)
                expected[s] = (float)((start + s * log.SamplingIntervalMs / 1000.0) % 86400); // actual stays 0
            notes.Add("Time axis generated: " + Fmt.Clock(start) + " (" + from + ") + snapshot x " + log.SamplingIntervalMs +
                      " ms. v4.0 does not record beam pauses, so clock times are approximate.");
        }
    }

    // =============================================================================================
    // Compare two logs (validation / testing)
    // =============================================================================================
    public sealed class CompareResult
    {
        public bool ByteIdentical;
        public long DifferenceCount;                          // header/subbeam fields + individual values
        public List<string> Differences = new List<string>();
        public List<string> Info = new List<string>();        // differences that were ignored, etc.
        public bool Same { get { return DifferenceCount == 0; } }
    }

    public static class LogComparer
    {
        /// <summary>
        /// Compares header fields, subbeams and every expected / actual snapshot value. Axes are matched
        /// by axis id, so a v4.0 and a v5.1 log can be compared; with <paramref name="ignoreVersion"/>
        /// the version string, the time axis and the machine specifier / serial number are not compared.
        /// Values count as equal when within <paramref name="tolerance"/> (0 = exactly equal).
        /// </summary>
        public static CompareResult Compare(TrajectoryLog a, TrajectoryLog b, double tolerance = 0,
                                            bool ignoreVersion = false, int maxValueDiffs = 10)
        {
            var r = new CompareResult();
            if (a.Raw.Length == b.Raw.Length && a.Raw.SequenceEqual(b.Raw)) { r.ByteIdentical = true; return r; }

            Action<string, object, object> field = (name, x, y) =>
            {
                if (Equals(x, y)) return;
                r.DifferenceCount++;
                r.Differences.Add(name + ": " + x + " vs " + y);
            };

            // ---- header ----
            if (!ignoreVersion) field("Version", a.Version, b.Version);
            else if (a.Version != b.Version) r.Info.Add("version " + a.Version + " vs " + b.Version + " ignored");
            field("Sampling interval (ms)", a.SamplingIntervalMs, b.SamplingIntervalMs);
            field("Axis scale", a.AxisScale, b.AxisScale);
            field("Number of subbeams", a.NumSubbeamsField, b.NumSubbeamsField);
            field("Truncated", a.Truncated, b.Truncated);
            field("Number of snapshots", a.NumSnapshots, b.NumSnapshots);
            field("MLC model", a.MlcModel, b.MlcModel);
            field("Metadata", Quote(a.MetaDataText), Quote(b.MetaDataText));
            if (!ignoreVersion)
            {
                field("Machine specifier", a.MachineSpecifier, b.MachineSpecifier);
                field("Machine serial", Quote(a.MachineSerial), Quote(b.MachineSerial));
            }

            Func<TrajectoryLog, int[]> axes = l => l.AxisIds.Where(id => !(ignoreVersion && id == TrajectoryLog.TimeAxisId)).ToArray();
            int[] axesA = axes(a), axesB = axes(b);
            // v4.0 and v5.1 logs store the axes in a different order, so with ignoreVersion only the
            // set of axes has to match.
            if (ignoreVersion && axesA.Length == axesB.Length && !axesA.Except(axesB).Any() && !axesB.Except(axesA).Any())
            {
                if (!axesA.SequenceEqual(axesB)) r.Info.Add("axis order differs, ignored");
            }
            else if (!axesA.SequenceEqual(axesB))
                field("Axes", AxisList(axesA), AxisList(axesB));
            if (ignoreVersion && (a.TimeAxisIndex >= 0) != (b.TimeAxisIndex >= 0))
                r.Info.Add("time axis only in " + (a.TimeAxisIndex >= 0 ? "A" : "B") + ", ignored");

            // ---- subbeams ----
            for (int k = 0; k < Math.Min(a.Subbeams.Count, b.Subbeams.Count); k++)
            {
                Subbeam x = a.Subbeams[k], y = b.Subbeams[k];
                string p = "Subbeam " + (k + 1) + " ";
                field(p + "control point", x.ControlPoint, y.ControlPoint);
                if (!Same(x.MU, y.MU, tolerance)) field(p + "MU", Fmt.Exact(x.MU), Fmt.Exact(y.MU));
                if (!Same(x.RadTime, y.RadTime, tolerance)) field(p + "radiation time", Fmt.Exact(x.RadTime), Fmt.Exact(y.RadTime));
                field(p + "sequence", x.Seq, y.Seq);
                field(p + "name", Quote(x.Name), Quote(y.Name));
            }

            // ---- snapshots (axes matched by id) ----
            int snaps = Math.Min(a.NumSnapshots, b.NumSnapshots);
            int listed = 0;
            foreach (int id in axesA.Distinct())
            {
                int ia = Array.IndexOf(a.AxisIds, id), ib = Array.IndexOf(b.AxisIds, id);
                if (ib < 0) continue; // already reported in the axis list
                string axis = TrajectoryLog.AxisName(id);
                int n = a.SamplesPerAxis[ia];
                if (n != b.SamplesPerAxis[ib])
                {
                    field(axis + " samples per snapshot", n, b.SamplesPerAxis[ib]);
                    continue;
                }

                long count = 0;
                var details = new List<string>();
                var diffSnapshots = new HashSet<int>();
                double maxDiff = 0;
                for (int s = 0; s < snaps; s++)
                    for (int k = 0; k < n; k++)
                        for (int act = 0; act < 2; act++)
                        {
                            float x = act == 0 ? a.Expected(s, ia, k) : a.Actual(s, ia, k);
                            float y = act == 0 ? b.Expected(s, ib, k) : b.Actual(s, ib, k);
                            if (Same(x, y, tolerance)) continue;
                            count++;
                            diffSnapshots.Add(s);
                            double d = Math.Abs((double)x - y);
                            if (!double.IsNaN(d) && d > maxDiff) maxDiff = d;
                            if (listed < maxValueDiffs)
                            {
                                listed++;
                                details.Add(string.Format(CultureInfo.InvariantCulture, "  snapshot {0}, {1} {2}: {3} vs {4}",
                                    s + 1, SampleName(id, k, n), act == 0 ? "expected" : "actual", Fmt.Exact(x), Fmt.Exact(y)));
                            }
                        }
                if (count == 0) continue;
                r.DifferenceCount += count;
                r.Differences.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0}: {1:N0} value(s) differ in {2:N0} snapshot(s), max |difference| {3}",
                    axis, count, diffSnapshots.Count, maxDiff.ToString("G6", CultureInfo.InvariantCulture)));
                r.Differences.AddRange(details);
            }
            if (r.Differences.Count > 0 && listed == maxValueDiffs && maxValueDiffs > 0)
                r.Info.Add("value differences listed up to --max-diffs " + maxValueDiffs);
            if (r.Same && a.CrcVariant != b.CrcVariant)
                r.Info.Add("CRC variant " + a.CrcVariant.Name + " vs " + b.CrcVariant.Name);
            return r;
        }

        /// <summary>Equal bit pattern, both NaN, or within the tolerance.</summary>
        static bool Same(float x, float y, double tolerance)
        {
            if (x == y || (float.IsNaN(x) && float.IsNaN(y))) return true;
            return tolerance > 0 && Math.Abs((double)x - y) <= tolerance;
        }

        static string SampleName(int axisId, int k, int total)
        {
            if (axisId == 50) return CsvExporter.MlcColumn(k, total);
            return TrajectoryLog.AxisName(axisId) + (total > 1 ? "_" + (k + 1) : "");
        }

        static string AxisList(int[] ids) { return "[" + string.Join(", ", ids.Select(TrajectoryLog.AxisName)) + "]"; }

        static string Quote(string s) { return "'" + s + "'"; }
    }

    // =============================================================================================
    // CSV export
    // =============================================================================================
    public static class CsvExporter
    {
        const float NoValue = 3.0e38f; // log uses float.MaxValue for axes that don't exist (jaws)

        public static string DataCsv(TrajectoryLog log, Options opt)
        {
            bool both = !opt.ActualOnly;
            int cpIdx = Array.IndexOf(log.AxisIds, 42);
            int timeIdx = log.TimeAxisIndex;
            var sb = new StringBuilder(log.NumSnapshots * 64);
            bool[] hasData = AxesWithData(log);

            // ---- column header ----
            var cols = new List<string> { "Snapshot", "ElapsedTime_s" };
            if (timeIdx >= 0) cols.Add("ClockTime");
            if (log.Subbeams.Count > 0 && cpIdx >= 0) cols.Add("Subbeam");
            for (int a = 0; a < log.NumAxes; a++)
            {
                int id = log.AxisIds[a];
                if (id == TrajectoryLog.TimeAxisId) continue;
                if (id == 42) { cols.Add("ControlPoint"); continue; }
                if (id == 50)
                {
                    if (opt.NoMlc) continue;
                    for (int k = 0; k < log.SamplesPerAxis[a]; k++)
                        AddPair(cols, MlcColumn(k, log.SamplesPerAxis[a]) + "_cm", both);
                    continue;
                }
                if (!hasData[a]) continue;
                string unit = TrajectoryLog.AxisUnit(id);
                string name = TrajectoryLog.AxisName(id) + (unit.Length > 0 && unit != "MU" ? "_" + unit : "");
                if (log.SamplesPerAxis[a] == 1) AddPair(cols, name, both);
                else for (int k = 0; k < log.SamplesPerAxis[a]; k++) AddPair(cols, name + "_" + (k + 1), both);
                if (id == 41) cols.Add("DoseServoState");
            }
            sb.Append(string.Join(",", cols)).Append("\r\n");

            // ---- rows ----
            double t0 = timeIdx >= 0 && log.NumSnapshots > 0 ? log.Expected(0, timeIdx, 0) : 0;
            for (int s = 0; s < log.NumSnapshots; s++)
            {
                sb.Append(s + 1);
                if (timeIdx >= 0)
                {
                    double t = log.Expected(s, timeIdx, 0);
                    double el = t - t0;
                    if (el < -43200) el += 86400; // treatment crossed midnight
                    sb.Append(',').Append(Fmt.Fixed(el, 3)).Append(',').Append(Fmt.Clock(t));
                }
                else
                {
                    sb.Append(',').Append(Fmt.Fixed(s * log.SamplingIntervalMs / 1000.0, 3));
                }
                if (log.Subbeams.Count > 0 && cpIdx >= 0)
                    sb.Append(',').Append(SubbeamFor(log, log.Expected(s, cpIdx, 0)));

                for (int a = 0; a < log.NumAxes; a++)
                {
                    int id = log.AxisIds[a];
                    if (id == TrajectoryLog.TimeAxisId) continue;
                    if (id == 42) { sb.Append(',').Append(Fmt.Num(log.Expected(s, a, 0))); continue; }
                    if ((id == 50 && opt.NoMlc) || !hasData[a]) continue;
                    for (int k = 0; k < log.SamplesPerAxis[a]; k++)
                    {
                        if (both) sb.Append(',').Append(Val(log.Expected(s, a, k)));
                        sb.Append(',').Append(Val(log.Actual(s, a, k)));
                    }
                    if (id == 41) sb.Append(',').Append(TrajectoryLog.DoseServoName(log.Actual(s, a, 0)));
                }
                sb.Append("\r\n");
            }
            return sb.ToString();
        }

        /// <summary>
        /// False for axes that hold the "no value" marker in every snapshot (e.g. Halcyon jaws);
        /// those are left out of the data CSV.
        /// </summary>
        public static bool[] AxesWithData(TrajectoryLog log)
        {
            var has = new bool[log.NumAxes];
            for (int a = 0; a < log.NumAxes; a++)
            {
                for (int s = 0; s < log.NumSnapshots && !has[a]; s++)
                    for (int k = 0; k < log.SamplesPerAxis[a] && !has[a]; k++)
                        if (Math.Abs(log.Expected(s, a, k)) < NoValue || Math.Abs(log.Actual(s, a, k)) < NoValue)
                            has[a] = true;
                if (log.NumSnapshots == 0) has[a] = true;
            }
            return has;
        }

        static void AddPair(List<string> cols, string name, bool both)
        {
            if (both) cols.Add(name + "_Expected");
            cols.Add(name + "_Actual");
        }

        // First two MLC samples are the banks, the rest are leaves (spec: "all leaves plus the 2 banks").
        public static string MlcColumn(int k, int total)
        {
            int leaves = total - 2;
            if (k < 2) return "MLC_Bank" + (k + 1);
            return "MLC_Leaf" + (k - 1).ToString(leaves >= 100 ? "000" : "00", CultureInfo.InvariantCulture);
        }

        // Subbeam = last subbeam whose starting control point is <= the current control point.
        static int SubbeamFor(TrajectoryLog log, float cp)
        {
            int result = 1;
            for (int k = 0; k < log.Subbeams.Count; k++)
                if (log.Subbeams[k].ControlPoint <= cp + 1e-4) result = k + 1;
            return result;
        }

        static string Val(float v)
        {
            if (float.IsNaN(v) || Math.Abs(v) >= NoValue) return "";
            return Fmt.Num(v);
        }
    }

    // =============================================================================================
    // Helpers
    // =============================================================================================
    public static class Csv
    {
        public static string Field(string s)
        {
            if (s == null) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }

    public static class Fmt
    {
        // 7 significant digits = full single-precision resolution, without float noise
        // (e.g. 101.9 rather than 101.899994).
        public static string Num(float v) { return v.ToString("G7", CultureInfo.InvariantCulture); }

        // 9 significant digits always round-trip a float exactly.
        public static string Exact(float v) { return v.ToString("G9", CultureInfo.InvariantCulture); }

        public static string Fixed(double v, int decimals)
        {
            return v.ToString("F" + decimals, CultureInfo.InvariantCulture);
        }

        public static string Clock(double secondsSinceMidnight)
        {
            if (double.IsNaN(secondsSinceMidnight) || secondsSinceMidnight < 0 || secondsSinceMidnight >= 172800) return "";
            return TimeSpan.FromSeconds(secondsSinceMidnight).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
        }
    }

    public static class FileUtil
    {
        static readonly Regex StampRx = new Regex(@"(?<!\d)\d{14}(?!\d)");

        /// <summary>Time of day from a "yyyyMMddHHmmss" stamp in the file name (last one wins).</summary>
        public static bool TryTimeFromName(string path, out TimeSpan time)
        {
            MatchCollection ms = StampRx.Matches(Path.GetFileNameWithoutExtension(path) ?? "");
            for (int i = ms.Count - 1; i >= 0; i--)
            {
                DateTime d;
                if (DateTime.TryParseExact(ms[i].Value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                {
                    time = d.TimeOfDay;
                    return true;
                }
            }
            time = TimeSpan.Zero;
            return false;
        }

        /// <summary>Write via a temp file so a failure never leaves a half-written output behind.</summary>
        public static void WriteAtomic(string path, byte[] data)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, data);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
    }

    public static class BinUtil
    {
        public static string UpToNull(string s)
        {
            int z = s.IndexOf('\0');
            return z >= 0 ? s.Substring(0, z) : s;
        }

        public static int ReadInt(byte[] b, ref int p)
        {
            int v = b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24);
            p += 4;
            return v;
        }

        public static void WriteInt(byte[] b, ref int p, int v)
        {
            b[p] = (byte)v; b[p + 1] = (byte)(v >> 8); b[p + 2] = (byte)(v >> 16); b[p + 3] = (byte)(v >> 24);
            p += 4;
        }

        public static void WriteFloat(byte[] b, int p, float v)
        {
            byte[] f = BitConverter.GetBytes(v);
            if (!BitConverter.IsLittleEndian) Array.Reverse(f);
            Buffer.BlockCopy(f, 0, b, p, 4);
        }

        public static float ReadFloat(byte[] b, int p)
        {
            if (BitConverter.IsLittleEndian) return BitConverter.ToSingle(b, p);
            var r = new[] { b[p + 3], b[p + 2], b[p + 1], b[p] };
            return BitConverter.ToSingle(r, 0);
        }

        // The spec says "zero terminated Unicode string"; logs in the field use 8-bit text.
        // UTF-16LE is detected (and preserved on write) in case a writer uses it.
        public static string ReadString(byte[] b, int offset, int length, out bool wide)
        {
            wide = length >= 4 && b[offset] != 0 && b[offset + 1] == 0 && b[offset + 2] != 0 && b[offset + 3] == 0;
            string s = wide ? Encoding.Unicode.GetString(b, offset, length)
                            : Encoding.UTF8.GetString(b, offset, length);
            int z = s.IndexOf('\0');
            return (z >= 0 ? s.Substring(0, z) : s).Trim();
        }

        public static void WriteString(byte[] b, int offset, int length, string value, bool wide)
        {
            Array.Clear(b, offset, length);
            byte[] bytes = wide ? Encoding.Unicode.GetBytes(value) : Encoding.ASCII.GetBytes(value);
            Buffer.BlockCopy(bytes, 0, b, offset, Math.Min(bytes.Length, length - (wide ? 2 : 1)));
        }
    }

    /// <summary>
    /// 16-bit CCITT CRC (poly 0x1021, seed 0xFFFF) as stated in the spec. The spec does not say
    /// whether the bit order is reflected or whether a final XOR is applied, so the variant that
    /// reproduces the input file's CRC is detected and reused for the output.
    /// </summary>
    public static class Crc16
    {
        public sealed class Variant
        {
            public readonly string Name;
            readonly bool _reflected;
            readonly ushort _xorOut;
            readonly ushort[] _table = new ushort[256];

            public Variant(string name, bool reflected, ushort xorOut)
            {
                Name = name; _reflected = reflected; _xorOut = xorOut;
                for (int i = 0; i < 256; i++)
                {
                    ushort c;
                    if (reflected)
                    {
                        c = (ushort)i;
                        for (int k = 0; k < 8; k++) c = (ushort)((c & 1) != 0 ? (c >> 1) ^ 0x8408 : c >> 1);
                    }
                    else
                    {
                        c = (ushort)(i << 8);
                        for (int k = 0; k < 8; k++) c = (ushort)((c & 0x8000) != 0 ? (c << 1) ^ 0x1021 : c << 1);
                    }
                    _table[i] = c;
                }
            }

            public ushort Compute(byte[] data, int length)
            {
                ushort crc = 0xFFFF;
                if (_reflected)
                    for (int i = 0; i < length; i++) crc = (ushort)((crc >> 8) ^ _table[(crc ^ data[i]) & 0xFF]);
                else
                    for (int i = 0; i < length; i++) crc = (ushort)((crc << 8) ^ _table[((crc >> 8) ^ data[i]) & 0xFF]);
                return (ushort)(crc ^ _xorOut);
            }
        }

        public static readonly Variant[] Variants =
        {
            new Variant("CRC-16/CCITT-FALSE", false, 0x0000), // poly 0x1021, seed 0xFFFF, MSB-first
            new Variant("CRC-16/MCRF4XX",     true,  0x0000),
            new Variant("CRC-16/GENIBUS",     false, 0xFFFF),
            new Variant("CRC-16/X-25",        true,  0xFFFF),
        };

        public static Variant Detect(byte[] data, int length, ushort stored)
        {
            foreach (var v in Variants)
                if (v.Compute(data, length) == stored) return v;
            return null;
        }
    }
}

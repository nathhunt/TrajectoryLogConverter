// HalcyonTrajectoryLogTool.cs
//
// Command-line tool for Halcyon / Ethos trajectory logs (.bin):
//
//   to-v4   Convert a version 5.1 log (HAL 5.0) to the version 4.0 layout (HAL 2.0 - 4.0 MR1)
//           so it can be read by tools that only understand 4.0 (e.g. DoseLab).
//   to-csv  Export a version 4.0 or 5.1 log to human-readable CSV:
//             <name>.csv          one row per 20 ms snapshot, expected + actual for every axis
//             <name>_header.csv   header fields, plan metadata and the subbeam table
//
// Based on "Halcyon and Ethos Radiotherapy System Trajectory Log File Specification"
//   P1069495-001-A (May 2025)  -> version 4.0 layout
//   P1069495-002-B (May 2026)  -> version 5.1 layout
//
// ---------------------------------------------------------------------------------------------
// Usage
//   HalcyonTrajectoryLogTool to-v4  <input.bin | folder> [-o <file | folder>] [options]
//   HalcyonTrajectoryLogTool to-csv <input.bin | folder> [-o <folder>]        [options]
//
// Common options
//   -o, --output <path>     to-v4 : output file, or folder. Default: "v4.0" subfolder next to
//                                   the input, ORIGINAL file name kept (DoseLab reads the
//                                   treatment date from the "_yyyyMMddHHmmss.bin" file name).
//                           to-csv: output folder. Default: same folder as the input.
//   --overwrite             Overwrite existing output files.
//   --ignore-crc            Process even if the input CRC does not verify.
//
// to-v4 options
//   --keep-machine-info     Keep machine specifier + serial number bytes in the header.
//   --axis-scale <1|2|3>    Header axis-scale value to write (axis values are NOT converted).
//   --time-csv              Save the removed v5.1 time axis to "<output>_time.csv".
//
// to-csv options
//   --no-mlc                Leave the 116 MLC column pairs out of the data CSV.
//   --actual-only           Only write actual values (no expected columns).
//
// Exit code: 0 = all files OK, 1 = at least one failure, 2 = bad arguments.
// ---------------------------------------------------------------------------------------------
// What to-v4 changes (per P1069495-002-B, "Introduction"):
//   1. Version string "5.1" -> "4.0".
//   2. Time axis (enum 43) removed from the axis enumeration, samples-per-axis arrays, axis
//      count, and from every snapshot.
//   3. Machine specifier (1 byte) + serial number (6 bytes), new in 5.1, are zeroed unless
//      --keep-machine-info.
//   4. Axis scale copied unchanged (5.1 writes 3, which is also a legal 4.0 value).
//   Header rebuilt, subbeams copied verbatim, CRC recomputed.
//
// CSV notes:
//   * Values are in the axis scale given in the header: cm for linear axes, degrees for
//     rotations, MU for dose. Axes that hold the "no value" marker (float max) in every
//     snapshot - the jaws, which Halcyon does not have - are left out and listed in the
//     header CSV; any single "no value" sample elsewhere is written as an empty cell.
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

            // Work out the list of input files
            var inputs = new List<string>();
            bool folderInput = Directory.Exists(opt.Input);
            if (folderInput)
                inputs.AddRange(Directory.GetFiles(opt.Input, "*.bin").OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            else if (File.Exists(opt.Input))
                inputs.Add(opt.Input);
            else
            {
                Console.Error.WriteLine("Input not found: " + opt.Input);
                return 1;
            }
            if (inputs.Count == 0) { Console.Error.WriteLine("No .bin files found in " + opt.Input); return 1; }

            int failures = 0;
            foreach (string input in inputs)
            {
                try
                {
                    List<string> notes = opt.Command == Command.ToV4
                        ? RunToV4(input, folderInput, opt)
                        : RunToCsv(input, opt);
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

        static List<string> RunToV4(string input, bool folderInput, Options opt)
        {
            string name = Path.GetFileName(input);
            string output;
            if (opt.Output == null)
                output = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(input)), "v4.0", name);
            else if (folderInput || Directory.Exists(opt.Output) || opt.Output.EndsWith(Path.DirectorySeparatorChar.ToString()))
                output = Path.Combine(opt.Output, name);
            else
                output = opt.Output;

            if (string.Equals(Path.GetFullPath(output), Path.GetFullPath(input), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Output would overwrite the input; choose a different folder.");
            if (File.Exists(output) && !opt.Overwrite)
                throw new IOException("Output exists (use --overwrite): " + output);

            var log = TrajectoryLog.Parse(File.ReadAllBytes(input), opt.IgnoreCrc);
            if (log.Version == "4.0") throw new SkipException("already version 4.0");

            var notes = new List<string>();
            byte[] converted = V4Converter.Convert(log, opt, notes);
            FileUtil.WriteAtomic(output, converted);

            if (opt.TimeCsv && log.TimeAxisIndex >= 0)
            {
                string csvPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output)),
                                              Path.GetFileNameWithoutExtension(output) + "_time.csv");
                var sb = new StringBuilder("Snapshot,SecondsSinceMidnight,Clock\r\n");
                for (int s = 0; s < log.NumSnapshots; s++)
                {
                    double t = log.Expected(s, log.TimeAxisIndex, 0);
                    sb.Append(s + 1).Append(',').Append(Fmt.Fixed(t, 3)).Append(',').Append(Fmt.Clock(t)).Append("\r\n");
                }
                FileUtil.WriteAtomic(csvPath, Encoding.UTF8.GetBytes(sb.ToString()));
                notes.Add("Time axis saved to " + csvPath);
            }

            notes.Insert(0, "OK   " + name + " -> " + output);
            return notes.Select((n, i) => i == 0 ? n : "     " + n).ToList();
        }

        static List<string> RunToCsv(string input, Options opt)
        {
            string outDir = opt.Output ?? Path.GetDirectoryName(Path.GetFullPath(input));
            Directory.CreateDirectory(outDir);
            string baseName = Path.GetFileNameWithoutExtension(input);
            string dataPath = Path.Combine(outDir, baseName + ".csv");
            string headerPath = Path.Combine(outDir, baseName + "_header.csv");
            if (!opt.Overwrite && (File.Exists(dataPath) || File.Exists(headerPath)))
                throw new IOException("Output exists (use --overwrite): " + dataPath);

            var log = TrajectoryLog.Parse(File.ReadAllBytes(input), opt.IgnoreCrc);
            FileUtil.WriteAtomic(headerPath, Encoding.UTF8.GetBytes(CsvExporter.HeaderCsv(log, input)));
            FileUtil.WriteAtomic(dataPath, Encoding.UTF8.GetBytes(CsvExporter.DataCsv(log, opt)));

            var notes = new List<string>
            {
                "OK   " + Path.GetFileName(input) + " (v" + log.Version + ") -> " + dataPath,
                "     " + string.Format(CultureInfo.InvariantCulture, "{0} snapshots, {1} axes, {2} subbeam(s); header -> {3}",
                                        log.NumSnapshots, log.NumAxes, log.Subbeams.Count, headerPath)
            };
            if (log.CrcWarning != null) notes.Add("     WARNING: " + log.CrcWarning);
            return notes;
        }
    }

    public enum Command { ToV4, ToCsv }

    public sealed class SkipException : Exception
    {
        public SkipException(string message) : base(message) { }
    }

    public sealed class Options
    {
        public Command Command;
        public string Input;
        public string Output;
        public bool Overwrite;
        public bool IgnoreCrc;
        // to-v4
        public bool KeepMachineInfo;
        public int? AxisScale;
        public bool TimeCsv;
        // to-csv
        public bool NoMlc;
        public bool ActualOnly;

        public static Options Parse(string[] args)
        {
            if (args.Length == 0) return null;
            var o = new Options();
            string cmd = args[0].ToLowerInvariant();
            if (cmd == "-h" || cmd == "--help" || cmd == "/?") return null;
            if (cmd == "to-v4") o.Command = Command.ToV4;
            else if (cmd == "to-csv") o.Command = Command.ToCsv;
            else throw new ArgumentException("First argument must be a command: to-v4 or to-csv");

            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "-h": case "--help": case "/?": return null;
                    case "-o": case "--output": o.Output = Next(args, ref i, a); break;
                    case "--overwrite": o.Overwrite = true; break;
                    case "--ignore-crc": o.IgnoreCrc = true; break;
                    case "--keep-machine-info": RequireCmd(o, Command.ToV4, a); o.KeepMachineInfo = true; break;
                    case "--time-csv": RequireCmd(o, Command.ToV4, a); o.TimeCsv = true; break;
                    case "--axis-scale":
                        RequireCmd(o, Command.ToV4, a);
                        int s;
                        if (!int.TryParse(Next(args, ref i, a), out s) || s < 1 || s > 3)
                            throw new ArgumentException("--axis-scale must be 1, 2 or 3");
                        o.AxisScale = s; break;
                    case "--no-mlc": RequireCmd(o, Command.ToCsv, a); o.NoMlc = true; break;
                    case "--actual-only": RequireCmd(o, Command.ToCsv, a); o.ActualOnly = true; break;
                    default:
                        if (a.StartsWith("-")) throw new ArgumentException("Unknown option " + a);
                        if (o.Input != null) throw new ArgumentException("Only one input path is allowed");
                        o.Input = a; break;
                }
            }
            if (o.Input == null) throw new ArgumentException("No input given");
            return o;
        }

        static void RequireCmd(Options o, Command c, string name)
        {
            if (o.Command != c)
                throw new ArgumentException(name + " only applies to " + (c == Command.ToV4 ? "to-v4" : "to-csv"));
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
            Console.WriteLine("      Export v4.0 / v5.1 logs to <name>.csv (data) and <name>_header.csv.");
            Console.WriteLine("      --no-mlc              leave out MLC columns");
            Console.WriteLine("      --actual-only         write actual values only");
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
    // CSV export
    // =============================================================================================
    public static class CsvExporter
    {
        const float NoValue = 3.0e38f; // log uses float.MaxValue for axes that don't exist (jaws)

        public static string HeaderCsv(TrajectoryLog log, string sourcePath)
        {
            var sb = new StringBuilder();
            Action<string, string> kv = (k, v) => sb.Append(Csv.Field(k)).Append(',').Append(Csv.Field(v)).Append("\r\n");

            kv("Field", "Value");
            kv("Source file", Path.GetFileName(sourcePath));
            kv("Log version", log.Version);
            kv("Sampling interval (ms)", log.SamplingIntervalMs.ToString(CultureInfo.InvariantCulture));
            kv("Number of axes", log.NumAxes.ToString(CultureInfo.InvariantCulture));
            kv("Axes (id:name x samples)", string.Join("; ", Enumerable.Range(0, log.NumAxes)
                .Select(i => log.AxisIds[i] + ":" + TrajectoryLog.AxisName(log.AxisIds[i]) + " x" + log.SamplesPerAxis[i])));
            kv("Axis scale", log.AxisScale + " - " + TrajectoryLog.AxisScaleName(log.AxisScale));
            kv("Number of subbeams", log.NumSubbeamsField.ToString(CultureInfo.InvariantCulture));
            kv("Truncated", log.Truncated == 1 ? "Yes (1)" : "No (" + log.Truncated + ")");
            kv("Number of snapshots", log.NumSnapshots.ToString(CultureInfo.InvariantCulture));
            kv("Duration recorded (s)", Fmt.Fixed(log.NumSnapshots * log.SamplingIntervalMs / 1000.0, 2));
            kv("MLC model", log.MlcModel + (log.MlcModel == 6 ? " - SX2" : ""));
            if (log.HasMachineInfo)
            {
                kv("Machine specifier", log.MachineSpecifier + (log.MachineSpecifier == 0 ? " - TrueBeam"
                                        : log.MachineSpecifier == 1 ? " - Halcyon/Ethos" : ""));
                kv("Machine serial number (last 6)", log.MachineSerial);
            }
            if (log.TimeAxisIndex >= 0 && log.NumSnapshots > 0)
            {
                kv("First snapshot clock time", Fmt.Clock(log.Expected(0, log.TimeAxisIndex, 0)));
                kv("Last snapshot clock time", Fmt.Clock(log.Expected(log.NumSnapshots - 1, log.TimeAxisIndex, 0)));
            }
            bool[] hasData = AxesWithData(log);
            string empty = string.Join("; ", Enumerable.Range(0, log.NumAxes).Where(i => !hasData[i])
                                                   .Select(i => TrajectoryLog.AxisName(log.AxisIds[i])));
            if (empty.Length > 0) kv("Axes with no data (left out of data CSV)", empty);
            kv("CRC", log.CrcWarning ?? ("OK (" + log.CrcVariant.Name + ")"));

            // Plan metadata ("Key:\tValue" lines)
            sb.Append("\r\n");
            kv("Metadata", "");
            foreach (string line in log.MetaDataText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                int c = line.IndexOf(':');
                if (c > 0) kv(line.Substring(0, c).Trim(), line.Substring(c + 1).Trim());
                else kv(line.Trim(), "");
            }

            // Subbeams
            sb.Append("\r\n");
            sb.Append("Subbeam,Name,StartControlPoint,MU,ExpectedRadTime_s,Seq\r\n");
            for (int k = 0; k < log.Subbeams.Count; k++)
            {
                var s = log.Subbeams[k];
                sb.Append(k + 1).Append(',').Append(Csv.Field(s.Name)).Append(',')
                  .Append(s.ControlPoint).Append(',').Append(Fmt.Num(s.MU)).Append(',')
                  .Append(Fmt.Num(s.RadTime)).Append(',').Append(s.Seq).Append("\r\n");
            }
            return sb.ToString();
        }

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
        /// those are left out of the data CSV and listed in the header CSV.
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
        static string MlcColumn(int k, int total)
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

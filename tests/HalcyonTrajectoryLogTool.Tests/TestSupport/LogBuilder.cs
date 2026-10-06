using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

// Program.Main and the CLI tests redirect Console.Out / Console.Error, which is process-wide.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace HalcyonTrajectoryLogTool.Tests
{
    /// <summary>
    /// Builds synthetic trajectory logs byte by byte, following the layout in the Halcyon / Ethos
    /// trajectory log specification (P1069495-001-A for v4.0, P1069495-002-B for v5.1), independently
    /// of the parser under test.
    /// </summary>
    public sealed class LogBuilder
    {
        /// <summary>HAL 5.0 (v5.1) axis order, as written by machines.</summary>
        public static readonly int[] V51Axes = { 43, 42, 40, 41, 1, 0, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 50 };

        /// <summary>v4.0 axis order (HAL 2.0 - 4.0 MR1).</summary>
        public static readonly int[] V40Axes = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 40, 41, 42, 50 };

        public const int MlcSamples = 116; // 2 banks + 114 leaves
        public const float TimeStart = 50000f; // 13:53:20

        public string Signature = "VOSTL";
        public string Version = "5.1";
        public bool Wide;
        public int HeaderSizeField = 1024;
        public int SamplingIntervalMs = 20;
        public List<int> AxisIds = new List<int>();
        public List<int> Samples = new List<int>();
        public int AxisScale = 3;
        public int Truncated;
        public int NumSnapshots = 5;
        public int? NumSnapshotsField;       // override the header field (to make the length wrong)
        public int MlcModel = 2;
        public string Metadata = "Patient ID:\tPAT001\r\nPlan Name:\tPlan1\r\n";
        public byte MachineSpecifier = 1;
        public string Serial = "H12345";
        public List<Subbeam> Subbeams = new List<Subbeam>();
        public Crc16.Variant Crc = Crc16.Variants[0];
        public bool CorruptCrc;

        /// <summary>(snapshot, axisId, sample, actual?) -> value.</summary>
        public Func<int, int, int, bool, float> Value = DefaultValue;

        public static float DefaultValue(int s, int axisId, int k, bool actual)
        {
            if (axisId == 43) return actual ? 0f : TimeStart + s * 0.02f;
            if (axisId >= 2 && axisId <= 5) return float.MaxValue; // Halcyon has no jaws
            if (axisId == 41) return actual ? (s % 4) : 0f;        // BeamHold / dose servo state
            if (axisId == 42) return s / 2;                         // control point
            return axisId * 100 + s + k / 1000f + (actual ? 0.5f : 0f);
        }

        public static LogBuilder V51(int snapshots = 5)
        {
            var b = new LogBuilder { Version = "5.1", NumSnapshots = snapshots };
            b.SetAxes(V51Axes);
            b.Subbeams.Add(new Subbeam { ControlPoint = 0, MU = 100f, RadTime = 30f, Seq = 0, Name = "Arc 1" });
            b.Subbeams.Add(new Subbeam { ControlPoint = 2, MU = 50.5f, RadTime = 12.25f, Seq = 1, Name = "Arc 2" });
            return b;
        }

        public static LogBuilder V40(int snapshots = 5)
        {
            var b = new LogBuilder { Version = "4.0", NumSnapshots = snapshots, MachineSpecifier = 0, Serial = "" };
            b.SetAxes(V40Axes);
            b.Subbeams.Add(new Subbeam { ControlPoint = 0, MU = 100f, RadTime = 30f, Seq = 0, Name = "Arc 1" });
            b.Subbeams.Add(new Subbeam { ControlPoint = 2, MU = 50.5f, RadTime = 12.25f, Seq = 1, Name = "Arc 2" });
            return b;
        }

        /// <summary>Sets the axes; MLC (50) gets 116 samples, everything else 1.</summary>
        public LogBuilder SetAxes(params int[] ids)
        {
            AxisIds = ids.ToList();
            Samples = ids.Select(id => id == 50 ? MlcSamples : 1).ToList();
            return this;
        }

        public LogBuilder SetSamples(int axisId, int samples)
        {
            Samples[AxisIds.IndexOf(axisId)] = samples;
            return this;
        }

        public LogBuilder RemoveAxes(params int[] ids)
        {
            foreach (int id in ids)
            {
                int i = AxisIds.IndexOf(id);
                if (i < 0) continue;
                AxisIds.RemoveAt(i);
                Samples.RemoveAt(i);
            }
            return this;
        }

        public byte[] Build()
        {
            int floatsPerSnapshot = Samples.Sum() * 2;
            int length = 1024 + Subbeams.Count * 560 + NumSnapshots * floatsPerSnapshot * 4 + 2;
            var b = new byte[length];

            WriteText(b, 0, 16, Signature);
            WriteText(b, 16, 16, Version);
            int p = 32;
            Int(b, ref p, HeaderSizeField);
            Int(b, ref p, SamplingIntervalMs);
            Int(b, ref p, AxisIds.Count);
            foreach (int id in AxisIds) Int(b, ref p, id);
            foreach (int n in Samples) Int(b, ref p, n);
            Int(b, ref p, AxisScale);
            Int(b, ref p, Subbeams.Count);
            Int(b, ref p, Truncated);
            Int(b, ref p, NumSnapshotsField ?? NumSnapshots);
            Int(b, ref p, MlcModel);
            byte[] meta = Encoding.UTF8.GetBytes(Metadata);
            Buffer.BlockCopy(meta, 0, b, p, Math.Min(meta.Length, 744));
            p += 745;
            b[p] = MachineSpecifier;
            byte[] serial = Encoding.ASCII.GetBytes(Serial);
            Buffer.BlockCopy(serial, 0, b, p + 1, Math.Min(6, serial.Length));

            p = 1024;
            foreach (Subbeam sb in Subbeams)
            {
                int q = p;
                Int(b, ref q, sb.ControlPoint);
                Float(b, ref q, sb.MU);
                Float(b, ref q, sb.RadTime);
                Int(b, ref q, sb.Seq);
                WriteText(b, q, 512, sb.Name);
                p += 560;
            }

            for (int s = 0; s < NumSnapshots; s++)
                for (int a = 0; a < AxisIds.Count; a++)
                    for (int k = 0; k < Samples[a]; k++)
                    {
                        Float(b, ref p, Value(s, AxisIds[a], k, false));
                        Float(b, ref p, Value(s, AxisIds[a], k, true));
                    }

            ushort crc = Crc.Compute(b, length - 2);
            if (CorruptCrc) crc ^= 0x5A5A;
            b[length - 2] = (byte)crc;
            b[length - 1] = (byte)(crc >> 8);
            return b;
        }

        void WriteText(byte[] b, int offset, int max, string s)
        {
            byte[] bytes = Wide ? Encoding.Unicode.GetBytes(s) : Encoding.UTF8.GetBytes(s);
            Buffer.BlockCopy(bytes, 0, b, offset, Math.Min(bytes.Length, max - 2));
        }

        static void Int(byte[] b, ref int p, int v)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(v), 0, b, p, 4);
            p += 4;
        }

        static void Float(byte[] b, ref int p, float v)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(v), 0, b, p, 4);
            p += 4;
        }

        // ---- helpers for reading converter output ----

        public static int ReadIntAt(byte[] b, int offset) { return BitConverter.ToInt32(b, offset); }

        /// <summary>Header offset of the metadata block for a log with <paramref name="numAxes"/> axes.</summary>
        public static int MetaOffset(int numAxes) { return 32 + 12 + numAxes * 8 + 20; }

        public static TrajectoryLog Parse(byte[] bytes) { return TrajectoryLog.Parse(bytes, false); }
    }
}

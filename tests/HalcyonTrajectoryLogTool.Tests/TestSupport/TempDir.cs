using System;
using System.IO;

namespace HalcyonTrajectoryLogTool.Tests
{
    /// <summary>A fresh temporary folder, deleted on Dispose.</summary>
    public sealed class TempDir : IDisposable
    {
        public readonly string Path;

        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tlc-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string File(string name) { return System.IO.Path.Combine(Path, name); }

        public string Write(string name, byte[] data)
        {
            string p = File(name);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p));
            System.IO.File.WriteAllBytes(p, data);
            return p;
        }

        public string Sub(string name)
        {
            string p = File(name);
            Directory.CreateDirectory(p);
            return p;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Captures Console.Out and Console.Error while alive.</summary>
    public sealed class ConsoleCapture : IDisposable
    {
        readonly TextWriter _out, _err;
        readonly StringWriter _capturedOut = new StringWriter(), _capturedErr = new StringWriter();

        public ConsoleCapture()
        {
            _out = Console.Out; _err = Console.Error;
            Console.SetOut(_capturedOut);
            Console.SetError(_capturedErr);
        }

        public string Out { get { return _capturedOut.ToString(); } }
        public string Err { get { return _capturedErr.ToString(); } }

        public void Dispose()
        {
            Console.SetOut(_out);
            Console.SetError(_err);
        }
    }
}

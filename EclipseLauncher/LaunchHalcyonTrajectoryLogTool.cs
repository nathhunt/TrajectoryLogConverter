// LaunchHalcyonTrajectoryLogTool.cs
//
// Single-file ESAPI plug-in script that starts the Halcyon / Ethos trajectory log converter
// (HalcyonTrajectoryLogGui.exe) from Eclipse: Tools > Scripts > LaunchHalcyonTrajectoryLogTool.cs.
//
// Setup: set ExePath below to where HalcyonTrajectoryLogGui.exe is deployed (a local path or a
// UNC share), and optionally the default input / export folders shown when the converter opens.
// The converter runs as its own process, so Eclipse stays usable while logs are converted and
// closing Eclipse does not stop a running conversion.
//
// The script reads no patient data, so it runs with or without a patient open and does not
// need to be approved as a writeable script.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using VMS.TPS.Common.Model.API;

namespace VMS.TPS
{
    public class Script
    {
        // ---- Edit these for your clinic ------------------------------------------------------
        // Full path of the converter executable.
        const string ExePath = @"HalcyonTrajectoryLogGui.exe";
        // Folder the converter opens with as input (e.g. where logs are copied from the machine).
        // Leave empty to start with a blank input.
        const string DefaultInputFolder = @"";
        // Folder the converted files are exported to by default. Leave empty to start blank.
        const string DefaultExportFolder = @"";
        // Initial conversion: "v4" (v5.1 -> v4.0 .bin), "csv" (CSV export) or "both".
        const string DefaultMode = "v4";
        // ---------------------------------------------------------------------------------------

        public Script() { }

        public void Execute(ScriptContext context)
        {
            if (!File.Exists(ExePath))
            {
                MessageBox.Show("The trajectory log converter was not found at:\n\n" + ExePath +
                                "\n\nEdit ExePath at the top of LaunchHalcyonTrajectoryLogTool.cs to point to " +
                                "HalcyonTrajectoryLogGui.exe.",
                                "Trajectory Log Converter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var args = new StringBuilder();
            if (DefaultInputFolder.Length > 0) args.Append("--input ").Append(Quote(DefaultInputFolder)).Append(' ');
            if (DefaultExportFolder.Length > 0) args.Append("--output ").Append(Quote(DefaultExportFolder)).Append(' ');
            if (DefaultMode.Length > 0) args.Append("--mode ").Append(DefaultMode);

            try
            {
                var psi = new ProcessStartInfo(ExePath, args.ToString().Trim())
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(ExePath)
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not start the trajectory log converter:\n\n" + ex.Message,
                                "Trajectory Log Converter", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Quote a path for the command line. A trailing backslash would escape the closing quote
        // ("C:\Logs\" -> C:\Logs"), so it is dropped unless the path is a drive root.
        static string Quote(string path)
        {
            string p = path.Trim();
            if (p.EndsWith("\\") && !p.EndsWith(":\\")) p = p.TrimEnd('\\');
            else if (p.EndsWith(":\\")) p += "\\";   // "C:\" -> "C:\\" so the quote survives
            return "\"" + p + "\"";
        }
    }
}

// HalcyonTrajectoryLogGui.cs
//
// Windows Forms front end for HalcyonTrajectoryLogTool.cs, built as an ESAPI-style stand-alone
// executable (x64, .NET Framework 4.x) so it can be deployed next to other ESAPI tools and
// launched from Eclipse by LaunchHalcyonTrajectoryLogTool.cs.
//
//   * Input:  a single .bin trajectory log, or a folder of logs (optionally with subfolders),
//             optionally limited to logs treated within a date range.
//   * Output: v5.1 -> v4.0 converted .bin files and/or CSV exports, written to a chosen folder.
//             When subfolders are included, their structure is mirrored in the output folder.
//
// Treatment date of a log:
//   Halcyon / Ethos logs are named "..._yyyyMMddHHmmss.bin"; that time stamp is used for the date
//   filter. Files without one fall back to the file's last-modified date (shown in "Find files").
//
// The conversion itself is done by Program.RunToV4 / Program.RunToCsv in HalcyonTrajectoryLogTool.cs,
// so the GUI and the command-line tool always produce identical output.
//
// Command-line arguments (all optional, used by the Eclipse launcher to pre-fill the form):
//   --input <file | folder>   --output <folder>   --mode <v4 | csv | both>
//
// No ESAPI session is opened: the tool only reads and writes log files, so it does not need an
// Eclipse login, license or script approval. Build with TrajectoryLogConverter.sln (Visual Studio or VS Code).

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace HalcyonTrajectoryLogTool.Gui
{
    public static class GuiProgram
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args));
        }
    }

    /// <summary>A log file found in the input, with the date used for the date-range filter.</summary>
    public sealed class LogFile
    {
        public string FullPath;
        public string RelativeDir;   // folder relative to the input folder ("" = top level)
        public DateTime Date;
        public bool DateFromName;    // true = from "_yyyyMMddHHmmss" in the name, false = file date
    }

    /// <summary>Snapshot of the form, taken on the UI thread and handed to the worker.</summary>
    public sealed class RunSettings
    {
        public bool FolderMode;
        public string Input;
        public bool IncludeSubfolders;
        public bool UseDateRange;
        public DateTime From;        // inclusive, start of day
        public DateTime To;          // inclusive, end of day
        public string Output;
        public bool ToV4;
        public bool ToCsv;
        public bool Overwrite;
        public bool IgnoreCrc;
        public bool KeepMachineInfo;
        public bool TimeCsv;
        public bool NoMlc;
        public bool ActualOnly;
    }

    public static class LogFinder
    {
        static readonly Regex StampRx = new Regex(@"(?<!\d)\d{14}(?!\d)");

        /// <summary>Treatment date/time from a "yyyyMMddHHmmss" stamp in the file name (last one wins).</summary>
        public static bool TryDateFromName(string path, out DateTime date)
        {
            MatchCollection ms = StampRx.Matches(Path.GetFileNameWithoutExtension(path));
            for (int i = ms.Count - 1; i >= 0; i--)
                if (DateTime.TryParseExact(ms[i].Value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                                           DateTimeStyles.None, out date))
                    return true;
            date = DateTime.MinValue;
            return false;
        }

        public static LogFile Describe(string path, string rootFolder)
        {
            var f = new LogFile { FullPath = Path.GetFullPath(path) };
            f.DateFromName = TryDateFromName(path, out f.Date);
            if (!f.DateFromName) f.Date = File.GetLastWriteTime(path);
            string dir = Path.GetDirectoryName(f.FullPath);
            f.RelativeDir = rootFolder == null ? "" : RelativePath(rootFolder, dir);
            return f;
        }

        public static List<LogFile> Find(RunSettings s)
        {
            var result = new List<LogFile>();
            if (!s.FolderMode)
            {
                result.Add(Describe(s.Input, null));
                return result;
            }

            string root = Path.GetFullPath(s.Input);
            string outRoot = string.IsNullOrEmpty(s.Output) ? null : Path.GetFullPath(s.Output);
            var option = s.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (string path in Directory.GetFiles(root, "*.bin", option))
            {
                // Don't pick up our own earlier output when it sits inside the input folder.
                if (outRoot != null && !PathEquals(outRoot, root) && IsUnder(path, outRoot)) continue;
                LogFile f = Describe(path, root);
                if (s.UseDateRange && (f.Date < s.From || f.Date > s.To)) continue;
                result.Add(f);
            }
            return result.OrderBy(f => f.RelativeDir, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(f => f.Date)
                         .ThenBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase)
                         .ToList();
        }

        public static string Normalize(string path)
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public static bool PathEquals(string a, string b)
        {
            return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsUnder(string path, string folder)
        {
            return Path.GetFullPath(path).StartsWith(Normalize(folder) + Path.DirectorySeparatorChar,
                                                     StringComparison.OrdinalIgnoreCase);
        }

        static string RelativePath(string root, string dir)
        {
            string r = Normalize(root), d = Normalize(dir);
            if (string.Equals(r, d, StringComparison.OrdinalIgnoreCase)) return "";
            return d.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? d.Substring(r.Length + 1) : "";
        }
    }

    public sealed class MainForm : Form
    {
        // Input
        RadioButton rbFile, rbFolder;
        TextBox txtInput;
        Button btnBrowseInput;
        CheckBox chkSubfolders, chkDateRange;
        DateTimePicker dtFrom, dtTo;
        // Output
        TextBox txtOutput;
        Button btnBrowseOutput;
        CheckBox chkV4, chkCsv, chkOverwrite, chkIgnoreCrc, chkKeepMachine, chkTimeCsv, chkNoMlc, chkActualOnly;
        // Run
        Button btnFind, btnRun, btnCancel, btnOpenOutput;
        ProgressBar progress;
        Label lblStatus;
        TextBox txtLog;
        BackgroundWorker worker;

        public MainForm(string[] args)
        {
            Text = "Halcyon / Ethos Trajectory Log Converter";
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(780, 720);
            MinimumSize = new Size(700, 640);
            StartPosition = FormStartPosition.CenterScreen;

            BuildLayout();
            ApplyArgs(args);
            UpdateEnabled();

            worker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };
            worker.DoWork += Worker_DoWork;
            worker.ProgressChanged += Worker_ProgressChanged;
            worker.RunWorkerCompleted += Worker_Completed;
        }

        // =========================================================================================
        // Layout
        // =========================================================================================
        void BuildLayout()
        {
            const int m = 12;
            int w = ClientSize.Width - 2 * m;
            var anchorTLR = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            // ---- Input ----
            var grpIn = new GroupBox { Text = "Input", Location = new Point(m, m), Size = new Size(w, 145), Anchor = anchorTLR };
            rbFile = new RadioButton { Text = "Single log file", Location = new Point(12, 22), AutoSize = true };
            rbFolder = new RadioButton { Text = "Folder of log files", Location = new Point(150, 22), AutoSize = true, Checked = true };
            rbFile.CheckedChanged += (s, e) => UpdateEnabled();
            rbFolder.CheckedChanged += (s, e) => UpdateEnabled();

            txtInput = new TextBox { Location = new Point(12, 50), Size = new Size(w - 120, 23), Anchor = anchorTLR };
            btnBrowseInput = new Button { Text = "Browse...", Location = new Point(w - 100, 49), Size = new Size(88, 25), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnBrowseInput.Click += BrowseInput_Click;

            chkSubfolders = new CheckBox { Text = "Include subfolders", Location = new Point(12, 82), AutoSize = true };

            chkDateRange = new CheckBox { Text = "Only logs treated from", Location = new Point(12, 110), AutoSize = true };
            chkDateRange.CheckedChanged += (s, e) => UpdateEnabled();
            dtFrom = new DateTimePicker { Location = new Point(175, 107), Width = 130, Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Value = DateTime.Today.AddDays(-7) };
            var lblTo = new Label { Text = "to", Location = new Point(313, 111), AutoSize = true };
            dtTo = new DateTimePicker { Location = new Point(335, 107), Width = 130, Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Value = DateTime.Today };
            grpIn.Controls.AddRange(new Control[] { rbFile, rbFolder, txtInput, btnBrowseInput, chkSubfolders, chkDateRange, dtFrom, lblTo, dtTo });

            // ---- Output ----
            var grpOut = new GroupBox { Text = "Output", Location = new Point(m, grpIn.Bottom + 8), Size = new Size(w, 190), Anchor = anchorTLR };
            var lblOut = new Label { Text = "Export to folder:", Location = new Point(12, 24), AutoSize = true };
            txtOutput = new TextBox { Location = new Point(12, 44), Size = new Size(w - 120, 23), Anchor = anchorTLR };
            btnBrowseOutput = new Button { Text = "Browse...", Location = new Point(w - 100, 43), Size = new Size(88, 25), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnBrowseOutput.Click += BrowseOutput_Click;

            chkV4 = new CheckBox { Text = "Convert v5.1 to v4.0 (.bin, same file name)", Location = new Point(12, 78), AutoSize = true, Checked = true };
            chkCsv = new CheckBox { Text = "Export CSV (<name>.csv)", Location = new Point(380, 78), AutoSize = true };
            chkV4.CheckedChanged += (s, e) => UpdateEnabled();
            chkCsv.CheckedChanged += (s, e) => UpdateEnabled();

            chkKeepMachine = new CheckBox { Text = "Keep machine specifier / serial", Location = new Point(30, 104), AutoSize = true };
            chkTimeCsv = new CheckBox { Text = "Save removed time axis (_time.csv)", Location = new Point(30, 128), AutoSize = true };
            chkNoMlc = new CheckBox { Text = "Leave out MLC columns", Location = new Point(398, 104), AutoSize = true };
            chkActualOnly = new CheckBox { Text = "Actual values only", Location = new Point(398, 128), AutoSize = true };

            chkOverwrite = new CheckBox { Text = "Overwrite existing output files", Location = new Point(12, 158), AutoSize = true };
            chkIgnoreCrc = new CheckBox { Text = "Process logs whose CRC does not verify", Location = new Point(380, 158), AutoSize = true };
            grpOut.Controls.AddRange(new Control[] { lblOut, txtOutput, btnBrowseOutput, chkV4, chkCsv, chkKeepMachine, chkTimeCsv, chkNoMlc, chkActualOnly, chkOverwrite, chkIgnoreCrc });

            // ---- Buttons ----
            int by = grpOut.Bottom + 10;
            btnFind = new Button { Text = "Find files", Location = new Point(m, by), Size = new Size(100, 30) };
            btnRun = new Button { Text = "Convert", Location = new Point(m + 108, by), Size = new Size(100, 30) };
            btnCancel = new Button { Text = "Cancel", Location = new Point(m + 216, by), Size = new Size(100, 30), Enabled = false };
            btnOpenOutput = new Button { Text = "Open output folder", Location = new Point(ClientSize.Width - m - 140, by), Size = new Size(140, 30), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnFind.Click += Find_Click;
            btnRun.Click += Run_Click;
            btnCancel.Click += (s, e) => { if (worker.IsBusy) { worker.CancelAsync(); btnCancel.Enabled = false; } };
            btnOpenOutput.Click += OpenOutput_Click;

            progress = new ProgressBar { Location = new Point(m, by + 40), Size = new Size(w, 18), Anchor = anchorTLR };
            lblStatus = new Label { Text = "Ready.", Location = new Point(m, by + 62), Size = new Size(w, 20), Anchor = anchorTLR };

            txtLog = new TextBox
            {
                Location = new Point(m, by + 84), Size = new Size(w, ClientSize.Height - (by + 84) - m),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
                Font = new Font("Consolas", 9f), BackColor = SystemColors.Window
            };

            Controls.AddRange(new Control[] { grpIn, grpOut, btnFind, btnRun, btnCancel, btnOpenOutput, progress, lblStatus, txtLog });
            AcceptButton = btnRun;
        }

        void ApplyArgs(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                string v = i + 1 < args.Length ? args[i + 1] : null;
                if ((a == "--input" || a == "-i") && v != null) { txtInput.Text = v; i++; }
                else if ((a == "--output" || a == "-o") && v != null) { txtOutput.Text = v; i++; }
                else if (a == "--mode" && v != null)
                {
                    string mode = v.ToLowerInvariant();
                    chkV4.Checked = mode == "v4" || mode == "both";
                    chkCsv.Checked = mode == "csv" || mode == "both";
                    i++;
                }
                else if (!a.StartsWith("-") && txtInput.Text.Length == 0) txtInput.Text = args[i];
            }
            if (File.Exists(txtInput.Text)) rbFile.Checked = true;
        }

        void UpdateEnabled()
        {
            bool idle = worker == null || !worker.IsBusy;
            bool folder = rbFolder.Checked;
            foreach (Control c in new Control[] { rbFile, rbFolder, txtInput, btnBrowseInput, txtOutput, btnBrowseOutput,
                                                  chkV4, chkCsv, chkOverwrite, chkIgnoreCrc, btnFind, btnRun })
                c.Enabled = idle;
            chkSubfolders.Enabled = idle && folder;
            chkDateRange.Enabled = idle && folder;
            dtFrom.Enabled = dtTo.Enabled = idle && folder && chkDateRange.Checked;
            chkKeepMachine.Enabled = chkTimeCsv.Enabled = idle && chkV4.Checked;
            chkNoMlc.Enabled = chkActualOnly.Enabled = idle && chkCsv.Checked;
            btnCancel.Enabled = !idle;
        }

        // =========================================================================================
        // Browse buttons
        // =========================================================================================
        void BrowseInput_Click(object sender, EventArgs e)
        {
            if (rbFile.Checked)
            {
                using (var dlg = new OpenFileDialog { Filter = "Trajectory logs (*.bin)|*.bin|All files (*.*)|*.*", Title = "Select a trajectory log" })
                {
                    string start = StartFolder(txtInput.Text);
                    if (start != null) dlg.InitialDirectory = start;
                    if (dlg.ShowDialog(this) == DialogResult.OK) txtInput.Text = dlg.FileName;
                }
            }
            else
            {
                string path = PickFolder("Select the folder containing the trajectory logs", txtInput.Text);
                if (path != null) txtInput.Text = path;
            }
        }

        void BrowseOutput_Click(object sender, EventArgs e)
        {
            string path = PickFolder("Select the folder to export converted files to", txtOutput.Text);
            if (path != null) txtOutput.Text = path;
        }

        string PickFolder(string description, string current)
        {
            using (var dlg = new FolderBrowserDialog { Description = description, ShowNewFolderButton = true })
            {
                string start = StartFolder(current);
                if (start != null) dlg.SelectedPath = start;
                return dlg.ShowDialog(this) == DialogResult.OK ? dlg.SelectedPath : null;
            }
        }

        static string StartFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                if (Directory.Exists(path)) return path;
                string dir = Path.GetDirectoryName(path);
                return !string.IsNullOrEmpty(dir) && Directory.Exists(dir) ? dir : null;
            }
            catch (ArgumentException) { return null; }
        }

        void OpenOutput_Click(object sender, EventArgs e)
        {
            string path = txtOutput.Text.Trim();
            if (Directory.Exists(path)) Process.Start("explorer.exe", "\"" + path + "\"");
            else MessageBox.Show(this, "The output folder does not exist yet.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // =========================================================================================
        // Validation / file discovery
        // =========================================================================================
        RunSettings ReadSettings(bool forRun)
        {
            var s = new RunSettings
            {
                FolderMode = rbFolder.Checked,
                Input = txtInput.Text.Trim().Trim('"'),
                IncludeSubfolders = chkSubfolders.Checked,
                UseDateRange = rbFolder.Checked && chkDateRange.Checked,
                From = dtFrom.Value.Date,
                To = dtTo.Value.Date.AddDays(1).AddTicks(-1),
                Output = txtOutput.Text.Trim().Trim('"'),
                ToV4 = chkV4.Checked,
                ToCsv = chkCsv.Checked,
                Overwrite = chkOverwrite.Checked,
                IgnoreCrc = chkIgnoreCrc.Checked,
                KeepMachineInfo = chkKeepMachine.Checked,
                TimeCsv = chkTimeCsv.Checked,
                NoMlc = chkNoMlc.Checked,
                ActualOnly = chkActualOnly.Checked
            };

            string error = null;
            if (s.Input.Length == 0) error = s.FolderMode ? "Select an input folder." : "Select a log file.";
            else if (s.FolderMode && !Directory.Exists(s.Input)) error = "Input folder not found:\n" + s.Input;
            else if (!s.FolderMode && !File.Exists(s.Input)) error = "Log file not found:\n" + s.Input;
            else if (s.UseDateRange && s.From > s.To) error = "The start date is after the end date.";
            else if (forRun)
            {
                if (!s.ToV4 && !s.ToCsv) error = "Tick at least one of 'Convert to v4.0' or 'Export CSV'.";
                else if (s.Output.Length == 0) error = "Select a folder to export to.";
                else if (s.ToV4 && LogFinder.PathEquals(s.Output, s.FolderMode ? s.Input : Path.GetDirectoryName(Path.GetFullPath(s.Input))))
                    error = "The export folder is the same as the input folder, so the v4.0 files would overwrite the originals.\nChoose a different export folder.";
            }
            if (error != null)
            {
                MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
            return s;
        }

        void Find_Click(object sender, EventArgs e)
        {
            RunSettings s = ReadSettings(false);
            if (s == null) return;
            List<LogFile> files;
            try { files = LogFinder.Find(s); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

            txtLog.Clear();
            AppendLog(Describe(s, files.Count));
            foreach (LogFile f in files)
                AppendLog(string.Format(CultureInfo.InvariantCulture, "  {0:dd/MM/yyyy HH:mm:ss}{1}  {2}",
                                        f.Date, f.DateFromName ? "  " : " *", Path.Combine(f.RelativeDir, Path.GetFileName(f.FullPath))));
            if (files.Any(f => !f.DateFromName))
                AppendLog("  * no time stamp in the file name; file modified date used");
            lblStatus.Text = files.Count + " log file(s) found.";
        }

        static string Describe(RunSettings s, int count)
        {
            var sb = new StringBuilder();
            sb.Append(count).Append(" log file(s) in ").Append(s.Input);
            if (s.FolderMode && s.IncludeSubfolders) sb.Append(" (including subfolders)");
            if (s.UseDateRange)
                sb.Append(string.Format(CultureInfo.InvariantCulture, ", treated {0:dd/MM/yyyy} to {1:dd/MM/yyyy}", s.From, s.To));
            return sb.ToString();
        }

        // =========================================================================================
        // Conversion (background thread)
        // =========================================================================================
        sealed class RunResult { public int Files, Ok, Skipped, Failed; public bool Cancelled; public string LogPath; }

        void Run_Click(object sender, EventArgs e)
        {
            if (worker.IsBusy) return;
            RunSettings s = ReadSettings(true);
            if (s == null) return;
            try { Directory.CreateDirectory(s.Output); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Cannot create the export folder:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            txtLog.Clear();
            progress.Value = 0;
            lblStatus.Text = "Working...";
            worker.RunWorkerAsync(s);
            UpdateEnabled();
        }

        void Worker_DoWork(object sender, DoWorkEventArgs e)
        {
            var s = (RunSettings)e.Argument;
            var bw = (BackgroundWorker)sender;
            var r = new RunResult();
            e.Result = r;
            var runLog = new StringBuilder();
            Action<int, string> report = (pct, line) =>
            {
                if (line != null && !line.StartsWith("#status ")) runLog.Append(line).Append("\r\n");
                bw.ReportProgress(pct, line);
            };

            List<LogFile> files = LogFinder.Find(s);
            r.Files = files.Count;
            report(0, string.Format(CultureInfo.InvariantCulture, "{0:dd/MM/yyyy HH:mm:ss}  {1}", DateTime.Now, Describe(s, files.Count)));
            report(0, "Export folder: " + s.Output);
            if (files.Count == 0) { report(100, "Nothing to do."); return; }

            for (int i = 0; i < files.Count; i++)
            {
                if (bw.CancellationPending) { r.Cancelled = true; report(100 * i / files.Count, "Cancelled."); break; }
                LogFile f = files[i];
                string outDir = f.RelativeDir.Length == 0 ? s.Output : Path.Combine(s.Output, f.RelativeDir);
                report(100 * i / files.Count, "#status " + (i + 1) + " / " + files.Count + ": " + Path.GetFileName(f.FullPath));

                bool anyFail = false, anyOk = false;
                if (s.ToV4)
                {
                    Options opt = MakeOptions(s, Command.ToV4, outDir);
                    RunOne(report, f.FullPath, delegate { return Program.RunToV4(f.FullPath, true, opt); }, ref anyOk, ref anyFail);
                }
                if (s.ToCsv)
                {
                    Options opt = MakeOptions(s, Command.ToCsv, outDir);
                    RunOne(report, f.FullPath, delegate { return Program.RunToCsv(f.FullPath, opt); }, ref anyOk, ref anyFail);
                }
                if (anyFail) r.Failed++;
                else if (anyOk) r.Ok++;
                else r.Skipped++;
                report(100 * (i + 1) / files.Count, null);
            }

            string summary = string.Format(CultureInfo.InvariantCulture, "Done: {0} file(s) - {1} OK, {2} skipped, {3} failed{4}.",
                                           r.Files, r.Ok, r.Skipped, r.Failed, r.Cancelled ? " (cancelled)" : "");
            report(100, "");
            report(100, summary);

            // Keep a record of the run next to the exported files.
            try
            {
                r.LogPath = Path.Combine(s.Output, "TrajectoryLogConversion_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".log");
                File.WriteAllText(r.LogPath, runLog.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                r.LogPath = null;
                report(100, "Could not write the run log: " + ex.Message);
            }
        }

        static void RunOne(Action<int, string> report, string input, Func<List<string>> action, ref bool anyOk, ref bool anyFail)
        {
            try
            {
                foreach (string line in action()) report(-1, line);
                anyOk = true;
            }
            catch (SkipException ex) { report(-1, "SKIP " + Path.GetFileName(input) + ": " + ex.Message); }
            catch (Exception ex) { anyFail = true; report(-1, "FAIL " + Path.GetFileName(input) + ": " + ex.Message); }
        }

        static Options MakeOptions(RunSettings s, Command command, string outDir)
        {
            return new Options
            {
                Command = command,
                Output = outDir,
                Overwrite = s.Overwrite,
                IgnoreCrc = s.IgnoreCrc,
                KeepMachineInfo = command == Command.ToV4 && s.KeepMachineInfo,
                TimeCsv = command == Command.ToV4 && s.TimeCsv,
                NoMlc = command == Command.ToCsv && s.NoMlc,
                ActualOnly = command == Command.ToCsv && s.ActualOnly
            };
        }

        void Worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            if (e.ProgressPercentage >= 0) progress.Value = Math.Max(0, Math.Min(100, e.ProgressPercentage));
            var line = e.UserState as string;
            if (line == null) return;
            if (line.StartsWith("#status ")) { lblStatus.Text = line.Substring(8); return; }
            AppendLog(line);
        }

        void Worker_Completed(object sender, RunWorkerCompletedEventArgs e)
        {
            UpdateEnabled();
            if (e.Error != null)
            {
                lblStatus.Text = "Error.";
                AppendLog("ERROR: " + e.Error.Message);
                MessageBox.Show(this, e.Error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            var r = (RunResult)e.Result;
            lblStatus.Text = string.Format(CultureInfo.InvariantCulture, "{0} OK, {1} skipped, {2} failed{3}.",
                                           r.Ok, r.Skipped, r.Failed, r.Cancelled ? " (cancelled)" : "");
            if (r.LogPath != null) AppendLog("Run log saved to " + r.LogPath);
            if (r.Failed > 0)
                MessageBox.Show(this, r.Failed + " file(s) failed - see the log for details.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        void AppendLog(string line)
        {
            txtLog.AppendText(line + Environment.NewLine);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (worker.IsBusy)
            {
                if (MessageBox.Show(this, "A conversion is running. Stop it and close?", Text,
                                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                worker.CancelAsync();
            }
            base.OnFormClosing(e);
        }
    }
}

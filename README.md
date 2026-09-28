# Halcyon / Ethos Trajectory Log Converter

| File | What it is |
|---|---|
| `TrajectoryLogConverter.sln` | Visual Studio solution for the GUI and the command-line tool. |
| `HalcyonTrajectoryLogTool.cs` | Conversion engine + command-line tool (`to-v4`, `to-csv`). |
| `HalcyonTrajectoryLogTool/` | Visual Studio project for the command-line tool. |
| `HalcyonTrajectoryLogGui/` | Stand-alone Windows GUI (x64, .NET Framework 4.8) built on the engine. |
| `EclipseLauncher/LaunchHalcyonTrajectoryLogTool.cs` | Single-file ESAPI plug-in script that starts the GUI from Eclipse. |

## GUI

* **Input:** a single `.bin` log, or a folder of logs. For a folder you can also include
  subfolders and keep only logs treated within a date range (both dates inclusive).
  The treatment date comes from the `_yyyyMMddHHmmss` time stamp in the file name, or from the
  file's modified date when the name has none. **Find files** lists what will be processed.
* **Output:** choose an export folder and tick **Convert v5.1 to v4.0** and/or **Export CSV**.
  Output files keep the original file name, and subfolder structure is mirrored.
  v4.0 logs are skipped for conversion (they are already v4.0) but can still be exported to CSV.
* Each run writes `TrajectoryLogConversion_<date>_<time>.log` to the export folder.
* Optional arguments that pre-fill the form: `--input <file|folder> --output <folder> --mode v4|csv|both`.

The GUI does not open an ESAPI session. It only reads and writes log files, so it needs no
Eclipse login, license or script approval.

### Build (Visual Studio)

Open `TrajectoryLogConverter.sln` in Visual Studio 2017 or later, with the **.NET desktop development**
workload and the .NET Framework 4.8 targeting pack installed. Select **Release | x64** and use
**Build > Build Solution**. The solution contains:

| Project | Output |
|---|---|
| `HalcyonTrajectoryLogGui` (startup project) | `HalcyonTrajectoryLogGui\bin\x64\Release\HalcyonTrajectoryLogGui.exe` |
| `HalcyonTrajectoryLogTool` (command line) | `HalcyonTrajectoryLogTool\bin\x64\Release\HalcyonTrajectoryLogTool.exe` |

Both projects compile the shared `HalcyonTrajectoryLogTool.cs` as a linked file, so a fix to the
converter applies to both. The Eclipse launcher is listed under *Solution Items* for editing. It
is not built, because Eclipse compiles single-file scripts itself.

Copy the `.exe` to a folder the Eclipse workstations can read, such as a local folder or a network share.

## Eclipse launcher

1. Edit the constants at the top of `LaunchHalcyonTrajectoryLogTool.cs`: `ExePath` (required),
   and optionally `DefaultInputFolder`, `DefaultExportFolder` and `DefaultMode`.
2. Copy it into your Eclipse scripts folder (e.g. `\\<server>\va_data$\ProgramData\Vision\PublishedScripts`).
3. In Eclipse, go to **Tools > Scripts** and run `LaunchHalcyonTrajectoryLogTool.cs`. The converter
   opens as a separate process, so Eclipse remains usable.

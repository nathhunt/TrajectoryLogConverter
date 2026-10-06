# Halcyon / Ethos Trajectory Log Converter

| File | What it is |
|---|---|
| `TrajectoryLogConverter.sln` | Solution for the GUI and the command-line tool (Visual Studio or VS Code). |
| `HalcyonTrajectoryLogTool.cs` | Conversion engine + command-line tool (`to-v4`, `to-csv`, and for validation `to-v5`, `version`, `compare`). |
| `HalcyonTrajectoryLogTool/` | Project for the command-line tool. |
| `HalcyonTrajectoryLogGui/` | Stand-alone Windows GUI (x64, .NET Framework 4.8) built on the engine. |
| `EclipseLauncher/LaunchHalcyonTrajectoryLogTool.cs` | Single-file ESAPI plug-in script that starts the GUI from Eclipse. |
| `scripts/RoundTripTest.ps1` | Round-trip test for the command-line tool (v5.1 -> v4.0 -> v5.1 and back, then compare). |
| `tests/HalcyonTrajectoryLogTool.Tests/` | Unit tests for the conversion engine (xUnit). |
| `.vscode/` | VS Code build tasks, launch (debug) configurations and settings. |

## GUI

* **Input:** a single `.bin` log, or a folder of logs. For a folder you can also include
  subfolders and keep only logs treated within a date range (both dates inclusive).
  The treatment date comes from the `_yyyyMMddHHmmss` time stamp in the file name, or from the
  file's modified date when the name has none. **Find files** lists what will be processed.
* **Output:** choose an export folder and tick **Convert v5.1 to v4.0** and/or **Export CSV**.
  Output files keep the original file name, and subfolder structure is mirrored.
  v4.0 logs are skipped for conversion (they are already v4.0) but can still be exported to CSV.
  Converted logs have the time axis removed and the axes put in the order v4.0 machines write them
  (Coll, Gantry, jaws, couch, MU, BeamHold, ControlPoint, MLC). v5.1 logs use a different order.
* Each run writes `TrajectoryLogConversion_<date>_<time>.log` to the export folder.
* Optional arguments that pre-fill the form: `--input <file|folder> --output <folder> --mode v4|csv|both`.

The GUI does not open an ESAPI session. It only reads and writes log files, so it needs no
Eclipse login, license or script approval.

### Build

Both projects are SDK-style .NET Framework 4.8 (x64) projects, so the same
`TrajectoryLogConverter.sln` builds in Visual Studio and VS Code. The .NET Framework 4.8 reference
assemblies come from NuGet (`Microsoft.NETFramework.ReferenceAssemblies`), so the 4.8 Developer
Pack is not required. The first build needs access to nuget.org.

**Visual Studio 2019 or later:** open `TrajectoryLogConverter.sln`, select **Release | x64**, then
use **Build > Build Solution**. This needs the **.NET desktop development** workload.

**VS Code:** install the [.NET SDK](https://dotnet.microsoft.com/download) (6.0 or later) and the
**C#** extension (VS Code offers it when the folder is opened; C# Dev Kit is not needed). Open the
repository folder, then press **Ctrl+Shift+B** to build, or run this in the terminal:

```
dotnet build TrajectoryLogConverter.sln -c Release
```

The `.vscode` folder holds the shared workspace setup:

| File | What it has |
|---|---|
| `tasks.json` | **build** (Debug, default), **build release** (also fills `Deploy\`), **build release to folder** (asks for the deploy folder), **build tool** / **build tool release** (command-line tool only), **clean**, **rebuild**; **tool: run** (`to-v4` / `to-csv` / `to-v5`), **tool: check versions**, **tool: compare**, **tool: round-trip test**; **open Deploy folder**. Run them from **Terminal > Run Task**. |
| `launch.json` | Debug the GUI (empty or pre-filled form) and the command-line tool (convert / export, version, compare), or attach to a running process. Press **F5** and pick one. |
| `settings.json` | Uses the C# extension on its own and loads `TrajectoryLogConverter.sln`. |
| `extensions.json` | Recommends the C# extension. |

`scripts\RoundTripTest.ps1` converts each log in a file or folder to the other version and back,
then compares the result with the original; every log should come back byte-for-byte identical.
Outputs go to a `roundtrip` folder next to the input:

```
powershell -ExecutionPolicy Bypass -File scripts\RoundTripTest.ps1 -Path D:\Logs
```

### Unit tests

`tests/HalcyonTrajectoryLogTool.Tests` holds xUnit tests for the conversion engine
(`HalcyonTrajectoryLogTool.cs`, compiled in as a linked file, like the other two projects). The tests
build their own synthetic v4.0 and v5.1 logs byte by byte from the specification layout, so no
patient logs are needed. They cover parsing and validation, CRC variants, `to-v4`, `to-v5`
(time axis, couch axes, machine info), byte-for-byte round trips, `compare`, CSV export,
option parsing, and the command-line exit codes.

```
dotnet test tests/HalcyonTrajectoryLogTool.Tests
```

or run the **test** task in VS Code (**Terminal > Run Task**), or use **Test > Run All Tests** in
Visual Studio. The tests target `net8.0`, so they need the .NET 8 SDK or later, and run on any OS;
on Windows they also run on .NET Framework 4.8. For code coverage:

```
dotnet test tests/HalcyonTrajectoryLogTool.Tests --collect:"XPlat Code Coverage" --settings tests/coverage.runsettings
```

The GUI (`HalcyonTrajectoryLogGui.cs`) is Windows Forms and is not unit tested.

The solution contains:

| Project | What it builds |
|---|---|
| `HalcyonTrajectoryLogGui` (startup project) | `HalcyonTrajectoryLogGui.exe`, the GUI |
| `HalcyonTrajectoryLogTool` | `HalcyonTrajectoryLogTool.exe`, the command-line tool |
| `HalcyonTrajectoryLogTool.Tests` (under *tests*) | Unit tests for the engine |

All three projects compile the shared `HalcyonTrajectoryLogTool.cs` as a linked file, so a fix to the
converter applies to the GUI and the tool, and is tested by the unit tests. The Eclipse launcher is listed under *Solution Items* for editing. It
is not built, because Eclipse compiles single-file scripts itself.

A Release build also copies the two files to be deployed into one folder at the repository root
(Debug builds are not copied):

```
Deploy\
    HalcyonTrajectoryLogGui.exe
    HalcyonTrajectoryLogGui.exe.config
    LaunchHalcyonTrajectoryLogTool.cs
```

Deploy the `.exe.config` alongside the `.exe`. It tells Windows the exe needs .NET Framework 4.8.

To build straight into a network share instead, set `DeployDir`:

```
dotnet build TrajectoryLogConverter.sln -c Release -p:DeployDir=\\server\ESAPI\TrajectoryLog\
```

Copy the `.exe` to a folder the Eclipse workstations can read, such as a local folder or a network share.

## Validation and testing commands

These are only in the command-line tool (`HalcyonTrajectoryLogTool.exe`), not in the GUI.

```
HalcyonTrajectoryLogTool to-v4   <input.bin | folder> [-o <file|folder>] --drop-couch-rotations
HalcyonTrajectoryLogTool to-v5   <input.bin | folder> [-o <file|folder>] [--time-from <csv>] [--start-time hh:mm:ss]
                                 [--serial <text>] [--add-couch-rotations]
HalcyonTrajectoryLogTool version <input.bin | folder> [--expect 4.0|5.1]
HalcyonTrajectoryLogTool compare <a.bin | folderA> <b.bin | folderB> [--ignore-version] [--tolerance x] [--max-diffs n]
```

* **`to-v5`** converts a v4.0 log to v5.1 (default output: `v5.1` subfolder). It adds the time axis
  (43) and puts the axes in the order HAL 5.0 machine logs use: Time, ControlPoint, MU, BeamHold,
  Gantry, Coll, Y1, Y2, X1, X2, couch, MLC (v4.0 logs use Coll, Gantry, jaws, couch, MU, BeamHold,
  ControlPoint, MLC; the spec does not state either order). Per the spec, the time is seconds since midnight in the "expected"
  record and the "actual" record is empty (written as 0). v4.0 logs do not record clock time, so
  the time axis is either restored from the `_time.csv` that `to-v4 --time-csv` writes, or
  generated as start time + snapshot x 20 ms. The start time comes from `--start-time`, else from
  the `_yyyyMMddHHmmss` stamp in the file name. Generated times leave out beam pauses.
  The machine specifier and serial number are copied; if they were zeroed by `to-v4`, the
  specifier is set to 1 (Halcyon / Ethos, since 0 means TrueBeam) and `--serial` can supply the
  serial number. The axis scale is copied; HAL 5.0 writes 3, so a different value gets a warning
  (couch values are not converted).
* **`to-v4 --drop-couch-rotations`** leaves the couch rotation, pitch and roll axes (9, 10, 11)
  out of the converted log. **`to-v5 --add-couch-rotations`** adds any of these three axes that
  are missing, with the couch at its zero position in every snapshot (expected and actual):
  rotation 180° in the Varian machine scale (axis scale 1 or 3, as in HAL 5.0 logs) or 0° in
  Modified IEC 61217 (axis scale 2), and pitch and roll 0°. Axes already in the log are left as
  they are.
* **`version`** reads only the first 32 bytes of each file and prints `v4.0` or `v5.1`. With
  `--expect`, a log of any other version is a failure (exit code 1).
* **`compare`** compares header fields, subbeams and every expected/actual snapshot value. It
  prints `SAME` (byte-for-byte or same content) or `DIFF` with a summary per axis and the first
  differing values. Axes are matched by axis id, so a v4.0 log can be compared with a v5.1 log.
  `--ignore-version` ignores the differences between 4.0 and 5.1 that are there by design (version
  string, time axis, axis order, machine specifier and serial number). With two folders, files are matched
  by name. Exit code 0 means everything is the same.

A full round trip gives back the original file byte for byte:

```
HalcyonTrajectoryLogTool to-v4 log.bin -o v4\ --time-csv --keep-machine-info
HalcyonTrajectoryLogTool to-v5 v4\log.bin -o rt\ --time-from v4\log_time.csv
HalcyonTrajectoryLogTool compare log.bin rt\log.bin
```

To check that a v4.0 conversion kept all the data, compare it with the original using `--ignore-version`:

```
HalcyonTrajectoryLogTool compare original\ converted\v4.0\ --ignore-version
```

The `_time.csv` written by `to-v4 --time-csv` now also has `Expected` and `Actual` columns with
the exact time values, which `to-v5 --time-from` uses. Older `_time.csv` files still work but
restore the time to the nearest millisecond only.

## Eclipse launcher

1. Edit the constants at the top of `LaunchHalcyonTrajectoryLogTool.cs`: `ExePath` (required),
   and optionally `DefaultInputFolder`, `DefaultExportFolder` and `DefaultMode`.
2. Copy it into your Eclipse scripts folder (e.g. `\\<server>\va_data$\ProgramData\Vision\PublishedScripts`).
3. In Eclipse, go to **Tools > Scripts** and run `LaunchHalcyonTrajectoryLogTool.cs`. The converter
   opens as a separate process, so Eclipse remains usable.

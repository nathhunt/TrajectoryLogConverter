@echo off
rem Builds HalcyonTrajectoryLogGui.exe with the C# compiler that ships with .NET Framework 4.x
rem (present on every Eclipse workstation) - no Visual Studio needed. Output: bin\HalcyonTrajectoryLogGui.exe
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (echo csc.exe not found at %CSC% & exit /b 1)
if not exist bin mkdir bin
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ ^
  /main:HalcyonTrajectoryLogTool.Gui.GuiProgram ^
  /reference:System.Windows.Forms.dll /reference:System.Drawing.dll ^
  /out:bin\HalcyonTrajectoryLogGui.exe ^
  HalcyonTrajectoryLogGui.cs ..\HalcyonTrajectoryLogTool.cs
if errorlevel 1 (echo Build failed. & exit /b 1)
echo Built bin\HalcyonTrajectoryLogGui.exe

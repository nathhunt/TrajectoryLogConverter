<#
.SYNOPSIS
    Round-trip test for HalcyonTrajectoryLogTool: v5.1 -> v4.0 -> v5.1, then compare with the original.

.DESCRIPTION
    For each v5.1 log: to-v4 --time-csv --keep-machine-info, then to-v5 --time-from the saved
    time CSV, then compare the result with the original. Every log should come back
    "byte-for-byte identical". v4.0 logs are round-tripped the other way (v4.0 -> v5.1 -> v4.0).
    Outputs go to a "roundtrip" folder next to the input. Exit code 0 = all logs identical.

.EXAMPLE
    .\scripts\RoundTripTest.ps1 -Path D:\Logs
    .\scripts\RoundTripTest.ps1 -Path D:\Logs\Field1_20260716142318.bin -Exe .\HalcyonTrajectoryLogTool\bin\x64\Release\HalcyonTrajectoryLogTool.exe
#>
param(
    [Parameter(Mandatory = $true)] [string] $Path,
    [string] $Exe = (Join-Path $PSScriptRoot '..\HalcyonTrajectoryLogTool\bin\x64\Debug\HalcyonTrajectoryLogTool.exe')
)

if (-not (Test-Path $Exe)) { Write-Error "Tool not found: $Exe (build it first)"; exit 2 }
$Path = (Resolve-Path $Path).Path
$logs = if (Test-Path $Path -PathType Container) { Get-ChildItem $Path -Filter *.bin -File } else { Get-Item $Path }
if (-not $logs) { Write-Error "No .bin files found in $Path"; exit 2 }

$failed = 0
foreach ($log in $logs) {
    $root = Join-Path $log.DirectoryName 'roundtrip'
    $base = [IO.Path]::GetFileNameWithoutExtension($log.Name)
    $version = (& $Exe version $log.FullName) -replace '^v(\S+).*$', '$1'
    if ($LASTEXITCODE -ne 0) { $failed++; continue }

    $mid = Join-Path $root $(if ($version -eq '5.1') { 'v4.0' } else { 'v5.1' })
    $out = Join-Path $root "v$version"
    # Existing folders are used as output folders, so no trailing "\" is needed (it would escape
    # the closing quote of a path with spaces).
    New-Item -ItemType Directory -Force -Path $mid, $out | Out-Null

    if ($version -eq '5.1') {
        & $Exe to-v4 $log.FullName -o $mid --time-csv --keep-machine-info --overwrite | Out-Null
        if ($LASTEXITCODE -eq 0) {
            & $Exe to-v5 (Join-Path $mid $log.Name) -o $out --time-from (Join-Path $mid "$($base)_time.csv") --overwrite | Out-Null
        }
    }
    else {
        & $Exe to-v5 $log.FullName -o $mid --overwrite | Out-Null
        if ($LASTEXITCODE -eq 0) { & $Exe to-v4 (Join-Path $mid $log.Name) -o $out --overwrite | Out-Null }
    }
    if ($LASTEXITCODE -ne 0) { Write-Host "FAIL $($log.Name): conversion failed"; $failed++; continue }

    & $Exe compare $log.FullName (Join-Path $out $log.Name)
    if ($LASTEXITCODE -ne 0) { $failed++ }
}

Write-Host ("{0} log(s), {1} not identical or failed" -f @($logs).Count, $failed)
exit ([int]($failed -ne 0))

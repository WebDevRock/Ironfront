<#
.SYNOPSIS
  Runs the Ironfront AI-vs-AI skirmish study headless (no Editor play mode, no rendering).

.DESCRIPTION
  1. Builds the Windows player (Builds/Windows/Ironfront.exe) if it is missing or older than any file under Assets/,
     or when -Rebuild is given. Uses the open Editor when one is running on this project (through the `unity command`
     bridge), otherwise a batch-mode Unity.
  2. Starts the player with -batchmode -nographics -ironfrontSkirmishStudy. It plays the plan with the same code as
     the Editor PlayMode study (Ironfront.Diagnostics.SkirmishStudy): same maps, seeds, start selection, commanders,
     10x time scale and 2700 s limit. Each frame advances a fixed 1/60 s x 10 of game time, and frames run as fast as the
     CPU allows.
  3. Writes skirmish_runs.jsonl, skirmish_trace.txt, skirmish_study.txt, skirmish_done.txt and skirmish_player.log
     to -Out (default: the project's Temp/, the same files the Editor study wrote, so the existing analysis scripts
     work unchanged).

.PARAMETER Plan
  "map:seed:F1difficulty:F2difficulty,..." Default: the 12-match comparison plan used since Milestone 009.
  Pass "default" for the runner's built-in 28-match plan.
.PARAMETER Out
  Output directory. Existing study files there are replaced unless -Append is given.
.PARAMETER Rebuild
  Always rebuild the player first.
.PARAMETER SkipBuild
  Never build; use the existing player as it is.
.PARAMETER Graphics
  Fall back to -batchmode without -nographics (if a future change needs a graphics device).
.EXAMPLE
  pwsh Tools/SkirmishStudy/run_study.ps1
.EXAMPLE
  pwsh Tools/SkirmishStudy/run_study.ps1 -Plan "dustfall_plains:11:Normal:Normal" -Out D:\studies\probe
#>
param(
    [string]$Plan = "dustfall_plains:11:Normal:Normal,dustfall_plains:23:Normal:Normal,dustfall_plains:61:Hard:Normal,iron_pass:11:Normal:Normal,iron_pass:23:Normal:Normal,iron_pass:61:Hard:Normal,green_divide:11:Normal:Normal,green_divide:23:Normal:Normal,green_divide:61:Hard:Normal,blackstone_basin:11:Normal:Normal,blackstone_basin:23:Normal:Normal,blackstone_basin:61:Hard:Normal",
    [string]$Out = "",
    [switch]$Rebuild,
    [switch]$SkipBuild,
    [switch]$Graphics,
    [switch]$Append
)
$ErrorActionPreference = "Stop"
$project = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (-not $Out) { $Out = Join-Path $project "Temp" }
$exe = Join-Path $project "Builds\Windows\Ironfront.exe"
$unityExe = "C:\Program Files\Unity\Hub\Editor\6000.6.5f1\Editor\Unity.exe"
if ($Plan -eq "default") { $Plan = "" }

function Test-PlayerStale {
    if (-not (Test-Path $exe)) { return $true }
    $built = (Get-Item $exe).LastWriteTimeUtc
    $newer = Get-ChildItem (Join-Path $project "Assets") -Recurse -File |
        Where-Object { $_.FullName -notlike "*\Assets\Temp\*" -and $_.LastWriteTimeUtc -gt $built } | Select-Object -First 1
    return $null -ne $newer
}

function Test-EditorOpen {
    if (-not (Get-Command unity -ErrorAction SilentlyContinue)) { return $false }
    Push-Location $project
    try { $status = & unity command editor_status --result-only 2>&1 | Out-String } finally { Pop-Location }
    return $status -match '"status":\s*"ready"'
}

function Build-Player {
    $started = Get-Date
    if (Test-EditorOpen) {
        Write-Host "Building the player in the open Editor..."
        Push-Location $project
        try {
            & unity command editor_stop --result-only 2>&1 | Out-Null
            # The bridge times out after ~5 s but the build carries on on the Editor's main thread.
            & unity command eval --code "return Ironfront.EditorTools.IronfrontBuild.BuildWindows();" --result-only 2>&1 | Out-Null
        } finally { Pop-Location }
        $log = Join-Path $project "Logs\Editor.log"
        $deadline = (Get-Date).AddMinutes(60)
        while ((Get-Date) -lt $deadline) {
            Start-Sleep -Seconds 10
            $line = Select-String -Path $log -Pattern "\[Ironfront\] Build (Succeeded|Failed|Cancelled)" -ErrorAction SilentlyContinue |
                Select-Object -Last 1
            $fresh = (Test-Path $exe) -and (Get-Item $exe).LastWriteTime -ge $started
            if ($line -and $line.Line -match "Build Succeeded" -and $fresh) { Write-Host $line.Line; break }
            if ($line -and $line.Line -match "Failed|Cancelled" -and (Get-Item $log).LastWriteTime -ge $started) { throw "Player build failed: $($line.Line)" }
        }
        if (-not (Test-Path $exe) -or (Get-Item $exe).LastWriteTime -lt $started) { throw "Player build did not finish within 60 minutes" }
    }
    else {
        Write-Host "Building the player with batch-mode Unity..."
        $buildLog = Join-Path $project "Builds\build.log"
        New-Item -ItemType Directory -Force -Path (Split-Path $buildLog) | Out-Null
        $p = Start-Process $unityExe -ArgumentList @("-batchmode", "-quit", "-projectPath", "`"$project`"",
            "-executeMethod", "Ironfront.EditorTools.IronfrontBuild.BuildWindows", "-logFile", "`"$buildLog`"") -PassThru -Wait
        if ($p.ExitCode -ne 0 -or -not (Test-Path $exe) -or (Get-Item $exe).LastWriteTime -lt $started) { throw "Player build failed (see $buildLog)" }
    }
}

if (-not $SkipBuild -and ($Rebuild -or (Test-PlayerStale))) { Build-Player } else { Write-Host "Using the existing player: $exe" }
if (-not (Test-Path $exe)) { throw "No player at $exe" }

New-Item -ItemType Directory -Force -Path $Out | Out-Null
if (-not $Append) {
    foreach ($f in "skirmish_runs.jsonl", "skirmish_trace.txt", "skirmish_study.txt", "skirmish_done.txt") {
        $path = Join-Path $Out $f
        if (Test-Path $path) { Remove-Item $path }
    }
}
$playerArgs = @("-batchmode")
if (-not $Graphics) { $playerArgs += "-nographics" }
$playerArgs += @("-ironfrontSkirmishStudy", "-out", "`"$Out`"", "-logFile", "`"$(Join-Path $Out 'skirmish_player.log')`"")
if ($Plan) { $playerArgs += @("-plan", "`"$Plan`"") }
$count = if ($Plan) { ($Plan -split ",").Count } else { 28 }
Write-Host "Running $count matches headless -> $Out"
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$p = Start-Process $exe -ArgumentList $playerArgs -PassThru -Wait
$sw.Stop()
$done = Join-Path $Out "skirmish_done.txt"
$status = if (Test-Path $done) { (Get-Content $done -Raw).Trim() } else { "no completion marker (see skirmish_player.log)" }
Write-Host ("Finished in {0:N0} s, exit code {1}: {2}" -f $sw.Elapsed.TotalSeconds, $p.ExitCode, $status)
exit $p.ExitCode

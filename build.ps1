<#
.SYNOPSIS
    Builds a standalone Windows .exe for Drone Trainer.

.DESCRIPTION
    Runs Unity headlessly (-batchmode -nographics) and calls SceneBuilder.BuildPlayer,
    which regenerates the training scene (if missing) and produces a Windows x64 player
    in .\Build\DroneTrainer.exe. No Unity Editor window is opened.

.PARAMETER Zip
    Also pack the Build folder into Build\DroneTrainer-win64.zip for sharing.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Zip
#>
param([switch]$Zip)

$ErrorActionPreference = "Stop"
$ProjectPath = $PSScriptRoot
$BuildDir    = Join-Path $ProjectPath "Build"
$LogFile     = Join-Path $ProjectPath "build.log"

# ---- locate the Unity editor (matches ProjectSettings/ProjectVersion.txt, falls back to newest installed) ----
function Find-UnityExe {
    $verFile = Join-Path $ProjectPath "ProjectSettings\ProjectVersion.txt"
    $wanted = $null
    if (Test-Path $verFile) {
        $line = Get-Content $verFile | Where-Object { $_ -match '^m_EditorVersion:\s*(\S+)' }
        if ($line -match '^m_EditorVersion:\s*(\S+)') { $wanted = $Matches[1] }
    }
    $hubRoot = "C:\Program Files\Unity\Hub\Editor"
    if ($wanted -and (Test-Path (Join-Path $hubRoot "$wanted\Editor\Unity.exe"))) {
        return Join-Path $hubRoot "$wanted\Editor\Unity.exe"
    }
    if (Test-Path $hubRoot) {
        $newest = Get-ChildItem $hubRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1
        if ($newest) { return Join-Path $newest.FullName "Editor\Unity.exe" }
    }
    throw "Could not find a Unity editor under $hubRoot. Install it via Unity Hub first."
}

$UnityExe = Find-UnityExe
Write-Host "Unity editor : $UnityExe"
Write-Host "Project      : $ProjectPath"
Write-Host "Output       : $BuildDir\DroneTrainer.exe"
Write-Host ""

# ---- clean stale editor lock / recovery state that can block a fresh batch run ----
Get-Process -Name "Unity" -ErrorAction SilentlyContinue | Stop-Process -Force -Confirm:$false
Start-Sleep -Seconds 2
Remove-Item (Join-Path $ProjectPath "Temp") -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $ProjectPath "Assets\_Recovery"), (Join-Path $ProjectPath "Assets\_Recovery.meta") -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $LogFile -ErrorAction SilentlyContinue

# ---- run the build ----
Write-Host "Building... (a few minutes; full log: $LogFile)"
$proc = Start-Process -FilePath $UnityExe -ArgumentList @(
    "-batchmode", "-nographics",
    "-projectPath", $ProjectPath,
    "-executeMethod", "SceneBuilder.BuildPlayer",
    "-logFile", $LogFile,
    "-quit"
) -PassThru -Wait

$exePath = Join-Path $BuildDir "DroneTrainer.exe"
$resultLine = Select-String -Path $LogFile -Pattern "player build (Succeeded|Failed)" -ErrorAction SilentlyContinue | Select-Object -Last 1
$errorLines = Select-String -Path $LogFile -Pattern "error CS|Exception" -ErrorAction SilentlyContinue

if ($proc.ExitCode -eq 0 -and $resultLine -match "Succeeded" -and (Test-Path $exePath)) {
    $sizeMb = [math]::Round((Get-ChildItem $BuildDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
    Write-Host ""
    Write-Host "BUILD SUCCEEDED" -ForegroundColor Green
    Write-Host "  $exePath"
    Write-Host "  Total size: $sizeMb MB"

    if ($Zip) {
        $zipPath = Join-Path $BuildDir "DroneTrainer-win64.zip"
        Remove-Item $zipPath -ErrorAction SilentlyContinue
        Compress-Archive -Path (Join-Path $BuildDir "*") -DestinationPath $zipPath -CompressionLevel Optimal
        Write-Host "  Zipped:     $zipPath"
    }
    Write-Host ""
    Write-Host "Run it with:  & `"$exePath`""
    exit 0
}
else {
    Write-Host ""
    Write-Host "BUILD FAILED" -ForegroundColor Red
    Write-Host "  exit code : $($proc.ExitCode)"
    if ($errorLines) {
        Write-Host "  errors found in the log:"
        $errorLines | Select-Object -First 15 | ForEach-Object { Write-Host "    $($_.Line)" }
    }
    Write-Host "  full log: $LogFile"
    exit 1
}

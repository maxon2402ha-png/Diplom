# Records the automated demo with ffmpeg gdigrab, runs the FlaUI driver, trims to exactly 270s.
param(
    [int]$TrimStart = 3,      # seconds to drop from the head (clean-desktop lead-in)
    [double]$TrimDur = 270.0  # final exact duration
)

$ErrorActionPreference = 'Stop'
$demoDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$driverExe = 'C:\demo_build\DemoDriver\bin\Release\net8.0-windows\DemoDriver.exe'
$raw       = Join-Path $demoDir 'raw.mp4'
$final     = Join-Path $demoDir 'demo.mp4'

$ffmpeg = (Get-Command ffmpeg -ErrorAction SilentlyContinue).Source
if (-not $ffmpeg) { $ffmpeg = "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-8.1.1-full_build\bin\ffmpeg.exe" }
$ffprobe = (Get-Command ffprobe -ErrorAction SilentlyContinue).Source
if (-not $ffprobe) { $ffprobe = (Split-Path $ffmpeg) + '\ffprobe.exe' }

# Clean state
Get-Process -Name 'КР_Ханников' -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item $raw, $final -ErrorAction SilentlyContinue

# Start ffmpeg recording the full desktop (30 fps, no audio, cursor drawn), stdin kept open for clean 'q' stop.
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName  = $ffmpeg
$psi.Arguments = '-y -f gdigrab -framerate 30 -draw_mouse 1 -i desktop -c:v libx264 -preset veryfast -pix_fmt yuv420p "' + $raw + '"'
$psi.RedirectStandardInput = $true
$psi.UseShellExecute = $false
$ff = [System.Diagnostics.Process]::Start($psi)
Write-Host "ffmpeg PID $($ff.Id) recording -> $raw"
Start-Sleep -Seconds 1

# Run the choreography (blocks until complete)
& $driverExe demo
Write-Host "driver finished, stopping ffmpeg..."

# Graceful stop
try { $ff.StandardInput.WriteLine('q'); $ff.StandardInput.Flush() } catch {}
if (-not $ff.WaitForExit(15000)) { $ff.Kill() }

# Kill the app
Get-Process -Name 'КР_Ханников' -ErrorAction SilentlyContinue | Stop-Process -Force

# Trim to exactly TrimDur seconds, force 1920x1080
& $ffmpeg -y -ss $TrimStart -i $raw -t $TrimDur -vf "scale=1920:1080:flags=lanczos" -c:v libx264 -preset medium -crf 20 -pix_fmt yuv420p -movflags +faststart $final

Write-Host "`n=== FINAL DURATION ==="
& $ffprobe -v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 $final
& $ffprobe -v error -select_streams v:0 -show_entries stream=width,height,r_frame_rate -of csv=p=0 $final

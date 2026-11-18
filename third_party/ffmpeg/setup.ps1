# Downloads FFmpeg shared DLLs for the NVENC hardware encoder.
# Source: gyan.dev ffmpeg-release-full-shared (FFmpeg 9.0.2)
# Usage: powershell -ExecutionPolicy Bypass -File setup.ps1
$ErrorActionPreference = "Stop"
$out = Join-Path $env:TEMP "ffmpeg-shared.7z"
$dest = Join-Path $env:TEMP "ffmpeg-extract"
if (-not (Test-Path $out) -or (Get-Item $out).Length -lt 10MB) {
    Write-Host "downloading ffmpeg-release-full-shared.7z..."
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-full-shared.7z" -OutFile $out
}
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
& "C:\Program Files\7-Zip\7z.exe" x $out -o$dest -y | Out-Null
$bin = Get-ChildItem $dest -Recurse -Filter "bin" | Where-Object { $_.PSIsContainer } | Select-Object -First 1
foreach ($dll in @("avcodec-63.dll","avutil-61.dll","swscale-10.dll","swresample-7.dll","avformat-63.dll")) {
    Copy-Item (Join-Path $bin.FullName $dll) (Join-Path $PSScriptRoot $dll) -Force
}
Write-Host "DLLs copied to third_party/ffmpeg/"

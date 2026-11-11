# Builds the Grand Admiral self-play player (headless Linux server) and ships it to the cluster's farm.
#
#   powershell -ExecutionPolicy Bypass -File GrandAdmiral\tools\build-selfplay.ps1 [-NoUpload]
#
# * Builds from a copy of this working tree (Assets, Packages, ProjectSettings) in its own folder, with
#   the Unity version that has Linux build support, so your project and its Library are never touched.
#   The first build imports everything (slow); later ones only re-import what changed.
# * Uploads the build to grand-admiral-player-store and restarts the farm, which then plays it.
# Re-run after any game change you want self-play to train on (combat, AI, Grand Admiral code).
param(
    [string]$Project = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string]$Work = (Join-Path $env:USERPROFILE "OsirisSelfPlayBuild"),
    [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Unity.exe",
    [string]$Namespace = "oawga-machine-learning",
    [switch]$NoUpload
)
$ErrorActionPreference = "Stop"
if (-not (Test-Path $Unity)) { throw "Unity with Linux build support not found at $Unity (pass -Unity)." }

Write-Host "Copying sources to $Work"
foreach ($dir in "Assets", "Packages", "ProjectSettings") {
    robocopy (Join-Path $Project $dir) (Join-Path $Work $dir) /MIR /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed for $dir ($LASTEXITCODE)" }
}

$build = Join-Path $Work "Builds\SelfPlay"
if (Test-Path $build) { Remove-Item -Recurse -Force $build }
$log = Join-Path $Work "selfplay-build.log"
Write-Host "Building (log: $log)"
$p = Start-Process $Unity -Wait -PassThru -ArgumentList @("-batchmode", "-quit", "-nographics", "-projectPath", "`"$Work`"",
    "-executeMethod", "SelfPlayBuild.Build", "-buildPath", "Builds/SelfPlay/osiris-selfplay", "-logFile", "`"$log`"")
if ($p.ExitCode -ne 0 -or -not (Test-Path (Join-Path $build "osiris-selfplay"))) { throw "Build failed ($($p.ExitCode)); see $log" }

# Debug symbols Unity marks DoNotShip stay behind; everything else goes in the tarball.
Get-ChildItem $build -Directory -Filter "*DoNotShip*" | Remove-Item -Recurse -Force
$tarball = Join-Path $Work "osiris-selfplay.tar.gz"
if (Test-Path $tarball) { Remove-Item -Force $tarball }
tar -czf $tarball -C $build .
if ($LASTEXITCODE -ne 0) {
    # Unity can still hold a file for a moment after it exits: wait and try once more.
    Start-Sleep -Seconds 15
    tar -czf $tarball -C $build .
    if ($LASTEXITCODE -ne 0) { throw "tar failed" }
}
Write-Host ("Packed {0:N0} MB" -f ((Get-Item $tarball).Length / 1MB))
if ($NoUpload) { return }

$store = kubectl -n $Namespace get pod -l app=grand-admiral-player-store -o jsonpath="{.items[0].metadata.name}"
if (-not $store) { throw "No player-store pod in $Namespace (apply GrandAdmiral/k8s first)." }
Write-Host "Uploading to $store"
# In 20 MB pieces: one ~130 MB `kubectl cp` times out on the API server's stream.
$parts = Join-Path $Work "parts"
if (Test-Path $parts) { Remove-Item -Recurse -Force $parts }
New-Item -ItemType Directory $parts | Out-Null
$bytes = [System.IO.File]::ReadAllBytes($tarball)
$size = 20MB
for ($i = 0; $i * $size -lt $bytes.Length; $i++) {
    $count = [Math]::Min($size, $bytes.Length - $i * $size)
    $chunk = New-Object byte[] $count
    [Array]::Copy($bytes, $i * $size, $chunk, 0, $count)
    [System.IO.File]::WriteAllBytes((Join-Path $parts ("p{0:D3}" -f $i)), $chunk)
}
$html = "/usr/share/nginx/html"
kubectl -n $Namespace exec $store -- sh -c "rm -rf $html/parts && mkdir $html/parts"
Push-Location $parts
try {
    foreach ($part in Get-ChildItem -File | Sort-Object Name) {
        $sent = $false
        foreach ($try in 1..3) {
            kubectl -n $Namespace cp $part.Name "${store}:$html/parts/$($part.Name)"
            if ($LASTEXITCODE -eq 0) { $sent = $true; break }
        }
        if (-not $sent) { throw "upload of $($part.Name) failed" }
    }
} finally { Pop-Location; Remove-Item -Recurse -Force $parts }
$want = (Get-FileHash $tarball -Algorithm SHA256).Hash.ToLower()
$got = kubectl -n $Namespace exec $store -- sh -c "cd $html && cat parts/p* > osiris-selfplay.tar.gz.new && sha256sum osiris-selfplay.tar.gz.new | cut -d' ' -f1"
if ($got.Trim() -ne $want) { throw "upload checksum mismatch ($got vs $want)" }
kubectl -n $Namespace exec $store -- sh -c "cd $html && mv osiris-selfplay.tar.gz.new osiris-selfplay.tar.gz && rm -rf parts"
kubectl -n $Namespace rollout restart deployment/grand-admiral-selfplay
Write-Host "Self-play farm restarted on the new build."

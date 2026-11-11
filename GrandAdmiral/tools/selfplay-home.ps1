# Grand Admiral self-play on a home PC: Grand Admiral (always Avanti) vs Admiral in headless Unity,
# uploading its battles and win rates to the cluster as a trusted self-play install.
#
#   powershell -ExecutionPolicy Bypass -File GrandAdmiral\tools\selfplay-home.ps1 [-Rounds 25] [-Loop]
#
# * Plays in its own clone of the repo (default %USERPROFILE%\OsirisSelfPlay, updated from main
#   before each run), so it never fights the Unity editor over your working copy.
# * Reaches the API through `kubectl port-forward` (the game accepts http://localhost), so it needs no
#   DNS record and nothing exposed beyond your kubeconfig.
# * Needs a credentials file for this PC's self-play identity, made once (see README, "Self-play"):
#     kubectl -n oawga-machine-learning exec deploy/grand-admiral-api -- python -m app.admin selfplay-credentials --name home-pc |
#       Out-File -Encoding ascii $HOME\.osiris-grand-admiral\selfplay-home.json
#   and its install_id added to selfplay-installs in k8s/config.yaml.
param(
    [int]$Rounds = 25,
    [switch]$Loop,
    [string]$Clone = (Join-Path $env:USERPROFILE "OsirisSelfPlay"),
    [string]$Credentials = (Join-Path $env:USERPROFILE ".osiris-grand-admiral\selfplay-home.json"),
    [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.3.3f1\Editor\Unity.exe",
    [string]$Repo = "https://github.com/LoganCHobson/Osiris-At-War-2.git",
    [string]$Namespace = "oawga-machine-learning",
    [int]$Port = 18080
)
$ErrorActionPreference = "Stop"

if (-not (Test-Path $Credentials)) { throw "No self-play credentials at $Credentials (see the header of this script)." }
if (-not (Test-Path $Unity)) { throw "Unity not found at $Unity (pass -Unity)." }
$logs = Join-Path $env:USERPROFILE ".osiris-grand-admiral\selfplay-logs"
New-Item -ItemType Directory -Force $logs | Out-Null

do {
    if (Test-Path (Join-Path $Clone ".git")) {
        git -C $Clone fetch --quiet origin main
        git -C $Clone checkout --quiet --force FETCH_HEAD
    } else {
        git clone --quiet --branch main $Repo $Clone
    }

    $forward = Start-Process kubectl -ArgumentList "-n", $Namespace, "port-forward", "svc/grand-admiral-api", "${Port}:8080" `
        -NoNewWindow -PassThru
    try {
        Start-Sleep -Seconds 3
        $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $log = Join-Path $logs "selfplay-$stamp.log"
        Write-Host "Self-play: $Rounds rounds (log: $log)"
        # No -quit: the sim plays, reports and uploads, then exits the editor itself.
        $unity = Start-Process $Unity -Wait -PassThru -ArgumentList @(
            "-batchmode", "-nographics", "-projectPath", "`"$Clone`"",
            "-executeMethod", "DifficultySimRunner.Run", "-selfPlay", "-selfPlayRounds", $Rounds, "-simSpeed", "6",
            "-gaServer", "http://localhost:$Port", "-gaCredentials", "`"$Credentials`"", "-logFile", "`"$log`"")
        Write-Host "Unity exited with $($unity.ExitCode)"
        Select-String -Path $log -Pattern "GRAND ADMIRAL v" | ForEach-Object { Write-Host $_.Line }
    } finally {
        if (-not $forward.HasExited) { Stop-Process -Id $forward.Id -Force }
    }
} while ($Loop)

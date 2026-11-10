#!/usr/bin/env bash
# Grand Admiral self-play: Grand Admiral vs Admiral in real Unity battles, headless. Its battle
# records and win rates go to the API as a trusted self-play install (see README, "Self-play").
set -uo pipefail
export HOME=/tmp/home
mkdir -p "$HOME" /workspace

echo "--- Fetching $GIT_REF from $GIT_REPO_URL ---"
git -C /workspace init --quiet
git -C /workspace remote add origin "$GIT_REPO_URL"
git -C /workspace fetch --quiet --depth 1 origin "$GIT_REF" || exit 1
git -C /workspace checkout --quiet FETCH_HEAD || exit 1

echo "--- Activating Unity license ---"
unity-editor -batchmode -quit -nographics -serial "$UNITY_SERIAL" -username "$UNITY_EMAIL" \
  -password "$UNITY_PASSWORD" -projectPath /workspace -logFile /dev/stdout || exit 1

echo "--- Self-play: ${SELFPLAY_ROUNDS} rounds ---"
# No -quit: the sim enters play mode, fights, reports, then exits the editor itself.
timeout 5h unity-editor -batchmode -nographics -projectPath /workspace \
  -executeMethod DifficultySimRunner.Run -selfPlay -selfPlayRounds "$SELFPLAY_ROUNDS" -simSpeed 6 \
  -gaServer "http://grand-admiral-api.grand-admiral.svc.cluster.local:8080" \
  -gaCredentials /secrets/selfplay/credentials.json -logFile /dev/stdout
code=$?

echo "--- Returning Unity license ---"
unity-editor -batchmode -quit -nographics -returnlicense -projectPath /workspace -logFile /dev/stdout \
  || echo "license return failed (non-fatal)"
exit $code

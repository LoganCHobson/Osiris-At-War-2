"""Type-check the Grand Admiral game code without Unity.

Compiles Assets/Scripts/AI/GrandAdmiral, Difficulty.cs and AIBattleCommander.cs (which the Grand
Admiral partial extends) against stubbed Unity/game signatures (tools/unity-typecheck/Stubs.cs).
The rest of the game keeps evolving faster than the stubs, so errors in upstream commander code
are reported but don't fail; any error in Grand Admiral code, Difficulty.cs, or a commander line
that calls into Grand Admiral does.

    python GrandAdmiral/tools/unity_typecheck.py
"""

import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
PROJECT = ROOT / "GrandAdmiral" / "tools" / "unity-typecheck"
COMMANDER = ROOT / "Assets" / "Scripts" / "AI" / "Tactical" / "AIBattleCommander.cs"
OURS = re.compile(r"GrandAdmiral|HeuristicPriority|GA[A-Z]\w+")


def main():
    result = subprocess.run(["dotnet", "build", str(PROJECT), "-nologo", "-v", "q"], capture_output=True, text=True)
    errors = sorted(set(re.findall(r"(\S+\.cs)\((\d+),\d+\): error (CS\d+: [^\[]+)", result.stdout)))
    commander = COMMANDER.read_text().splitlines()
    ours, upstream = [], []
    for path, line, message in errors:
        name = path.replace("\\", "/")
        mine = "/AI/GrandAdmiral/" in name or name.endswith("/AI/Difficulty.cs")
        if name.endswith("AIBattleCommander.cs"):
            mine = bool(OURS.search(commander[int(line) - 1]))
        (ours if mine else upstream).append(f"{name.split('/Scripts/')[-1]}:{line} {message.strip()}")
    if upstream:
        print(f"{len(upstream)} error(s) in upstream game code the stubs don't model (not failing)")
    if ours:
        print("Grand Admiral code doesn't type-check:\n  " + "\n  ".join(ours))
        sys.exit(1)
    if not errors and result.returncode != 0:
        print(result.stdout[-2000:])
        sys.exit(1)
    print("Grand Admiral code type-checks")


if __name__ == "__main__":
    main()

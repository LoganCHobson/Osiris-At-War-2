#!/usr/bin/env bash
# Every Grand Admiral test, in dependency order. CI runs exactly this.
#   GrandAdmiral/run-tests.sh   (set GA_PG_ADMIN_URL to include the Postgres integration test)
set -euo pipefail
cd "$(dirname "$0")"

echo "== test vectors (Python -> C#)"
python tools/make_test_vectors.py unity-tests/vectors

echo "== C# client core"
dotnet test unity-tests --nologo -v q

echo "== ML: model, records, deep checks, learning, gate, pipeline, poisoning, C# parity"
(cd ml && python -m pytest -q)

echo "== API security"
(cd server && python -m pytest -q)

echo "== game scripts type-check (Unity stubs)"
python tools/unity_typecheck.py

if [ -n "${GA_PG_ADMIN_URL:-}" ]; then
  echo "== Postgres roles + schema"
  python tools/pg_integration.py
fi
echo "ALL GRAND ADMIRAL TESTS PASSED"

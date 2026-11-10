"""Records written by the C# client (unity-tests RecordTests) must pass the API schema and the
trainer's deep check against the model that produced them. Run the C# tests first (run-tests.sh
does); skipped if they haven't been run."""

import pathlib

import pytest

from osiris_ml import dataset
from osiris_ml.model import Model
from osiris_ml.records import BattleRecord

ROOT = pathlib.Path(__file__).resolve().parents[2]
RECORDS = ROOT / "unity-tests" / "out" / "csharp_records.jsonl"
MODEL = ROOT / "unity-tests" / "vectors" / "model.bin"


@pytest.mark.skipif(not RECORDS.exists(), reason="run the C# tests first")
def test_csharp_records_are_accepted():
    model = Model.from_bytes(MODEL.read_bytes())
    lines = [line for line in RECORDS.read_text().splitlines() if line.strip()]
    assert len(lines) == 12
    for line in lines:
        record = BattleRecord.model_validate_json(line)
        assert dataset.deep_check(record, {model.version: model}) is None, record.battle_id

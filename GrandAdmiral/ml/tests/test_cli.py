"""Operator commands, end to end on SQLite with a throwaway key."""

import os

import pytest
from sqlalchemy import select

from osiris_ml import bundle, cli, spec, storage
from osiris_ml.model import Model


@pytest.fixture
def env(tmp_path, monkeypatch, signing_key):
    key_file = tmp_path / "key.pem"
    key_file.write_bytes(bundle.private_key_pem(signing_key))
    monkeypatch.setenv("GA_SIGNING_KEY_FILE", str(key_file))
    monkeypatch.setenv("GA_KEY_ID", "1")
    monkeypatch.setenv("GA_DB_URL", f"sqlite:///{tmp_path / 'ga.db'}")
    return tmp_path


def _models(env):
    engine = storage.engine_for(f"sqlite:///{env / 'ga.db'}")
    with engine.connect() as conn:
        return conn.execute(select(storage.models.c.version, storage.models.c.status, storage.models.c.bundle)).all()


def test_keygen_refuses_to_overwrite(tmp_path, capsys):
    out = tmp_path / "k.pem"
    cli.main(["keygen", "--out", str(out), "--key-id", "2"])
    assert "{ 2, (" in capsys.readouterr().out
    if os.name != "nt":  # Windows has no Unix permission bits; chmod there only sets read-only.
        assert (out.stat().st_mode & 0o777) == 0o600
    with pytest.raises(FileExistsError):
        cli.main(["keygen", "--out", str(out)])


def test_shipped_bundle_and_server_v1_are_identical(env, signing_key):
    shipped = env / "baseline.bytes"
    cli.main(["bootstrap", "--no-db", "--bundle-out", str(shipped)])
    cli.main(["bootstrap", "--create-schema", "--from-bundle", str(shipped)])
    [(version, status, data)] = _models(env)
    assert (version, status) == (1, "released")
    assert bytes(data) == shipped.read_bytes()


def test_bootstrap_refuses_foreign_bundle(env):
    foreign = env / "foreign.bytes"
    foreign.write_bytes(bundle.sign(Model(version=1).to_bytes(), bundle.generate_key(), 1))
    with pytest.raises(bundle.BundleError):
        cli.main(["bootstrap", "--create-schema", "--from-bundle", str(foreign)])


def test_bootstrap_only_once(env):
    cli.main(["bootstrap", "--create-schema", "--hidden", "8"])
    with pytest.raises(SystemExit):
        cli.main(["bootstrap", "--hidden", "8"])


def test_set_knob_releases_a_retuned_model(env, signing_key, capsys):
    cli.main(["bootstrap", "--create-schema", "--hidden", "8"])
    cli.main(["set-knob", "temperature=0.8", "exploration=5"])
    rows = {v: (s, d) for v, s, d in _models(env)}
    assert rows[2][0] == "released"
    tuned = Model.from_bytes(bundle.verify(bytes(rows[2][1]), {1: signing_key.public_key()}))
    assert tuned.knob("temperature") == pytest.approx(0.8)
    assert tuned.knob("exploration") == pytest.approx(0.2)  # clamped
    cli.main(["status"])
    assert "v2" in capsys.readouterr().out
    with pytest.raises(SystemExit):
        cli.main(["set-knob", "laser_power=9000"])


def test_round_command(env, capsys):
    cli.main(["bootstrap", "--create-schema", "--hidden", "8"])
    cli.main(["round"])
    assert "waiting for data" in capsys.readouterr().out


def test_revert_moves_forward(env, signing_key):
    cli.main(["bootstrap", "--create-schema", "--hidden", "8"])
    cli.main(["set-knob", "temperature=0.5"])
    cli.main(["revert", "--to", "1"])
    rows = {v: (s, d) for v, s, d in _models(env)}
    assert {v: s for v, (s, _) in rows.items()} == {1: "released", 2: "revoked", 3: "released"}
    restored = Model.from_bytes(bundle.verify(bytes(rows[3][1]), {1: signing_key.public_key()}))
    assert restored.knob("temperature") == pytest.approx(1.0)
    with pytest.raises(SystemExit):
        cli.main(["revert", "--to", "42"])

"""osiris-ml: operator commands for the Grand Admiral trainer.

    osiris-ml keygen --out ga-signing-1.pem --key-id 1      # once; prints the C# trusted-key line
    osiris-ml bootstrap                                     # release v1 (Admiral, distilled)
    osiris-ml round                                         # the nightly CronJob
    osiris-ml set-knob temperature=0.9 exploration=0.03     # retune live, no game update
    osiris-ml revert --to 4                                 # bring back v4's behaviour
    osiris-ml status

Configuration comes from the environment (set by k8s/trainer.yaml):
    GA_DB_URL             SQLAlchemy URL of the database (trainer role)
    GA_SIGNING_KEY_FILE   PEM private key (mounted read-only from a Secret)
    GA_SIGNING_KEY_PASS   optional passphrase
    GA_KEY_ID             which of the game's trusted keys this is
    GA_SELFPLAY_INSTALLS  comma-separated install ids of the in-cluster self-play runner
    GA_REQUIRE_ARENA      reserved; see gate.GateConfig.require_arena
"""

import argparse
import hashlib
import json
import logging
import os
import sys

from sqlalchemy import select

from . import bundle, distill, pipeline, spec, storage
from .model import Model


def _env(name, default=None, required=False):
    value = os.environ.get(name, default)
    if required and not value:
        sys.exit(f"{name} is not set")
    return value


def _signing():
    path = _env("GA_SIGNING_KEY_FILE", required=True)
    with open(path, "rb") as fh:
        key = bundle.load_private_key(fh.read(), _env("GA_SIGNING_KEY_PASS"))
    key_id = int(_env("GA_KEY_ID", "1"))
    return key, key_id, {key_id: key.public_key()}


def _engine(args):
    return storage.engine_for(args.db or _env("GA_DB_URL", required=True))


def cmd_keygen(args):
    key = bundle.generate_key()
    flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL
    with os.fdopen(os.open(args.out, flags, 0o600), "wb") as fh:
        fh.write(bundle.private_key_pem(key, args.passphrase))
    modulus, exponent = bundle.public_key_for_client(key)
    print(f"Private key written to {args.out} (mode 600). Keep it out of git and off the API.")
    print("Add this line to GATrustedKeys.cs:")
    print(f'    {{ {args.key_id}, ("{modulus}", "{exponent}") }},')


def cmd_bootstrap(args):
    """Release v1. Normally run twice with the same bundle: once locally to make the file that ships
    in the game, then in the cluster with --from-bundle so the server's v1 is that exact file."""
    key, key_id, public = _signing()
    if args.from_bundle:
        with open(args.from_bundle, "rb") as fh:
            signed = fh.read()
        model = Model.from_bytes(bundle.verify(signed, public))
    else:
        model = distill.baseline(n_hidden=args.hidden).rounded()
        model.version = 1
        signed = bundle.sign(model.to_bytes(), key, key_id)
    if args.bundle_out:
        with open(args.bundle_out, "wb") as fh:
            fh.write(signed)
        print(f"Baseline bundle written to {args.bundle_out}; ship it as Assets/Resources/GrandAdmiral/baseline.bytes")
    if args.no_db:
        return
    engine = _engine(args)
    if args.create_schema:
        storage.create_all(engine)
    with engine.begin() as conn:
        if conn.execute(select(storage.models.c.version)).first() is not None:
            sys.exit("models already exist; bootstrap only runs on an empty database")
        conn.execute(storage.models.insert().values(
            version=model.version, created_at=storage.now(), status="released", bundle=signed,
            sha256=hashlib.sha256(signed).hexdigest(),
            report=json.dumps({"bootstrap": distill.heuristic_agreement(model)})))
    print(f"Released v{model.version} (Admiral rules, distilled).")


def cmd_round(args):
    key, key_id, public = _signing()
    selfplay = tuple(i for i in (_env("GA_SELFPLAY_INSTALLS", "") or "").split(",") if i)
    cfg = pipeline.RoundConfig(selfplay_installs=selfplay,
                               min_client_version=(_env("GA_MIN_CLIENT_VERSION", "") or "").strip())
    cfg.gating.require_arena = _env("GA_REQUIRE_ARENA", "false").lower() == "true"
    summary = pipeline.run_round(_engine(args), key, key_id, public, cfg)
    print(json.dumps(summary, indent=2, default=str))


def cmd_set_knob(args):
    key, key_id, public = _signing()
    engine = _engine(args)
    with engine.begin() as conn:
        models = pipeline.load_released(conn, public, statuses=("released",))
        current = models[max(models)]
        tuned = current.copy()
        for item in args.knobs:
            name, _, value = item.partition("=")
            if name not in spec.KNOB_IDS:
                sys.exit(f"unknown knob {name}; known: {', '.join(spec.KNOB_IDS)}")
            kid = spec.KNOB_IDS[name]
            tuned.knobs[kid] = spec.clamp_knob(kid, float(value))
        latest = max(v for (v,) in conn.execute(select(storage.models.c.version)))
        tuned.version = latest + 1
        pipeline.publish(conn, tuned, "released", {"knobs_from": current.version, "set": args.knobs}, key, key_id)
    print(f"Released v{tuned.version} with {args.knobs}")


def cmd_revert(args):
    """Release an earlier model's weights (and knobs) as the newest version, and revoke the current
    one. Clients never go back a version number, so a rollback has to move forward."""
    key, key_id, public = _signing()
    engine = _engine(args)
    with engine.begin() as conn:
        models = pipeline.load_released(conn, public)
        if args.to not in models:
            sys.exit(f"v{args.to} isn't a released or revoked model")
        current = max(pipeline.current_versions(conn))
        restored = models[args.to].copy()
        restored.version = max(v for (v,) in conn.execute(select(storage.models.c.version))) + 1
        conn.execute(storage.models.update().where(storage.models.c.version == current).values(status="revoked"))
        pipeline.publish(conn, restored, "released", {"reverted_from": current, "restores": args.to, "by": "operator"},
                         key, key_id)
    print(f"Revoked v{current}; released v{restored.version} = v{args.to}")


def cmd_status(args):
    with _engine(args).connect() as conn:
        for version, status, created, report in conn.execute(
                select(storage.models.c.version, storage.models.c.status, storage.models.c.created_at,
                       storage.models.c.report).order_by(storage.models.c.version)):
            print(f"v{version:<5} {status:<9} {created:%Y-%m-%d %H:%M}  {(report or '')[:120]}")


def main(argv=None):
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s %(message)s")
    parser = argparse.ArgumentParser(prog="osiris-ml")
    parser.add_argument("--db", help="database URL (default: $GA_DB_URL)")
    sub = parser.add_subparsers(dest="command", required=True)

    p = sub.add_parser("keygen")
    p.add_argument("--out", required=True)
    p.add_argument("--key-id", type=int, default=1)
    p.add_argument("--passphrase")
    p.set_defaults(func=cmd_keygen)

    p = sub.add_parser("bootstrap")
    p.add_argument("--hidden", type=int, default=64)
    p.add_argument("--bundle-out")
    p.add_argument("--from-bundle", help="release this signed bundle instead of building one")
    p.add_argument("--no-db", action="store_true")
    p.add_argument("--create-schema", action="store_true", help="local dev / SQLite only")
    p.set_defaults(func=cmd_bootstrap)

    sub.add_parser("round").set_defaults(func=cmd_round)

    p = sub.add_parser("set-knob")
    p.add_argument("knobs", nargs="+", metavar="name=value")
    p.set_defaults(func=cmd_set_knob)

    p = sub.add_parser("revert")
    p.add_argument("--to", type=int, required=True, help="version whose weights to bring back")
    p.set_defaults(func=cmd_revert)

    sub.add_parser("status").set_defaults(func=cmd_status)

    args = parser.parse_args(argv)
    args.func(args)


if __name__ == "__main__":
    main()

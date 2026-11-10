"""Writes the fixtures the C# tests check the game client against, so the Python trainer and the
C# client provably agree on the model format, the maths, the policy and the signatures.

    python GrandAdmiral/tools/make_test_vectors.py GrandAdmiral/unity-tests/vectors

Uses a throwaway signing key generated on the spot; it's only ever trusted by the tests.
"""

import json
import pathlib
import sys

import numpy as np

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1] / "ml"))

from osiris_ml import bundle, policy, spec  # noqa: E402
from osiris_ml.model import Model  # noqa: E402


def main(out):
    out = pathlib.Path(out)
    out.mkdir(parents=True, exist_ok=True)
    rng = np.random.default_rng(2026)

    model = Model(n_hidden=16, version=7, seed=5, knobs={"temperature": 0.7, "exploration": 0.1})
    model.in_mean = rng.normal(0, 0.2, spec.N_FEATURES)
    model.in_std = rng.uniform(0.5, 2.0, spec.N_FEATURES)
    model.w2 = rng.normal(0, 0.8, model.w2.shape)
    model.b2 = rng.normal(0, 0.5, spec.N_OUT)
    model = model.rounded()

    key = bundle.generate_key()
    signed = bundle.sign(model.to_bytes(), key, 3)
    tampered = bytearray(signed)
    tampered[40] ^= 0x10
    other_key = bundle.generate_key()

    (out / "model.bin").write_bytes(model.to_bytes())
    (out / "bundle.bin").write_bytes(signed)
    (out / "bundle_tampered.bin").write_bytes(bytes(tampered))
    (out / "bundle_other_key.bin").write_bytes(bundle.sign(model.to_bytes(), other_key, 3))
    modulus, exponent = bundle.public_key_for_client(key)

    temperature, epsilon = model.knob("temperature"), model.knob("exploration")
    cases = []
    for i in range(25):
        x = np.round(rng.normal(0, 1.2, spec.N_FEATURES), 4)
        out_vec = model.forward(x[None])[0][0]
        stance_mask = [True, True, bool(i % 3)]
        n_contacts = 2 + i % 9
        contacts = np.round(rng.normal(0, 1, (n_contacts, spec.N_CONTACT)), 4)
        decided = policy.decide(out_vec, stance_mask=stance_mask, commit_mask=[True] * 3, retreat_open=True,
                                contacts=contacts, temperature=temperature, epsilon=epsilon)
        cases.append({
            "x": x.tolist(),
            "output": out_vec.tolist(),
            "stance_mask": stance_mask,
            "stance": decided["stance"].tolist(),
            "commit": decided["commit"].tolist(),
            "retreat": float(decided["retreat"][1]),
            "contacts": contacts.tolist(),
            "target": decided["target"].tolist(),
        })

    sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1] / "server"))
    from app import security  # noqa: E402
    server_key = bytes(range(32))
    install = "6f1c1d6e-1111-4222-8333-444455556666"
    token = security.token_for(server_key, install)
    body = b'{"hello":"world"}'
    hmac_case = {"token": token, "method": "POST", "path": "/v1/battles", "time": 1700000000,
                 "body": body.decode(), "signature": security.request_signature(token, "POST", "/v1/battles",
                                                                                "1700000000", body)}

    (out / "vectors.json").write_text(json.dumps({
        "hmac": hmac_case,
        "key_id": 3, "modulus": modulus, "exponent": exponent,
        "version": model.version, "hidden": model.n_hidden,
        "temperature": temperature, "exploration": epsilon,
        "cases": cases,
    }, indent=1))
    print(f"wrote {len(cases)} cases to {out}")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "unity-tests/vectors")

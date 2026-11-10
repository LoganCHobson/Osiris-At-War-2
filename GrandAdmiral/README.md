# Grand Admiral: Osiris at War's learned difficulty

Grand Admiral is a fourth difficulty above Admiral. It fights like Admiral on day one. Then it gets
better: every battle anyone fights against it can be uploaded (anonymously, opt-out in Settings),
the cluster trains a better model each night, and every player's game downloads it in the
background. **New models are data, not code, so they reach players without a game update.** The
AI also adapts to each player locally after every battle, and it trains against itself in the
cluster.

```
 Player's game                        Homelab cluster (namespace grand-admiral)
 ─────────────                        ─────────────────────────────────────────────────────────────
 battle ends ──record──► outbox ──HTTPS + HMAC──► API ──► Postgres ◄── trainer CronJob (nightly)
                                         (no signing key,           │   check · train · gate · sign
 next launch ◄──signed model bundle──────  can't write models)      │
   verify RSA signature with the          ▲                         ▼
   public key baked into the build        └── self-play CronJob: Unity GA-vs-Admiral battles
```

| Folder | What it is |
|---|---|
| `../Assets/Scripts/AI/GrandAdmiral/` | The game side, plus small hooks in `AIBattleCommander` (now `partial`), `Difficulty`, `GameManager`, `DifficultySim(Runner)`, the main menu, settings and save panel. |
| `ml/` | `osiris_ml`: the shared spec, model format, trainer, promotion gate, deep checks, toy simulator, `osiris-ml` CLI. |
| `server/` | The ingest + model API (FastAPI). |
| `k8s/` | Everything the cluster runs: Postgres, API, ingress, network policies, trainer, self-play. |
| `unity-tests/` | xUnit tests for the C# client core, checked against fixtures from the Python side. |
| `tools/` | Test-vector generator, Postgres integration test, Unity type-check stubs. |
| [`SECURITY.md`](SECURITY.md) | Threat model and every defence, with the test that proves it. |

## How the AI works

The existing `AIBattleCommander` still does all the flying: positioning, formation, drop zones,
aiming. Every 3 s (a server-tunable knob), Grand Admiral's network makes the four calls that win
or lose fights:

| Head | Choice | Replaces |
|---|---|---|
| stance | engage / press / fall back | fixed force-ratio thresholds |
| retreat | stay / retreat | fixed 0.35 retreat ratio |
| commit | hold / match / surge reserves | "deploy just enough" |
| target | which enemy to focus | the hand-written priority formula (also an input, so it starts from it) |

It sees 24 battle-state numbers (force ratios, losses, health, DPS and range ratios, reserves,
composition, defenses, time, current stance) and 9 numbers per enemy (`ml/osiris_ml/spec.py`).
The network is one tanh hidden layer of 32 units plus a value head: 1.4k weights, about 3 KB,
microseconds per decision in plain C#.

**Safety rails outside the network.** It can never retreat from a fight it's winning (outlook
above 1.2), always retreats from a rout (below 0.12), and can't fall back with nothing to fall back
to. All knobs are clamped by the client, whatever a model file says.

**v1 is Admiral.** The baseline is the Admiral rules distilled into the network (99.7% agreement),
so Grand Admiral starts no worse than the best hand-written AI.

**How it learns (globally).** Each logged decision records its state, the action and the
probability it was taken with. The trainer runs offline actor-critic: a ridge-regression critic,
GAE(λ) advantages and a PPO-clipped objective with an L2 anchor to the released weights. Each
release is a small, conservative step. A candidate is released only if it passes the gate
(`ml/osiris_ml/gate.py`):

1. enough battles and distinct installs;
2. a significant improvement on held-out battles (bootstrap lower bound > 0);
3. a small step (per-head KL from the current model ≤ 0.05);
4. sane on canary states (presses a 3:1 advantage, escapes a 1:6 rout);
5. not worse in self-play, otherwise it's rolled back automatically.

On the toy simulator, five rounds took the AI from 0.27 to 0.34 mean battle return. The gate
rejected the one round that would have made it worse.

**How it learns (locally).** After every Grand Admiral battle against a player, the client nudges
the stance, retreat and reinforcement heads toward what worked against *that* player (REINFORCE,
with the value head as baseline). The offset is capped so it can move a logit by at most
0.6 × temperature, decays every battle, and resets when a new global model arrives.

**Self-play.** A nightly CronJob runs the game's existing `DifficultySim` headless in Unity:
Grand Admiral vs Admiral, exploring more than it would against players. It uploads those battles
as trusted training data and reports win rates. If a release wins less often than the one before
it, the trainer re-releases the previous weights under a new version.

## No game update needed

What ships once, in one game update: the code in `unity/`, the public signing key, and the baseline
model. After that, everything the server can change is data inside a signed bundle:

* weights for all four heads and the critic;
* knobs: decision interval, reinforcement delay, strategic think rate, attack margin, retreat
  judgement, decision tick, exploration, temperature, local learning rate (each clamped);
* the version number (the client never accepts a lower one).

`GrandAdmiralService` checks 5 s after launch and every 6 h after that. It's one small GET; a
download is about 3 KB. A new model takes effect from the next battle. It never blocks the game
and fails silently offline. Uploads queue on disk (at most 25) and send in the background.

A game update is only needed to change the *shape* of the network (bump `FEATURE_SCHEMA`; old
builds keep their last compatible model), to add a new decision type, or to rotate signing keys.

## Setup

### 1. Game

The game code is already in place under `Assets/Scripts/AI/GrandAdmiral/`. Open the project in
Unity and let it import (it generates the `.meta` files). CI type-checks the game scripts against
stubbed Unity signatures (`tools/unity-typecheck`), but **it has not been compiled or played inside
the Unity editor yet**; see "Not verified yet" below.

### 2. Signing key (on your own machine, never in CI)

```bash
pip install "./GrandAdmiral/ml[train]"
osiris-ml keygen --out ga-signing-1.pem --key-id 1      # add --passphrase for an encrypted key
```

Paste the line it prints into `GATrustedKeys.cs`. Keep `ga-signing-1.pem` somewhere safe and
offline (a password manager), plus one backup. Whoever holds this key can ship AI behaviour to
every player. If it's lost, no new models can be released until a game update adds a new key.

### 3. Baseline model (ships in the game)

```bash
GA_SIGNING_KEY_FILE=ga-signing-1.pem GA_KEY_ID=1 \
  osiris-ml bootstrap --no-db --bundle-out Assets/Resources/GrandAdmiral/baseline.bytes
```

Commit `baseline.bytes` and the trusted key, and build the game. That's the one update.

### 4. Cluster secrets

```bash
kubectl apply -f GrandAdmiral/k8s/namespace.yaml
ADMIN=$(openssl rand -hex 24); API=$(openssl rand -hex 24); TRAINER=$(openssl rand -hex 24)
kubectl -n grand-admiral create secret generic grand-admiral-db \
  --from-literal=admin-password=$ADMIN --from-literal=api-password=$API --from-literal=trainer-password=$TRAINER
kubectl -n grand-admiral create secret generic grand-admiral-api \
  --from-literal=db-url="postgresql+psycopg://ga_api:$API@grand-admiral-db:5432/grand_admiral" \
  --from-literal=server-key="$(openssl rand -base64 32)"
kubectl -n grand-admiral create secret generic grand-admiral-trainer \
  --from-literal=db-url="postgresql+psycopg://ga_trainer:$TRAINER@grand-admiral-db:5432/grand_admiral" \
  --from-file=signing-key.pem=ga-signing-1.pem --from-literal=key-id=1
kubectl -n grand-admiral create configmap grand-admiral-baseline \
  --from-file=baseline.bytes=Assets/Resources/GrandAdmiral/baseline.bytes
kubectl get secret ghcr-pull-secret -n default -o yaml | sed 's/namespace: default/namespace: grand-admiral/' | kubectl apply -f -
```

### 5. Deploy

A push to `main` that touches `GrandAdmiral/` or `Assets/Scripts/AI/` runs `.github/workflows/grand-admiral.yml`. It runs
every test on a GitHub-hosted runner, builds both images to GHCR, then deploys from the
`homelab-k8s` runner. Add required reviewers to the `grand-admiral` environment in the repo
settings if you want to approve each deploy. By hand:

```bash
kubectl apply -k GrandAdmiral/k8s
kubectl -n grand-admiral apply -f GrandAdmiral/k8s/bootstrap-job.yaml    # once: releases v1
```

Then point `ga.solarnebulae.com` at the ingress. Check that `ingressClassName` and the
`cert-manager.io/cluster-issuer` annotation in `k8s/ingress.yaml` match the homelab's names, and
that the API network policy's ingress-controller namespace (`ingress-nginx`) is right.

### 6. Self-play (optional, recommended)

```bash
kubectl -n grand-admiral exec deploy/grand-admiral-api -- python -m app.admin selfplay-credentials > selfplay.json
kubectl -n grand-admiral create secret generic grand-admiral-selfplay --from-file=credentials.json=selfplay.json
kubectl -n grand-admiral create secret generic unity-license \
  --from-literal=serial=... --from-literal=email=... --from-literal=password=...
```

Put the `install_id` from `selfplay.json` into `k8s/config.yaml`, set `suspend: false` in
`k8s/selfplay.yaml`, commit and push.

## Operating it

The trainer's environment (database role, signing key) exists only in the trainer pod, so admin
commands run as one-off Jobs from the trainer CronJob's template:

```bash
kubectl -n grand-admiral create job --from=cronjob/grand-admiral-trainer round-now   # a training round, now
kubectl -n grand-admiral logs job/round-now -f                                     # gate report: why it did or didn't release
```

For `osiris-ml status`, `set-knob` and `revert`, copy the CronJob's pod spec into a Job with `args`
set to the command. For example `args: [set-knob, temperature=0.9]` releases the current weights
under a new version with new knob values, which makes Grand Admiral more or less exploratory, or
quicker or slower to react, without retraining. `args: [revert, --to, "4"]` brings back v4's
behaviour as a new version.

* **Rollbacks always move forward.** Clients never accept a lower version, so a revert re-releases
  old weights under a new number. Self-play does this by itself when a release loses ground.
* **Rotate the signing key:** generate key 2, add it to `GATrustedKeys` next to key 1 in a game
  update, switch the trainer secret to key 2 once players have that update, and drop key 1 in a
  later update.
* **Data:** battles are deleted after 90 days. Nothing stored identifies a player (see SECURITY.md).

## Tests

```bash
GrandAdmiral/run-tests.sh   # GA_PG_ADMIN_URL=... adds the Postgres integration test
```

| Suite | Count | Covers |
|---|---|---|
| `unity-tests` (C#) | 44 | Model parsing, forward pass and policy match Python to 1e-9. Signature checks: tampering, wrong keys, downgrades, weak keys. Proof-of-work and HMAC match the server. Local adaptation stays within its cap. Records are well formed. |
| `ml/tests` | 87 | Model and bundle formats. 26 malformed-record cases. Deep checks catch forged probabilities and flipped actions. Gradients match finite differences. Learning improves the toy AI over rounds. The gate rejects no-ops, cowards, big jumps, thin evidence and self-play regressions. The full pipeline runs on SQLite, including forgers being blocked, an outcome-poisoner who can't make the AI worse, retention and rollback. C#-written records pass the trainer. CLI commands (bootstrap, set-knob, revert). |
| `server/tests` | 33 | Registration, proof-of-work, replay and expiry. Signatures, clock skew, blocked installs, size limits, bad records, unknown versions. Per-hour and per-network limits, flood shedding, spoofed `X-Forwarded-For`. No IP addresses stored. Only released models served. Self-play-only endpoints. |
| `tools/pg_integration.py` | 1 | Real Postgres 16, real schema and roles: API and trainer flows work, and 11 privilege-escalation attempts are refused. |
| `tools/unity_typecheck.py` | 1 | The Grand Admiral code, `Difficulty.cs` and the commander hooks compile against stubbed Unity and game signatures. |

## Not verified yet

These need a person with Unity and the cluster; the code is written but hasn't been run there:

* **In the Unity editor:** nothing here has been compiled by Unity itself or played. The
  type-check compiles the real files against stubbed Unity/game signatures, which catches most
  mistakes but not all. Watch for `.meta` files being generated, and play a Grand Admiral battle
  to check that decisions look sensible (`AIDebugOverlay` still works).
* **Self-play in the cluster:** `k8s/selfplay.sh` assumes the `unity-ci-build` image's
  `unity-editor` runs as a non-root user with `HOME=/tmp/home`, and that `DifficultySimRunner.Run`
  runs to completion in batch mode without `-quit`. Do a manual run first, with `suspend: true`
  left on and `kubectl create job --from=cronjob/grand-admiral-selfplay`.
* **Ingress names:** class, ClusterIssuer and controller namespace are educated guesses (see step 5).
* **Learning in the real game:** the learning loop is proven on the toy simulator, which shares
  the game's decisions and features but not its physics. Expect the first real releases to need
  a few hundred battles from 20+ installs before the gate lets anything through. That's by design.

# Grand Admiral: Osiris at War's learned difficulty

Grand Admiral is a fourth difficulty above Admiral. It fights like Admiral on day one. Then it gets
better: every battle anyone fights against it can be uploaded (anonymously, opt-out in Settings),
the cluster trains a better model each night, and every player's game downloads it in the
background. **New models are data, not code, so they reach players without a game update.** The
AI also adapts to each player locally after every battle, and it trains against itself in the
cluster.

```
 Player's game                        Homelab cluster (namespace oawga-machine-learning)
 ─────────────                        ─────────────────────────────────────────────────────────────
 battle ends ──record──► outbox ──HTTPS + HMAC──► API ──► Postgres ◄── trainer CronJob (nightly)
                                         (no signing key,      │    ▲   check · train · gate · sign
 next launch ◄──signed model bundle──────  can't write models)  │    │   → trial → release
   verify RSA signature with the          ▲                    ▼    │
   public key baked into the build        └── self-play farm   metrics exporter ──► Prometheus ──► Grafana
                                              (headless Linux players, GA vs Admiral, round after round)
```

| Folder | What it is |
|---|---|
| `../Assets/Scripts/AI/GrandAdmiral/` | The game side, plus small hooks in `AIBattleCommander` (now `partial`), `Difficulty`, `GameManager`, `DifficultySim(Runner)`, the main menu, settings and save panel. |
| `ml/` | `osiris_ml`: the shared spec, model format, trainer, promotion gate, deep checks, toy simulator, `osiris-ml` CLI. |
| `server/` | The ingest + model API (FastAPI). |
| `k8s/` | Everything the cluster runs: Postgres, API, HTTP route, network policies, trainer, self-play. |
| `unity-tests/` | xUnit tests for the C# client core, checked against fixtures from the Python side. |
| `tools/` | Test-vector generator, Postgres integration test, Unity type-check stubs, the self-play build and home-PC scripts, the dashboard generator. |
| [`SECURITY.md`](SECURITY.md) | Threat model and every defence, with the test that proves it. |

## How the AI works

The existing `AIBattleCommander` still does all the flying: positioning, formation, drop zones,
aiming. Every 3 s (a server-tunable knob), Grand Admiral's network makes the eight calls that win
or lose fights:

| Head | Choice | Replaces |
|---|---|---|
| stance | engage / press / fall back | fixed force-ratio thresholds |
| retreat | stay / retreat | fixed 0.35 retreat ratio |
| commit | hold / match / surge reserves | "deploy just enough" |
| target | which enemy to focus | the hand-written priority formula (also an input, so it starts from it) |
| formation | standard / tight / loose | fixed spacing (tight = overlapping point defence, loose = clear lines of fire) |
| escort | line / screen / hunt | escorts always in the line (screen = guard the capitals, hunt = chase strike craft) |
| manoeuvre | line / flank / envelop | one battle line (a detachment swings round one wing, or both) |
| tempo | steady / rotate / pulse | hold position (rotate = drained ships drop back to recharge shields, pulse = the whole line steps back) |

**Grand Admiral commands Avanti ships, never Human ones.** Its battle records don't say which navy it
had, so a battle fought with Human ships would poison its training. Every AI faction in the campaign
flies Avanti ships (the Avanti Empire, Ashcrown, Kesh, Severed Hand, Thessary, Vorrak and the neutral
fleets), so on Grand Admiral they're all commanded by it; only the player's Titans of Osiris fly Human
ships (`Faction.CanBeGrandAdmiral` checks the roster, not the name). The sims put it on Avanti every time.

It sees 53 battle-state numbers (`ml/osiris_ml/spec.py`): force ratios, losses, health, DPS and
range ratios, reserves, composition, defences and time; shields and how fast they're draining;
missiles in flight and idle point defence; enemy strike craft and bombers on our capitals; guns
facing their target or blocked; asteroid cover; enemy waves; the flagship's health and the fire on
it; kiting, closing rate, spread, speed and the damage exchange; and the orders standing now. Plus 9
numbers per enemy for targeting. The network is one tanh hidden layer of 64 units plus a value
head: about 5.5k weights, 22 KB, microseconds per decision in plain C#.

**Safety rails outside the network.** It can never retreat from a fight it's winning (outlook
above 1.2), always retreats from a rout (below 0.12), and can't fall back with nothing to fall back
to. All knobs are clamped by the client, whatever a model file says.

**v1 is the playbook.** The baseline is the Admiral rules, plus playbook rules for the four newer
orders (`distill.PLAYBOOK`: tight under missiles or bombers, screen when bombers are on the
capitals, envelop a compact enemy you outnumber 2:1, pulse back when shields collapse in an even
fight...), distilled into the network: 99.5% agreement on stance and retreat, 96-98% on the rest.

**How it learns (globally).** Each logged decision records its state, the action and the
probability it was taken with. The trainer runs offline actor-critic: a ridge-regression critic,
GAE(λ) advantages and a PPO-clipped objective with an L2 anchor to the released weights. Each
release is a small, conservative step. A candidate is released only if it passes the gate
(`ml/osiris_ml/gate.py`):

1. enough battles, and either 20+ distinct player installs (so no one player can steer it) or enough
   trusted self-play battles;
2. a significant improvement on held-out battles (bootstrap lower bound > 0), estimated with one
   importance ratio per decision across every head that changed;
3. a small step (per-head KL from the current model ≤ 0.05);
4. sane on canary states (presses a 3:1 advantage, escapes a 1:6 rout);
5. **proven in self-play.** A candidate that passes 1-4 becomes a *trial*: signed, but served only
   to self-play. The next round promotes it once self-play has played it 40+ games and it wins at
   least as often against Admiral as the release does (within 0.05), and fails it otherwise.
   Releases that later lose ground in self-play are still rolled back automatically.

**Known issue.** With eight decisions the offline estimate (check 2) is noisy: on the toy simulator a
round's real gain is about the size of the evaluation noise, and the gate both passed a worse model
and rejected better ones. That's why check 5 is on. Two toy learning tests are marked as expected
failures until the trainer and the toy are improved.

**How it learns (locally).** After every Grand Admiral battle against a player, the client nudges
the stance, retreat and reinforcement heads toward what worked against *that* player (REINFORCE,
with the value head as baseline). The offset is capped so it can move a logit by at most
0.6 × temperature, decays every battle, and resets when a new global model arrives.

**Self-play.** A farm of headless Linux players (`Assets/Editor/SelfPlayBuild.cs`, a Unity
dedicated-server build, so no license per copy) plays Grand Admiral (Avanti) against Admiral round
after round. Fleets are bought with the same credits (1,500-3,500, fresh every battle) from each
side's roster, on fresh asteroid layouts every run, exploring more than against players. Every
run asks the API what to play (a trial, else the release), uploads its battles as trusted training
data and reports its win rate. The farm runs at the lowest priority with preemption off: it only
ever uses spare capacity. A home PC can join with `tools/selfplay-home.ps1`.

## No game update needed

What ships once, in one game update: the code in `Assets/Scripts/AI/GrandAdmiral/`, the public signing key, and the baseline
model. After that, everything the server can change is data inside a signed bundle:

* weights for all eight heads and the critic;
* knobs: decision interval, reinforcement delay, strategic think rate, attack margin, retreat
  judgement, decision tick, exploration, temperature, local learning rate, and how the newer orders
  are carried out (tight and loose spacing, flank share and width, the shield thresholds for
  rotating out and back, pulse depth, screen radius), each clamped;
* the version number (the client never accepts a lower one).

`GrandAdmiralService` checks 5 s after launch and every 6 h after that. It's one small GET; a
download is about 22 KB. A new model takes effect from the next battle. It never blocks the game
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
ADMIN=$(openssl rand -hex 24); API=$(openssl rand -hex 24); TRAINER=$(openssl rand -hex 24); METRICS=$(openssl rand -hex 24)
kubectl -n oawga-machine-learning create secret generic grand-admiral-db \
  --from-literal=admin-password=$ADMIN --from-literal=api-password=$API --from-literal=trainer-password=$TRAINER \
  --from-literal=metrics-password=$METRICS
kubectl -n oawga-machine-learning create secret generic grand-admiral-metrics \
  --from-literal=db-url="postgresql+psycopg://ga_metrics:$METRICS@grand-admiral-db:5432/grand_admiral"
kubectl -n oawga-machine-learning create secret generic grand-admiral-api \
  --from-literal=db-url="postgresql+psycopg://ga_api:$API@grand-admiral-db:5432/grand_admiral" \
  --from-literal=server-key="$(openssl rand -base64 32)"
kubectl -n oawga-machine-learning create secret generic grand-admiral-trainer \
  --from-literal=db-url="postgresql+psycopg://ga_trainer:$TRAINER@grand-admiral-db:5432/grand_admiral" \
  --from-file=signing-key.pem=ga-signing-1.pem --from-literal=key-id=1
kubectl -n oawga-machine-learning create configmap grand-admiral-baseline \
  --from-file=baseline.bytes=Assets/Resources/GrandAdmiral/baseline.bytes
kubectl get secret ghcr-pull-secret -n default -o yaml | sed 's/namespace: default/namespace: oawga-machine-learning/' | kubectl apply -f -
```

### 5. Deploy

A push to `main` that touches `GrandAdmiral/` or `Assets/Scripts/AI/` runs `.github/workflows/grand-admiral.yml`. It runs
every test on a GitHub-hosted runner, builds both images to GHCR, then deploys from the
`homelab-k8s` runner. Add required reviewers to the `grand-admiral` environment in the repo
settings if you want to approve each deploy. By hand:

```bash
kubectl apply -k GrandAdmiral/k8s
kubectl -n oawga-machine-learning apply -f GrandAdmiral/k8s/bootstrap-job.yaml    # once: releases v1
```

The API is routed by `k8s/httproute.yaml` through the homelab's shared Gateway (`nginx-gateway/home`),
which terminates TLS with the `*.solarnebulae.com` wildcard certificate. `ga.solarnebulae.com` needs a DNS
record pointing at the gateway: AdGuard for the LAN, plus a public route (for example a Cloudflare
Tunnel) before players outside the LAN can reach it.

### 6. Self-play

```bash
kubectl -n oawga-machine-learning exec deploy/grand-admiral-api -- python -m app.admin selfplay-credentials > selfplay.json
kubectl -n oawga-machine-learning create secret generic grand-admiral-selfplay --from-file=credentials.json=selfplay.json
powershell -ExecutionPolicy Bypass -File GrandAdmiral/tools/build-selfplay.ps1   # build the player, ship it, restart the farm
```

Put the `install_id` from `selfplay.json` into `selfplay-installs` in `k8s/config.yaml` (comma
separated, together with any home PC's) and apply. Scale the farm with
`kubectl -n oawga-machine-learning scale deploy/grand-admiral-selfplay --replicas N`. Re-run
`build-selfplay.ps1` after any game change self-play should train on.

### 7. Dashboard

`k8s/grafana-dashboard.yaml` (generated by `tools/make_dashboard.py`) appears in Grafana as
**Grand Admiral**: battles per hour and in total, real and game time per battle, results and fleet
left, the farm's CPU and memory, self-play win rate and games per model, the gate's verdict and
step sizes, models and battles by status, how often each order is given, and player numbers.
Prometheus scrapes `grand-admiral-metrics` (a read-only exporter, `server/app/metrics.py`); the
farm's per-battle `GASTAT` log lines come from Loki.

## Operating it

The trainer's environment (database role, signing key) exists only in the trainer pod, so admin
commands run as one-off Jobs from the trainer CronJob's template:

```bash
kubectl -n oawga-machine-learning create job --from=cronjob/grand-admiral-trainer round-now   # a training round, now
kubectl -n oawga-machine-learning logs job/round-now -f                                     # gate report: why it did or didn't release
```

For `osiris-ml status`, `set-knob` and `revert`, copy the CronJob's pod spec into a Job with `args`
set to the command. For example `args: [set-knob, temperature=0.9]` releases the current weights
under a new version with new knob values, which makes Grand Admiral more or less exploratory, or
quicker or slower to react, without retraining. `args: [revert, --to, "4"]` brings back v4's
behaviour as a new version.

* **Rollbacks always move forward.** Clients never accept a lower version, so a revert re-releases
  old weights under a new number. Self-play does this by itself when a release loses ground.
* **After a rebalance** (ship stats, doctrine multipliers, combat rules): the model reads ratios
  (power, DPS, range, losses), not raw stats, so it keeps working. What goes stale is the battle
  data it learns from. Bump the game version in Player Settings for the release, then set
  `min-client-version` in `k8s/config.yaml` to that version and apply it. The trainer then ignores
  older battles: it waits for enough new-balance battles before it releases anything, and judges
  candidates only on them. For a drastic rebalance, also `revert --to 1` so it restarts from the
  Admiral rules instead of from habits learned under the old balance.
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
| `ml/tests` | 95 | Model and bundle formats. 26 malformed-record cases. Deep checks catch forged probabilities and flipped actions. Gradients match finite differences. Learning improves the toy AI over rounds. The gate rejects no-ops, cowards, big jumps, thin evidence and self-play regressions. The full pipeline runs on SQLite, including forgers being blocked, an outcome-poisoner who can't make the AI worse, retention, rollback, the rebalance floor and the trial stage (trial, wait, promote, fail). C#-written records pass the trainer. CLI commands (bootstrap, set-knob, revert). |
| `server/tests` | 36 | Registration, proof-of-work, replay and expiry. Signatures, clock skew, blocked installs, size limits, bad records, unknown versions. Per-hour and per-network limits, flood shedding, spoofed `X-Forwarded-For`. No IP addresses stored. Only released models served; trials only to self-play. Self-play-only endpoints. The metrics exporter. |
| `tools/pg_integration.py` | 1 | Real Postgres 16, real schema and roles: API and trainer flows work, and 15 privilege-escalation attempts are refused (including the metrics role). |
| `tools/unity_typecheck.py` | 1 | The Grand Admiral code, `Difficulty.cs` and the commander hooks compile against stubbed Unity and game signatures. |

## Not verified yet

* **The farm in the cluster:** the first cluster run is the real test of the Linux self-play build
  (the `ubuntu:24.04` image should have what Unity's server player needs; watch the pod logs for
  `GASTAT` lines).
* **Players outside the LAN:** `ga.solarnebulae.com` only resolves on the LAN today. Public play
  needs a public route (for example a Cloudflare Tunnel).
* **Learning in the real game:** see the known issue above. Self-play is the judge of every release.

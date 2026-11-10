# Grand Admiral security model

Grand Admiral takes input from strangers (battle uploads) and sends output to every player
(models). This document covers what an attacker could want, what stops them, and the test that
shows it.

## What we protect, in order

1. **Players' machines.** Nothing from the server may run code, or anything like it, on a player's PC.
2. **The AI everyone plays against.** Bad or malicious data must not make it worse, or weird.
3. **The cluster and the home network** behind it.
4. **Players' privacy.**

## 1. Players' machines: signed data only

* A model is a fixed binary layout of floats and clamped numbers (`GAModel.cs`). There is nothing
  executable in it, and no deserializer that could be tricked into making objects. The parser
  checks the magic number, version, schema, exact length, dimensions (hidden ≤ 256) and that every
  weight is finite. Tests: `ModelTests.*`, `test_rejects_malformed`, `test_rejects_nan_weights`.
* Every bundle is RSA-3072 / SHA-256 signed. The signature is domain-separated
  (`"OGAB-SIG-v1\0" + model`) and checked against public keys compiled into the game. The client
  refuses unknown key ids, keys under 3072 bits, any flipped bit and trailing bytes. Tests:
  `BundleTests.*` (60 random bit flips), `test_signature_is_domain_separated`.
* **Nobody on the network can sign.** Not the API server, not the CDN, not anyone in the middle.
  The private key exists only on the operator's machine and in the trainer pod, which accepts no
  connections and can reach only the database.
* No downgrades: a client never installs a lower version than it runs (`RefusesDowngrade`), so an
  old, weaker model can't be replayed at players.
* Every knob is clamped on the client, so even a validly signed model can only tune the AI within
  sane bounds (`KnobsAreClamped`).
* The client only talks HTTPS (except localhost and in-cluster `*.svc.cluster.local` for
  self-play), and it refuses URLs with credentials in them (`OnlyHttpsServers`). The proof of work
  a server can demand is capped at 26 bits, so a hostile server can't burn players' CPUs.

## 2. The AI: assume every upload might be a lie

Anyone can pull their own token out of their own game and sign whatever they like. Authentication
only gives a stable, costly-to-multiply identity per install. Content is judged on its own:

| Layer | Defence | Test |
|---|---|---|
| API | Strict schema: exact lengths, finite bounded numbers, valid probabilities, chosen action allowed, ordered times, end power ≤ start, no "won by retreating", only known model versions. Stored re-serialised, never as raw bytes. | `test_rejects_bad_records` (26 cases), `test_stored_payload_is_normalised` |
| Trainer | **Deep check:** recompute every logged action's probability under the model version the record claims. Invented decisions or confidence don't match. | `test_deep_check_rejects_fabricated_probabilities`, `..._flipped_actions` |
| Trainer | Installs with ≥ 50% failed uploads (after 10) are blocked; the API then refuses them. | `test_forger_is_rejected_and_blocked`, `test_honest_player_is_never_blocked` |
| Trainer | Contribution caps: each install gets at most 40 battles' weight and 2% of a round; each network at most 4%. 10,000 bot uploads count about as much as one player's. | `test_contribution_weights_cap_a_flooder` |
| Trainer | Conservative updates: PPO clipping plus an L2 anchor to the released weights. | `test_training_rounds_improve_the_toy_ai` |
| Gate | Held-out improvement must be significant, KL per head ≤ 0.05, canary states must still be right. A model that retreats from winning fights can't ship. | `test_gate_rejects_a_coward`, `..._a_big_jump`, `..._an_unchanged_model` |
| Gate | The hard case: an attacker who knows the model, logs *correct* probabilities and lies only about outcomes, sending 2,500 records. The released model is no worse. | `test_outcome_poisoner_cannot_make_the_ai_worse` |
| Self-play | Ground truth from real Unity battles the cluster ran itself. A release that loses ground is rolled back automatically. | `test_self_play_regression_rolls_back_forward` |

What's left: a large, patient Sybil attack (thousands of installs across thousands of networks,
each paying the proof of work) could bias training within the per-release KL limit. Self-play
rollback is the backstop, and `GA_REQUIRE_ARENA` is reserved for making self-play a hard gate.

## 3. The cluster

* **Least privilege in Postgres.** The internet-facing API role can insert installs, battles and
  arena results and read models. It can't update, delete or write models. The trainer can't
  rewrite payloads or model bundles. Tested on real Postgres: `tools/pg_integration.py`
  (11 refused escalations).
* **Network policies:** default deny. The API is reachable only from the ingress controller and
  self-play, and talks only to the DB. The DB accepts only the API and the trainer, with no egress.
  The trainer has no ingress and talks only to the DB. Self-play gets the API plus public HTTPS
  (GitHub, Unity licensing); private ranges are excluded, so it can't reach the home network.
* **Pods:** namespace enforces Pod Security `restricted`. Everything is non-root with
  `allowPrivilegeEscalation: false`, all capabilities dropped, seccomp `RuntimeDefault`, read-only
  root filesystems (except Unity), no service account tokens and memory limits. Secrets are mounted
  read-only with mode 0400.
* **The API itself:** body cap of 512 KB (with `Content-Length` required, streamed and enforced),
  per-IP flood shedding before any DB work, per-install and per-network quotas, and a 5 s
  statement timeout on its DB role. No docs or OpenAPI endpoints, generic errors, `nosniff`, HSTS.
  `X-Forwarded-For` is trusted only for the ingress's own hop (`test_spoofed_forwarded_for_is_ignored`).
  The image has no training stack (no numpy, no crypto library, no signing code).
* **CI/CD:** pull request code runs only on GitHub-hosted runners. The homelab runner only runs
  `main`, after tests, optionally behind a required-reviewer environment. Images are tagged by
  commit, with provenance.

## 4. Privacy

* No accounts, names, emails, saves, hardware ids or IP addresses. A record is the AI's own
  decisions (battle-state numbers) and the outcome (`records.py`, `extra="forbid"`).
* The install id is random; the token is derived from it server-side and never stored.
* Rate limits use a keyed hash of the /24 (IPv4) or /48 (IPv6) network, not the address
  (`test_no_ip_addresses_stored`).
* Battles are deleted after 90 days (`test_retention_deletes_old_battles`).
* Players can turn uploads off (Settings → Controls → Share Grand Admiral Battles). Queued records
  are then never sent. Model downloads continue, as they carry nothing about the player.

## Keys and secrets

| Secret | Lives in | If leaked |
|---|---|---|
| Model signing key | Operator's offline storage, `grand-admiral-trainer` Secret | Attacker can ship AI behaviour (still only data, still clamped). Rotate: new key in a game update, then revoke the old one. |
| Server key | `grand-admiral-api` Secret | Attacker can mint upload tokens. Still subject to every content check. Rotate: new secret, clients re-register automatically on 401. |
| DB role passwords | `grand-admiral-db` and the db-url Secrets | Limited to that role's grants. |

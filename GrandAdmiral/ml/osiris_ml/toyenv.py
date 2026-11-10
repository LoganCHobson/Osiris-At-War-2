"""A toy battle simulator for testing the learning pipeline end to end, without Unity.

It is *not* the game. It's a Lanchester-style fight with the same decisions (stance, retreat,
reinforcement commitment, focus target) and the same feature vector the game produces, plus a few
truths the Admiral heuristics get wrong, so there is something to learn:

* carriers' listed DPS hides the strike craft they launch: killing them early pays off;
* retreating a little earlier than the Admiral's 0.35 keeps more of the fleet alive;
* surging reserves into an already-winning fight finishes it faster.

Records come out in exactly the format the game uploads, so they also exercise the API and the
trainer's deep checks.
"""

import math
import uuid
from dataclasses import dataclass

import numpy as np

from . import distill, policy, spec

F = {name: i for i, name in enumerate(spec.FEATURES)}
KINDS = ("capital", "screen", "carrier", "strike")
TICK = 3.0
LIMIT = 600.0
STANCE_EFFECT = {0: (1.0, 1.0), 1: (1.35, 1.2), 2: (0.6, 0.55)}


@dataclass
class ShipState:
    kind: str
    power: float
    hp: float
    max_hp: float
    dps: float          # what the game would display
    true_dps: float     # what it actually does
    distance: float

    @property
    def alive(self):
        return self.hp > 0

    @property
    def health(self):
        return max(0.0, self.hp / self.max_hp)

    @property
    def live_power(self):
        return self.power * self.health


def _fleet(total, rng):
    ships = []
    while sum(s.power for s in ships) < total:
        kind = rng.choice(KINDS, p=[0.35, 0.35, 0.12, 0.18])
        power = float(rng.uniform(80, 260))
        listed = power * (0.04 if kind == "carrier" else 0.1)
        true = power * (0.3 if kind == "carrier" else 0.1)
        ships.append(ShipState(kind, power, power * 10, power * 10, listed, true, float(rng.uniform(0.2, 2.5))))
    return ships


def _power(ships):
    return sum(s.live_power for s in ships if s.alive)


def _priority(ship):
    proximity = 1.0 / (1.0 + ship.distance)
    return (ship.dps + ship.power * 0.05) / max(1.0, ship.hp) * proximity


class ToyBattle:
    def __init__(self, rng, vs_player=False):
        self.rng = rng
        self.vs_player = vs_player
        ours = float(rng.uniform(600, 1500))
        theirs = ours * float(np.exp(rng.normal(0.0, 0.55)))
        fleet = _fleet(ours, rng)
        rng.shuffle(fleet)
        split = max(1, int(len(fleet) * 0.6))
        self.field, self.reserve = fleet[:split], fleet[split:]
        self.enemy = _fleet(theirs, rng)
        self.attacker = bool(rng.random() < 0.5)
        self.defenses = (not self.attacker) and rng.random() < 0.4
        self.start = _power(fleet)
        self.enemy_start = _power(self.enemy)
        self.t = 0.0
        self.stance = 0
        self.commit = 1
        self.focus = None
        self.last_ratio = None
        self.won = self.retreated = self.enemy_retreated = False

    # ---- what the game would see ----

    def totals(self):
        ours = _power(self.field) + sum(s.power for s in self.reserve)
        defense = 300.0 if self.defenses else 0.0
        return ours + defense, _power(self.enemy)

    def features(self):
        ours, theirs = self.totals()
        field = _power(self.field) + (300.0 if self.defenses else 0.0)
        x = np.zeros(spec.N_FEATURES)
        log_total = math.log(max(ours, 1.0) / max(theirs, 1.0))
        x[F["log_total_ratio"]] = np.clip(log_total, -3, 3)
        x[F["log_field_ratio"]] = np.clip(math.log(max(field, 1.0) / max(theirs, 1.0)), -3, 3)
        x[F["ratio_trend"]] = np.clip(0.0 if self.last_ratio is None else log_total - self.last_ratio, -1, 1)
        self.last_ratio = log_total
        alive = [s for s in self.field if s.alive]
        enemies = [s for s in self.enemy if s.alive]
        x[F["our_health"]] = np.mean([s.health for s in alive]) if alive else 0.0
        x[F["enemy_health"]] = np.mean([s.health for s in enemies]) if enemies else 0.0
        reserve = sum(s.power for s in self.reserve)
        x[F["our_reserve_share"]] = reserve / max(ours, 1.0)
        x[F["our_losses"]] = np.clip(1 - (_power(self.field) + reserve) / self.start, 0, 1)
        x[F["enemy_losses"]] = np.clip(1 - theirs / self.enemy_start, 0, 1)
        our_dps = sum(s.dps for s in alive)
        their_dps = sum(s.dps for s in enemies)
        x[F["log_dps_ratio"]] = np.clip(math.log((our_dps + 1) / (their_dps + 1)), -3, 3)
        x[F["center_distance"]] = 0.5
        x[F["our_strike_share"]] = sum(s.live_power for s in alive if s.kind == "strike") / max(_power(alive), 1.0)
        x[F["enemy_strike_share"]] = sum(s.live_power for s in enemies if s.kind == "strike") / max(theirs, 1.0)
        x[F["enemy_capital_share"]] = sum(s.live_power for s in enemies if s.kind in ("capital", "carrier")) / max(theirs, 1.0)
        x[F["pop_headroom"]] = 0.5
        x[F["time"]] = min(2.0, self.t / 600.0)
        x[F["is_attacker"]] = self.attacker
        x[F["friendly_defenses"]] = self.defenses
        x[F["stance_press"]] = self.stance == 1
        x[F["stance_fallback"]] = self.stance == 2
        x[F["vs_player"]] = self.vs_player
        return np.round(x, 4)

    def contacts(self):
        enemies = [s for s in self.enemy if s.alive]
        enemies.sort(key=_priority, reverse=True)
        enemies = enemies[:spec.MAX_CONTACTS]
        log_p = distill.log_priority([_priority(s) for s in enemies]) if enemies else []
        rows = []
        for ship, lp in zip(enemies, log_p):
            rows.append([lp, math.log1p(ship.dps) / 6, math.log1p(ship.power) / 7, ship.health,
                         min(4.0, ship.distance / 2), 0.0, float(ship.kind in ("capital", "screen", "carrier")),
                         float(ship.kind == "strike"), float(ship.kind == "carrier")])
        return enemies, np.round(np.asarray(rows), 4)

    # ---- dynamics ----

    def deploy(self):
        if not self.reserve:
            return
        if self.commit == 0 and any(s.alive for s in self.field):
            return
        if self.commit == 2:
            self.field += self.reserve
            self.reserve = []
            return
        want = _power(self.enemy) * distill.PRESS_AT - _power(self.field)
        self.reserve.sort(key=lambda s: -s.power)
        while self.reserve and (want > 0 or not any(s.alive for s in self.field)):
            ship = self.reserve.pop(0)
            self.field.append(ship)
            want -= ship.power

    def step(self):
        out_mult, in_mult = STANCE_EFFECT[self.stance]
        enemies = [s for s in self.enemy if s.alive]
        alive = [s for s in self.field if s.alive]
        damage = sum(s.true_dps for s in alive) * out_mult * TICK * 10
        order = sorted(enemies, key=_priority, reverse=True)
        if self.focus is not None and self.focus.alive:
            order.remove(self.focus)
            order.insert(0, self.focus)
        for ship in order:
            if damage <= 0:
                break
            hit = min(damage, ship.hp)
            ship.hp -= hit
            damage -= hit

        incoming = sum(s.true_dps for s in enemies) * in_mult * TICK * 10 * (0.7 if self.defenses else 1.0)
        total = sum(s.power for s in alive)
        for ship in alive:
            ship.hp -= incoming * ship.power / max(total, 1.0)
        self.t += TICK

    # ---- one battle ----

    def finished(self):
        ours = any(s.alive for s in self.field) or bool(self.reserve)
        theirs = any(s.alive for s in self.enemy)
        if not theirs:
            self.won = True
            return True
        if not ours:
            return True
        if _power(self.enemy) < 0.3 * self.enemy_start and self.rng.random() < 0.3:
            self.enemy_retreated = self.won = True
            return True
        if self.t >= LIMIT:
            ours_left = (_power(self.field) + sum(s.power for s in self.reserve)) / self.start
            self.won = ours_left > _power(self.enemy) / self.enemy_start
            return True
        return False

    def end_power(self):
        return _power(self.field) + sum(s.power for s in self.reserve)


def _sample(probs, rng):
    return int(rng.choice(len(probs), p=probs / probs.sum()))


def play(model, rng, vs_player=False, heuristic=False, greedy=False):
    """One battle. Returns the battle record as the game would upload it (a plain dict)."""
    battle = ToyBattle(rng, vs_player)
    temperature = model.knob("temperature")
    epsilon = 0.0 if greedy else model.knob("exploration")
    decisions = []

    while not battle.finished():
        x = battle.features()
        out, _ = model.forward(x[None])
        out = out[0]
        decision = {"t": round(battle.t, 2), "x": x.tolist()}

        smask = distill.stance_mask(x[None])[0]
        if heuristic:
            battle.stance = int(distill.heuristic_stance(x[None])[0])
        else:
            probs = policy.masked_softmax(out[spec.OUT_STANCE], smask, temperature, epsilon)[0]
            a = int(np.argmax(probs)) if greedy else _sample(probs, rng)
            battle.stance = a
            decision["s"] = {"a": a, "p": float(probs[a]), "m": smask.tolist()}

        outlook = math.exp(x[F["log_total_ratio"]])
        if outlook < spec.RETREAT_FORCED_BELOW:
            battle.retreated = True
        elif outlook <= spec.RETREAT_NEVER_ABOVE:
            if heuristic:
                retreat = int(distill.heuristic_retreat(x[None])[0])
            else:
                p1 = float(policy.retreat_probability(out[spec.OUT_RETREAT], temperature, epsilon)[0])
                retreat = int(p1 > 0.5) if greedy else int(rng.random() < p1)
                decision["r"] = {"a": retreat, "p": p1 if retreat else 1.0 - p1}
            battle.retreated = bool(retreat)

        if battle.reserve and not battle.retreated:
            if heuristic:
                battle.commit = 1
            else:
                probs = policy.masked_softmax(out[spec.OUT_COMMIT], [True] * 3, temperature, epsilon)[0]
                a = int(np.argmax(probs)) if greedy else _sample(probs, rng)
                battle.commit = a
                decision["c"] = {"a": a, "p": float(probs[a]), "m": [True, True, True]}

        enemies, contacts = battle.contacts()
        if len(enemies) >= 2 and not battle.retreated:
            if heuristic:
                battle.focus = enemies[0]
            else:
                scores = policy.target_scores(out, contacts)
                probs = policy.masked_softmax(scores, np.ones(len(enemies), bool), temperature, epsilon)[0]
                a = int(np.argmax(probs)) if greedy else _sample(probs, rng)
                battle.focus = enemies[a]
                decision["g"] = {"c": contacts.tolist(), "a": a, "p": float(probs[a])}

        if not heuristic and len(decision) > 2 and len(decisions) < 160:
            decisions.append(decision)

        if battle.retreated:
            for ship in battle.field:
                ship.hp *= 0.75  # Escaping under fire.
            break
        battle.deploy()
        battle.step()

    if not decisions:  # Heuristic play, or a battle over before the first choice.
        decisions = [{"t": 0.0, "x": battle.features().tolist(), "s": {"a": 0, "p": 0.9, "m": [True, True, True]}}]

    return {
        "schema": spec.RECORD_SCHEMA,
        "feature_schema": spec.FEATURE_SCHEMA,
        "battle_id": str(uuid.UUID(int=int(rng.integers(0, 2**62)) << 64 | int(rng.integers(0, 2**62)), version=4)),
        "model_version": model.version,
        "client_version": "toy-1",
        "adapted": False,
        "opponent": "player" if vs_player else "ai",
        "side": "attacker" if battle.attacker else "defender",
        "duration": max(battle.t, 0.1),
        "start_power": battle.start,
        "end_power": min(battle.end_power(), battle.start),
        "enemy_start_power": battle.enemy_start,
        "enemy_end_power": min(_power(battle.enemy), battle.enemy_start),
        "won": bool(battle.won and not battle.retreated),
        "retreated": battle.retreated,
        "enemy_retreated": battle.enemy_retreated,
        "temperature": temperature,
        "exploration": model.knob("exploration"),
        "decisions": decisions,
    }


def mean_return(model, battles=300, seed=0, heuristic=False, greedy=True):
    from .dataset import battle_return
    from .records import BattleRecord

    rng = np.random.default_rng(seed)
    total = 0.0
    for _ in range(battles):
        total += battle_return(BattleRecord.model_validate(play(model, rng, heuristic=heuristic, greedy=greedy)))
    return total / battles

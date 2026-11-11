"""The battle record a game uploads after a Grand Admiral battle.

Deliberately contains nothing about the player: no names, no save data, no hardware info, just
the AI's own decisions (battle-state numbers, what it chose and with what probability) and how
the fight ended. Validated strictly here at the API edge, then again (more expensively, against
the model that supposedly produced it) in the trainer; see validate.py.
"""

import math
import re
from typing import List, Literal, Optional

from pydantic import BaseModel, ConfigDict, Field, field_validator, model_validator

from . import spec

MAX_DECISIONS = 160
MAX_DURATION = 7200.0
MAX_POWER = 1e7
FEATURE_BOUND = 10.0
_UUID = re.compile(r"^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")
_VERSION = re.compile(r"^[0-9A-Za-z.\-+]{1,32}$")

Feature = Field(ge=-FEATURE_BOUND, le=FEATURE_BOUND, allow_inf_nan=False)
Prob = Field(gt=0.0, le=1.0, allow_inf_nan=False)


class _Strict(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True, frozen=True)


class Choice(_Strict):
    """A categorical decision: action index, its probability, and which actions were allowed."""
    a: int = Field(ge=0, le=2)
    p: float = Prob
    m: List[bool] = Field(min_length=3, max_length=3)

    @model_validator(mode="after")
    def _consistent(self):
        if not self.m[self.a]:
            raise ValueError("chosen action was masked")
        if sum(self.m) < 2:
            raise ValueError("a decision needs at least two options")
        return self


class Binary(_Strict):
    a: int = Field(ge=0, le=1)
    p: float = Prob


class Target(_Strict):
    c: List[List[float]] = Field(min_length=2, max_length=spec.MAX_CONTACTS)
    a: int = Field(ge=0, lt=spec.MAX_CONTACTS)
    p: float = Prob

    @field_validator("c")
    @classmethod
    def _contacts(cls, rows):
        for row in rows:
            if len(row) != spec.N_CONTACT:
                raise ValueError("wrong contact width")
            for value in row:
                if not math.isfinite(value) or abs(value) > FEATURE_BOUND:
                    raise ValueError("contact feature out of range")
        return rows

    @model_validator(mode="after")
    def _in_range(self):
        if self.a >= len(self.c):
            raise ValueError("target index out of range")
        return self


class Decision(_Strict):
    t: float = Field(ge=0.0, le=MAX_DURATION, allow_inf_nan=False)
    x: List[float] = Field(min_length=spec.N_FEATURES, max_length=spec.N_FEATURES)
    s: Optional[Choice] = None
    r: Optional[Binary] = None
    c: Optional[Choice] = None
    g: Optional[Target] = None
    f: Optional[Choice] = None   # formation
    e: Optional[Choice] = None   # escort
    n: Optional[Choice] = None   # manoeuvre
    w: Optional[Choice] = None   # tempo

    @field_validator("x")
    @classmethod
    def _features(cls, values):
        for value in values:
            if not math.isfinite(value) or abs(value) > FEATURE_BOUND:
                raise ValueError("feature out of range")
        return values


class BattleRecord(_Strict):
    schema_: int = Field(alias="schema", ge=spec.RECORD_SCHEMA, le=spec.RECORD_SCHEMA)
    feature_schema: int = Field(ge=spec.FEATURE_SCHEMA, le=spec.FEATURE_SCHEMA)
    battle_id: str
    model_version: int = Field(ge=0, le=2**31)
    client_version: str
    adapted: bool
    opponent: Literal["player", "ai"]
    side: Literal["attacker", "defender"]
    duration: float = Field(gt=0.0, le=MAX_DURATION, allow_inf_nan=False)
    start_power: float = Field(gt=0.0, le=MAX_POWER, allow_inf_nan=False)
    end_power: float = Field(ge=0.0, le=MAX_POWER, allow_inf_nan=False)
    enemy_start_power: float = Field(gt=0.0, le=MAX_POWER, allow_inf_nan=False)
    enemy_end_power: float = Field(ge=0.0, le=MAX_POWER, allow_inf_nan=False)
    won: bool
    retreated: bool
    enemy_retreated: bool
    temperature: float = Field(ge=0.25, le=2.0, allow_inf_nan=False)
    exploration: float = Field(ge=0.0, le=0.2, allow_inf_nan=False)
    decisions: List[Decision] = Field(min_length=1, max_length=MAX_DECISIONS)

    model_config = ConfigDict(extra="forbid", strict=True, frozen=True, populate_by_name=True)

    @field_validator("battle_id")
    @classmethod
    def _uuid(cls, value):
        if not _UUID.match(value):
            raise ValueError("battle_id must be a lowercase v4 UUID")
        return value

    @field_validator("client_version")
    @classmethod
    def _client_version(cls, value):
        if not _VERSION.match(value):
            raise ValueError("bad client_version")
        return value

    @model_validator(mode="after")
    def _coherent(self):
        if self.end_power > self.start_power * 1.001 or self.enemy_end_power > self.enemy_start_power * 1.001:
            raise ValueError("a side can't end a battle stronger than it started")
        if self.won and self.retreated:
            raise ValueError("can't win by retreating")
        last = -1.0
        for decision in self.decisions:
            if decision.t < last or decision.t > self.duration + 5.0:
                raise ValueError("decision times must be ordered and inside the battle")
            last = decision.t
            if decision.r is None and decision.g is None and all(
                    getattr(decision, key) is None for _, key in spec.CATEGORICAL.values()):
                raise ValueError("empty decision")
        return self

import functools

import numpy as np
import pytest

from osiris_ml import bundle, distill, toyenv
from osiris_ml.records import BattleRecord


@functools.lru_cache(maxsize=None)
def _baseline():
    model = distill.baseline().rounded()
    model.version = 1
    return model


@pytest.fixture
def baseline():
    return _baseline().copy()


@functools.lru_cache(maxsize=None)
def _key():
    return bundle.generate_key()


@pytest.fixture
def signing_key():
    return _key()


@pytest.fixture
def toy_record(baseline):
    def make(seed=0, model=None, **overrides):
        record = toyenv.play(model or baseline, np.random.default_rng(seed))
        record.update(overrides)
        return record
    return make


def parse(record):
    return BattleRecord.model_validate(record)

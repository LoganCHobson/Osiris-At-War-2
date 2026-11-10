"""The Grand Admiral contract shared by the game client, the ingest API and the trainer.

Everything here has a twin in the Unity client (Assets/Scripts/AI/GrandAdmiral/Core/GASpec.cs).
Change one, change both, and bump FEATURE_SCHEMA: a client only accepts models built for the
feature schema it was compiled with, so an old game build keeps using its last good model instead
of misreading a new one.
"""

FEATURE_SCHEMA = 1
MODEL_FORMAT = 1
BUNDLE_FORMAT = 1
RECORD_SCHEMA = 1

# Battle-state features, in order. Each is pre-scaled by the client to roughly [-3, 3].
FEATURES = (
    "log_total_ratio",      # ln(our total power incl. reserve / enemy power), clamped +-3
    "log_field_ratio",      # ln(our field power / enemy field power), clamped +-3
    "ratio_trend",          # change in log_total_ratio since the last decision, clamped +-1
    "our_health",           # mean hull fraction of our ships on the field
    "enemy_health",
    "our_reserve_share",    # share of our power still in reserve
    "enemy_unseen_share",   # share of the enemy's expected power not yet on the field
    "our_losses",           # fraction of our starting power lost
    "enemy_losses",
    "log_dps_ratio",        # clamped +-3
    "log_range_ratio",      # clamped +-2
    "center_distance",      # fleet centres apart / (our range * 4), clamped 0..3
    "our_strike_share",     # share of our field power that is strike craft
    "enemy_strike_share",
    "enemy_capital_share",
    "pop_headroom",         # free population / cap
    "time",                 # battle seconds / 600, clamped 0..2
    "is_attacker",
    "friendly_defenses",
    "enemy_defenses",
    "enemy_retreating",
    "stance_press",
    "stance_fallback",
    "vs_player",
)
N_FEATURES = len(FEATURES)

# Per-enemy features used to choose the focus target.
CONTACT_FEATURES = (
    "heuristic_priority",   # ln(priority / best priority), clamped -5..0 (best = 0)
    "log_dps",              # ln(1 + dps) / 6
    "log_power",            # ln(1 + power) / 7
    "health",
    "distance",             # / (our range * 2), clamped 0..4
    "is_defense",
    "is_capital",
    "is_strike",
    "is_carrier",
)
N_CONTACT = len(CONTACT_FEATURES)
MAX_CONTACTS = 10

# Network outputs.
STANCES = ("engage", "press", "fallback")
COMMITS = ("hold", "match", "surge")
OUT_STANCE = slice(0, 3)
OUT_RETREAT = 3
OUT_COMMIT = slice(4, 7)
OUT_TARGET = slice(7, 7 + N_CONTACT)
OUT_VALUE = 7 + N_CONTACT
N_OUT = OUT_VALUE + 1  # 17

HEADS = ("stance", "retreat", "commit", "target")

# Knobs: plain numbers the server can retune without a game update. The client clamps every one
# to these bounds, so even a validly signed model can't push the AI outside sane behaviour.
KNOBS = {
    1: ("decision_interval", 0.5, 0.3, 2.0),          # id: (name, default, min, max)
    2: ("reinforcement_delay_scale", 0.7, 0.3, 2.0),
    3: ("think_interval_scale", 0.75, 0.5, 2.0),
    4: ("attack_margin_scale", 1.1, 0.8, 1.6),
    5: ("retreat_judgement", 1.0, 0.5, 1.5),
    6: ("ga_tick_seconds", 3.0, 1.0, 10.0),
    7: ("exploration", 0.05, 0.0, 0.2),
    8: ("temperature", 1.0, 0.25, 2.0),
    9: ("adapt_learning_rate", 0.01, 0.0, 0.05),
}
KNOB_IDS = {name: kid for kid, (name, *_rest) in KNOBS.items()}


def knob_default(name):
    return KNOBS[KNOB_IDS[name]][1]


def clamp_knob(kid, value):
    _, _, lo, hi = KNOBS[kid]
    return min(hi, max(lo, value))


# Safety rails the client applies before the network ever gets a say.
RETREAT_NEVER_ABOVE = 1.2   # outlook (our/their power) above this: retreat isn't an option
RETREAT_FORCED_BELOW = 0.12  # below this: retreat regardless of the model

# Domain separation for signatures, so a signature over a model can't be replayed as anything else.
SIGNATURE_CONTEXT = b"OGAB-SIG-v1\x00"

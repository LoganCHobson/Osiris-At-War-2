"""The Grand Admiral contract shared by the game client, the ingest API and the trainer.

Everything here has a twin in the Unity client (Assets/Scripts/AI/GrandAdmiral/Core/GASpec.cs).
Change one, change both, and bump FEATURE_SCHEMA: a client only accepts models built for the
feature schema it was compiled with, so an old game build keeps using its last good model instead
of misreading a new one.
"""

FEATURE_SCHEMA = 2
MODEL_FORMAT = 1
BUNDLE_FORMAT = 1
RECORD_SCHEMA = 2

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
    # ---- schema 2: shields, missiles, craft, gunnery, terrain, tempo, flagship ----
    "our_shield",           # mean shield fraction of our shielded ships on the field (0 if none)
    "shield_trend",         # change in our_shield since the last decision, clamped +-1
    "low_shield_share",     # share of our shielded ships under the rotate threshold
    "missile_pressure",     # enemy missiles in flight at our ships / our ship count, clamped 0..3
    "pd_idle_share",        # share of our point-defence turrets with nothing to shoot
    "enemy_craft_ratio",    # enemy squadrons / our warships, clamped 0..3
    "bombers_on_capitals",  # share of enemy bombers attacking our capital ships
    "bow_alignment",        # share of our warships whose bow points within 30 deg of their target
    "los_blocked_share",    # share of our aimed guns with no line of sight
    "cover_between",        # share of the line between the fleets that runs through asteroid fields
    "cover_near",           # 1 if an asteroid field is within our range of our fleet centre
    "enemy_waves",          # enemy arrival waves seen so far / 5, clamped 0..2
    "since_enemy_wave",     # seconds since the enemy's last arrival / 120, clamped 0..2
    "flagship_health",      # health of our strongest capital ship (0 if we have none)
    "flagship_under_fire",  # share of enemy guns aimed at our flagship
    "kited",                # 1 while we're being shot from beyond our reach
    "closing_rate",         # change in centre_distance since the last decision, clamped +-1
    "our_spread",           # mean distance from our centre / our range, clamped 0..3
    "enemy_spread",
    "log_speed_ratio",      # ln(our mean cruise speed / theirs), clamped +-2
    "damage_exchange",      # ln((damage dealt + 1) / (damage taken + 1)) since the last decision, clamped +-3
    "formation_tight",      # the current formation, escort, manoeuvre and tempo orders (one-hot,
    "formation_loose",      # the middle option implied), like stance_press / stance_fallback
    "escort_screen",
    "escort_hunt",
    "maneuver_flank",
    "maneuver_envelop",
    "tempo_rotate",
    "tempo_pulse",
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
FORMATIONS = ("standard", "tight", "loose")     # spacing: mutual point defence vs clear lines of fire
ESCORTS = ("line", "screen", "hunt")            # escorts in the battle line / guarding capitals / chasing craft
MANEUVERS = ("line", "flank", "envelop")        # one battle line / a detachment on one wing / on both wings
TEMPOS = ("steady", "rotate", "pulse")          # hold / swap shield-drained ships out / the whole line steps back
OUT_STANCE = slice(0, 3)
OUT_RETREAT = 3
OUT_COMMIT = slice(4, 7)
OUT_TARGET = slice(7, 7 + N_CONTACT)
OUT_FORMATION = slice(16, 19)
OUT_ESCORT = slice(19, 22)
OUT_MANEUVER = slice(22, 25)
OUT_TEMPO = slice(25, 28)
OUT_VALUE = 28
N_OUT = OUT_VALUE + 1  # 29

# Every three-way order: output slice and the key it's logged under in a battle record. Stance and
# commit came first; the rest are schema 2. The trainer, the deep checks and the gate treat them alike.
CATEGORICAL = {
    "stance": (OUT_STANCE, "s"),
    "commit": (OUT_COMMIT, "c"),
    "formation": (OUT_FORMATION, "f"),
    "escort": (OUT_ESCORT, "e"),
    "maneuver": (OUT_MANEUVER, "n"),
    "tempo": (OUT_TEMPO, "w"),
}

HEADS = ("stance", "retreat", "commit", "target", "formation", "escort", "maneuver", "tempo")

# Where each head's numbers sit in the output vector.
HEAD_OUTPUTS = {name: outputs for name, (outputs, _) in CATEGORICAL.items()}
HEAD_OUTPUTS["retreat"] = slice(OUT_RETREAT, OUT_RETREAT + 1)
HEAD_OUTPUTS["target"] = OUT_TARGET

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
    # schema 2: how the new orders are carried out
    10: ("formation_tight_scale", 0.6, 0.4, 0.9),    # spacing multiplier for "tight"
    11: ("formation_loose_scale", 1.8, 1.2, 2.5),    # ...and for "loose"
    12: ("flank_share", 0.3, 0.15, 0.5),            # share of the line's warships sent on a flank
    13: ("flank_width", 1.0, 0.5, 2.0),             # how far out the wing swings, in our ranges
    14: ("rotate_out_below", 0.25, 0.1, 0.5),       # a ship's shield fraction that sends it back to recharge
    15: ("rotate_back_above", 0.7, 0.4, 0.95),      # ...and that brings it back into the line
    16: ("pulse_depth", 0.4, 0.2, 0.8),             # how far the line steps back on a pulse, in our ranges
    17: ("screen_radius", 1.5, 0.8, 3.0),           # escorts' distance from the capital they guard, in spacings
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

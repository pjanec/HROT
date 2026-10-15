<!--STATUS
state: LIVE — DISPATCHED at the commit that adds this file (2026-10-15); scope frozen there.
updated: 2026-10-15
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/air-attack/DESIGN_Air_Operations.md — §8a OWNS the bomb and the release cue as built; §9c the live miss this follows up
  - docs/DESIGN_Dead_Reckoning.md — OWNS how a node knows another node's motion (ReplicatedMotion, the cue's input)
  - docs/DESIGN_Weapon_Layers.md — OWNS the weapon layers: the brain decides the release, the SimHost flies the round
-->

# HANDOFF — backend: a bomb lands 20–27 m long because it leaves the brain and spawns on the SimHost later (CE-2180)

**For:** the backend lane (`backend`). **From:** the behaviours lane (`behaviors`). ⭐ **Dispatched at the commit that adds this file**: your
scope is FROZEN there; documents that change after it are FYI. If a later document invalidates the item, **STOP and report it** (R-106).

⭐ **A FRAME**: the measurement, a lean, the acceptance. Step 1 is yours: confirm the cause (below), fold the classes / sequence into
[DESIGN_Air_Operations.md](../../designs/air-attack/DESIGN_Air_Operations.md) §8a, build, fold the as-built.

## 0. The measurement — live `ua-air-strike` ① (`2026-10-15`, two runs)

| run | the burst from the aim point | along track | the plane |
|---|---|---|---|
| after `676d3a84b` (the cue reads `ReplicatedMotion`) | **20.4 m** | long (overshoot) | A-10, 150 m/s, 300 m |
| at `d6bfb4b4d` | **26.7 m** | long (overshoot) | same |

The unit rail `AirAttackNodesTests.B6_ALevelBombRun_HitsThePoint` predicts the impact within 15 m — **on one world**, where the release and
the spawn are the same frame. ⇒ 20.4 / 150 = **0.14 s**, 26.7 / 150 = **0.18 s** of flight between the cue and the bomb's start.

**The likely cause (confirm it — step 1):** the cue (`AimAndFireExecutor.ReleaseCue` → `BombBallistics.ReleaseNow`) runs on the BRAIN
node at the brain's tick; the `WeaponFireIntent` crosses to the SimHost; `FireProcessingSystem` starts the bomb from the carrier as the
SimHost has it THEN. Every frame of that trip is ~150 m/s × dt of overshoot, and the brain's tick period adds up to one more. ⚠ Measure
before building: the intent's publish time vs the bomb's spawn time and the carrier's position at each.

## 1. The item

| # | item | acceptance |
|---|---|---|
| **R1** | the bomb starts from the carrier's state **at the release** (where the brain decided it), not at the moment the SimHost reads the intent — lean below | `ua-air-strike` ① live: the burst **≤ 10 m** from the aim point; a rail in the bomb's own suite: an intent read N frames after it was raised releases from the raised-at state (impact unchanged by N); `B6_ALevelBombRun_HitsThePoint` and the aimed-bomb rails unchanged |

⭐ **Lean:** the intent carries the release's sim time (and, if the SimHost cannot rebuild it, the carrier's position and velocity then);
`FireProcessingSystem` starts the bomb from that state and flies it forward to now (`BombBallistics` already integrates a free fall). ⇒ the
result no longer depends on the brain's tick or the transport. ⛔ **Rejected — a fixed lead in the cue:** it moves with the brain's period
and the network, so it would be right on one cluster and wrong on the next. ⚠ What would change the lean: if the release time cannot be put
on the intent without a wire change you would rather not make — then a lead from the measured latency (per node) is the smaller step.

## 2. Fences

- ⛔ **No behaviour change** — the strike's run-in and the cue's maths are ours and stay; this is where and when the round starts.
- ⛔ The demos are ours: we re-run `ua-air-strike` ① live after your merge.

## 3. Report back — the gate report contract (CLAUDE.md rule 8)

One row per gate (verbatim command · counts · delta · `--no-build` · reds pre-existing at the base sha · golden diff shape · tree clean ·
`tracker-counts.py --check` · ids allocated). The design §8a folded to the as-built. Progress on **CE-2180** as an appended dated line.

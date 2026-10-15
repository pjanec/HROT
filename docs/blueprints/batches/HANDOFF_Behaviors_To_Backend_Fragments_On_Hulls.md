<!--STATUS
state: LIVE — DISPATCHED at the commit that adds this file (2026-10-15); scope frozen there.
updated: 2026-10-15
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/vehicle-tactics/DESIGN_Armour_Hit_Model.md — §7.5–7.6 OWN the area effect on hulls (AE1–AE4 as built); this is its follow-up
  - docs/DESIGN_Building_Interiors.md — §3k W-6′ OWNS the fragment model (falloff, exposure) this touches
  - docs/designs/air-attack/DESIGN_Air_Operations.md — §9c the live check that found it
-->

# HANDOFF — backend: a fragment on a HULL is not a fragment in a man (AE5, follow-up to CE-2177)

**For:** the backend lane (`backend`). **From:** the behaviours lane (`behaviors`). ⭐ **Dispatched at the commit that adds this file**: your
scope is FROZEN there; documents that change after it are FYI. If a later document invalidates the item, **STOP and report it** (R-106).

⭐ **A FRAME**: the goal, the measurement, a lean, the acceptance. Step 1 is yours: fold the classes / sequence into
[DESIGN_Armour_Hit_Model.md](../../designs/vehicle-tactics/DESIGN_Armour_Hit_Model.md) §7, then build, then fold the as-built.

## 0. The measurement — live `ua-air-strike` after AE1–AE4 (`behaviors@d6bfb4b4d`, `2026-10-15`)

One Mk-82 (`fragmentRadius` 142.9 m, `blastInjuryRadius` 27.3 m) burst 20 m from the Target Technical (`GET /combat/detonations`):

| entity | distance | fragmentFalloff | fragmentArmourChance | fragmentDamage | blastDamage | result |
|---|---|---|---|---|---|---|
| Target Technical (pickup 104, 5 mm) | 20.1 m | 0.738 | 1 | **41 638** | 59 | destroyed — ✅ wanted (R-2019 ①) |
| **Far Technical** (pickup 104) | **121.6 m** | 0.022 | 1 | **1 253** | 0 | 🔴 **destroyed** |
| its gunner, its M2 | 121.4 m | 0.022 | 1 | 1 253 / 1 276 | 0 | destroyed |

⇒ a soft hull takes the **personnel** fragment sum (every fragment of the falloff, at a person's per-fragment damage) once the armour chance
is 1 — so a pickup dies anywhere inside the fragment radius, and the one at 20 m is overkilled ×140. ⚠ The people row is not new (a
person's fragment damage did not move in AE1); the hull rows are — before AE1 the 0.5 g fragment never passed 5 mm.

## 1. The item

| # | item | acceptance |
|---|---|---|
| **AE5** | a hull's fragment damage counts **hits on the hull** at a **hull-scale damage per penetrating hit** (or an equivalent cap / per-type fragment susceptibility) — your call which; lean below | `AreaEffectSystemTests`: a pickup **in the open** at 20 m from a Mk-82 is still destroyed (blast + fragments); at **120 m** it is **damaged, not destroyed** (holed — Health > 0, mobility kept); an MBT unchanged (0); the grenade / 40 mm / RPG numbers against people unchanged |

⭐ **Lean:** hits = fragments through the hull's presented area at that distance (the falloff already is fragments per m²), damage per hit =
the round's per-hit damage the direct-fire path uses on that hull (`ArmorModel`, CE-3071) — so one model for "a fragment hit a pickup" and
"a bullet hit a pickup". ⚠ What would change the lean: if `ArmorModel` per-hit numbers make 20 m survivable — then a per-type fragment
susceptibility on `CombatPlatformDefDto` (beside `BlastSusceptibility`) is the smaller change.

## 2. Fences

- ⛔ **No person number moves.** (Whether a man at 121 m from a Mk-82 should always die is a separate question — report it as a line if
  you see a cheap answer, do not change it here.)
- ⛔ The demos are ours: we re-run `ua-air-strike` ④ live after your merge.

## 3. Report back — the gate report contract (CLAUDE.md rule 8)

One row per gate (verbatim command · counts · delta · `--no-build` · reds pre-existing at the base sha · golden diff shape · tree clean ·
`tracker-counts.py --check` · ids allocated). The design §7 folded to the as-built. Progress on **CE-2177** as an appended dated line.

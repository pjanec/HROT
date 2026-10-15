<!--STATUS
state: LIVE — DISPATCHED at the commit that adds this file (2026-10-15); scope frozen there.
updated: 2026-10-15
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/vehicle-tactics/DESIGN_Armour_Hit_Model.md — §7 THE frame (D-M1…D-M4, acceptance); you write its classes and sequences as step 1
  - docs/DESIGN_Building_Interiors.md — §3k W-6′ / W-7′ OWN the fragment and blast model and the building barrier / shadow this reuses
  - docs/designs/air-attack/DESIGN_Air_Operations.md — §9c the live miss that produced it
-->

# HANDOFF — backend: area effects against vehicles (bigger fragments from big warheads, blast on soft hulls)

**For:** the backend lane (`backend`). **From:** the behaviours lane (`behaviors`). ⭐ **Dispatched at the commit that adds this file**: your
scope is FROZEN there; documents that change after it are FYI. If a later document invalidates an item, **STOP that item and report it**; do
every item that is not blocked (R-106).

⭐ **This is a FRAME** (lane protocol): the goal, the fences, the decisions with leans, the acceptance. **Step 1 is yours: write the classes and
the sequence into [DESIGN_Armour_Hit_Model.md](../../designs/vehicle-tactics/DESIGN_Armour_Hit_Model.md) §7** (diagram first), then build
against it, then fold the as-built.

## 0. Why

📌 Measured live (`ua-air-strike`, `2026-10-15`): a Mk-82 bursting 15 m from a pickup leaves it at Health 100 — its fragment (one 0.5 g mass
for every warhead) penetrates ≈ 2.4 mm against the pickup's 5 mm, and blast skips every hull (`AreaEffectSystem.cs:116`). ⇒ nothing short of a
direct hit damages any vehicle, which the mortars and howitzers of IF-1 inherit. 🔒 User, `2026-10-15`: *"A and B together is the most realistic
… chosing this one, most realistic. Blast should be reduced by buildings (later we will be destructing buildings, backend can think about that,
but not needed immediately)."* (**R-2019**, tracker **CE-2177**.)

## 1. Before anything

1. Merge `behaviors` at the dispatch commit or later. Push a `chore: started …` marker (rule 1b).
2. Ids from your block (`CE-3xxx`); progress on **CE-2177** as appended dated lines.

## 2. The items

| # | item | design | acceptance |
|---|---|---|---|
| **AE1** | **(A)** a warhead's fragment mass grows with its casing, anchored so a hand grenade, a 40 mm and an RPG keep 0.5 g (the reference library's `fragments` coefficients + `ReferenceLibrary.Warhead`; a TKB `WarheadDto` still overrides) | §7.3 D-M1 | the library suite: grenade / 40 mm / RPG warheads unchanged; Mk-82, 155 mm, 120 mm heavier, their penetration in the report |
| **AE2** | **(B)** blast damages a hull × a per-type **blast susceptibility** (TKB, soft-skinned ≈ 1, armoured ≈ 0), through the SAME closed-barrier transmission and shadow a person gets (W-7′) — so buildings reduce it | §7.2, §7.3 D-M2 | `AreaEffectSystemTests`: a pickup in the open is wrecked by a Mk-82 at 20 m; behind a building it takes less; an MBT outside the lethal radius takes none |
| **AE3** | the catalogue: a susceptibility on every vehicle type (pickups, trucks, cars, APC / IFV / MBT, the M109, the helicopters on the ground) — report every number you invent | §7.3 | the catalogue suites; the type-picker golden if it moves |
| **AE4** | *(optional, your call)* a light vehicle's mobility kill from blast before it dies (`MobilityKill`, M9) | §7.3 D-M3 | — |

## 3. Fences

- ⛔ **No person number moves** for grenades, 40 mm and RPG HE. ⚠ Any number that DOES move (e.g. a bomb's fragments now passing a wall) is a
  REPORT line, with the before / after.
- ⛔ **Destructible buildings are NOT this batch** (D-M4) — note in your design where they would plug in (the barrier), nothing more.
- ⛔ The demos are ours: we re-run `ua-air-strike` ③ live after your merge.

## 4. Report back — the gate report contract (CLAUDE.md rule 8)

One row per gate: verbatim command · pass / fail / skip · delta vs base · `--no-build` column · every red confirmed pre-existing against the
base sha · golden movement as a diff shape · tree clean after each suite · `tracker-counts.py --check` · every id allocated. The design §7
folded to the as-built. Then the sha of each item on `backend`.

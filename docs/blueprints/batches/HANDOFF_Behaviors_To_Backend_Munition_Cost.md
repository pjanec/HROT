<!--STATUS
state: LIVE — DISPATCHED at the commit that adds this file (2026-10-15); scope frozen there.
updated: 2026-10-15
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/air-attack/DESIGN_Air_Operations.md — §7b W2 OWNS the approved rule (enough, not most; R-2020); fold the as-built there
  - docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md — §2 OWNS AmmoChoice (D-I: the round by expected damage); this changes its pick
-->

# HANDOFF — backend: a munition has a cost; the weapon brain picks the cheapest round that is enough (CE-2186)

**For:** the backend lane (`backend`). **From:** the behaviours lane (`behaviors`). ⭐ **Dispatched at the commit that adds this file**: your
scope is FROZEN there; documents that change after it are FYI. If a later document invalidates the item, **STOP and report it** (R-106).

⭐ **A FRAME**. Step 1 is yours: read [Air Operations §7b](../../designs/air-attack/DESIGN_Air_Operations.md) W2 (🔒 R-2020) and
[Missiles and Ammo §2](../../designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md), draw the change into Missiles and Ammo §2 (it owns
`AmmoChoice`), build, fold the as-built.

## 0. The measurement (`2026-10-15`)

- `AmmoChoice.ValueOf` = expected damage of one round ÷ the target's max health; `AmmoChoice.Best` takes the HIGHEST ⇒ the strongest round
  always wins, however weak the target (`AmmoChoice.cs:67,85`).
- No munition carries a cost: nothing in `Fdp.Toolkits/Tkb/Domain/*.cs`; every munition is built by `MunitionTkbCatalog` with a
  `TkbMasterDto` and (most) an `AmmoWeaponBallisticsDto` (`MunitionTkbCatalog.cs:228-235`).

🔒 **User, `2026-10-15`:** *"not using non-reasonably strong or expensive ammo or weak or collateral-damage sensitive targets"* → W2
*"accepted"*.

## 1. The item

| # | item | acceptance |
|---|---|---|
| **MC1** | every munition gets a **relative cost** (TKB, one field — the DTO is your call; a rifle round 1, an ATGM / a bomb in the hundreds; your numbers, with the rule you used). `AmmoChoice` picks the **cheapest** loaded round whose value is **enough** — lean: `ValueOf × the rounds the action fires ≥ 1` (it kills within the burst) — else the most effective, as today. Expose the rule as one function behaviors can call per MOUNT (lean: `AmmoChoice.Sufficient(world, mount, ammo, shooter, target, rounds)`), so the air attack's profile / mount pick (Air Ops §7b W2, B12) uses the SAME test — one rule, not two | `AmmoChoiceTests`: a 25 mm HE / APFSDS / ATGM mount vs a rifleman picks HE, vs an M1 picks the ATGM (nothing cheaper is enough), vs a technical picks the cheaper round that kills it; a type with no costs behaves exactly as today. ✅ Red-proof: the pick by value only ⇒ the rifleman draws the ATGM ⇒ red |

## 2. Fences

- ⛔ Collateral (W3) is NOT this item — the air attack checks who stands near the aim point (behaviors).
- ⛔ A type with no cost data keeps today's pick (no existing rail moves without a reason you state).

## 3. Report back — the gate report contract (CLAUDE.md rule 8)

One row per gate (verbatim command · counts · delta · `--no-build` · reds pre-existing at the base sha · golden diff shape · tree clean ·
`tracker-counts.py --check` · ids allocated). Missiles and Ammo §2 folded to the as-built. Progress on **CE-2186** as an appended dated line.

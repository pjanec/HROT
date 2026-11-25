<!--STATUS
state: LIVE — dispatched, not started — ⭐ UNATTENDED, under PROTOCOL_Autonomous_Lanes.md
updated: 2026-11-25
current-answer: the whole file
related-designs:
  - ../../designs/engineering/DESIGN_Bridge_Laying.md — ⭐ THE design (rev 2, leans APPROVED R-4299): §2 the diagrams, §3 BL-1 … BL-16, §5 BL-S1 / BL-S2, §10 the review
  - ../../DESIGN_Peek_And_Fire.md — §9 the static obstacle (P-7a, R-243) this adds a WALKABLE kind to
  - ../../DESIGN_Terrain_Authoring.md — §8 the authored bridge (the deck shape to lift), §18k the bridge roads
  - HANDOFF_Coordinator_To_Backend_Bridge_Laying.md — backend's half (BL-S3, the engineer), AFTER this one
  - PROTOCOL_Autonomous_Lanes.md — how you work
-->

# HANDOFF — coordinator → `terrain`: BRIDGE LAYING, the walkable obstacle and its destruction (CE-4692 — BL-S1, BL-S2)

**For:** terrain. **From:** coordinator. **Dispatched at the commit that adds this file**; ⛔ scope FROZEN there; a later document that
invalidates an item ⇒ STOP that item, do the rest (R-106).

🔒 User, `2026-11-25`: *"bridge layer vehicle to be instructed to create a bridge over a river, temporary, removable and destroyable. HROT
entities need to be able to cross the river (sumo not necessary if not easy)"* · *"leans approved, write the hand offs"* (R-4299).

## Priority
After the row in progress, before your second-tier rows. Status in a new `STATUS_Terrain_Bridge_Laying.md` (protocol §3); decisions to
`DECISIONS_Terrain.md`. ⭐ Backend's engineer slice (BL-S3) builds on yours — push per item so it can start from a real bridge.

## Read first
The design END TO END — rev 2, after the independent review (§10 says what each finding changed). ⭐ Build §2b's class diagram; a deviation is
folded back into the design (obligation ⑤). ⛔ Do not quote the HISTORY section (rev 1's separate entity + side table).

## What you build

| # | builds (design §) | done when (rail, red-proved) |
|---|---|---|
| ① | **the walkable kind** (BL-1): `StaticObstacle.Kind` (Solid / WalkableDeck), `BridgeShape` (`[PerInstanceValue]`, unmanaged: deck ends A / B, width, deck Z, ramp rise) + its descriptor and translator, the TKB types (an assault span and a floating span — placeholder figures, said; Q-6 fills them in BL-S3) | a placed bridge obstacle reaches every node with the same shape; held Constructing until its bake commits |
| ② | ⭐ **one deck geometry** (BL-4): lifted from `FeatureKinds` BELOW the JSON (corners + the buried ramp foot), deck height and ramp length (from the rise, ≤ 20°) as parameters; the authoring compile AND the bake call it | the authored bridge's compiled world is byte-identical before / after the lift (a rail) |
| ③ | **the bake ADD** (BL-5): `TerrainObstacles.With` adds walkables; `BakeObstacles` takes them; `SignatureNow`, `DirtyRegions`, `CommitObstacles` include them; a drawn-by-entity flag on a walkable (BL-13) | ⭐ the INCREMENTAL bake (dirty rectangles, not a full build) finds a path bank to bank for a man and for a vehicle over a placed deck, and none without it (BL-6 — on the pattern of `FeatureKindsTests.ABridge_IsTheOnlyCrossing…`) |
| ④ | **the floor**: the baked deck answers `SurfaceZ` / `HullSurfaceZ` (nothing new beyond ③ — verify); the kerb (BL-12): `HullClearance` blocks a drop larger than its climb on a deck | a hull pushed sideways on the deck stays on it |
| ⑤ | **the road ADD** (BL-7): `BridgeClosureSystem` keeps base and added apart (`base − blocked + added`), its own change test for the bridge set, bridges exempt from the blocker walk, the ends snapped by `BridgeRoads.Plan` over the run-time network | a road-following tank routes over a placed bridge; the base network is unchanged after the bridge is removed |
| ⑥ | **destroy** (BL-2, BL-3, BL-8): the deck skipped at once at removal (as a felled trunk), then rebaked; the road cut; `AreaEffectSystem` measures reach and nearest point against the DECK footprint | a charge at a span's far end drops it; a man on the deck has no floor at the frame it falls |
| ⑦ | **the water** (BL-9): `WaterFallSystem` on the deciding node — a man on a fallen deck out of the fight, a vehicle destroyed, through `DamageAssessedEvent` (R-171); a fallen laid bridge leaves NO rubble | the BL-S2 scenario rows below |

## Scenarios (R-4277) — `scripts/scenario-regression.sh <name>`

| scenario | expect rows (key on STATE and path existence, never a time — the bake is wall-clock paced, R-244) |
|---|---|
| `bl-placed-bridge` | a scenario-placed laid bridge: a tank and a squad ordered to the far bank reach it · ⭐ the same order with the bridge absent (a second variant): neither reaches it |
| `bl-blow-the-bridge` | a demolition charge on the deck: the bridge is gone · a tank on it is destroyed · a tank ordered across afterwards does not reach the far bank |

## ⛔⛔ DONE = THE SCENARIOS PASS (R-4277; user `2026-11-25`: *"do not forget the feature test scenario with objectives for testing that all, without that nothing is considered done"*)

- Every scenario above is a **feature test**: its `description` says **What** happens and how to **Judge** it, and its `checks.json` states the
  same as machine-checked `expect` rows — ⭐ where the expectation is an outcome (a bank reached, a bridge held or lost), say it as a scenario
  **OBJECTIVE** (Scenario Rules: the objective's state is what the row judges), not only a cue.
- ⛔ **The row is NOT done until `scripts/scenario-regression.sh <name>` passes for EVERY scenario listed** (deterministic editor run). Green rails
  alone are not "the feature works". A scenario that cannot pass ⇒ the row stays open, the reason in the status file.
- The report lists each scenario with its expect rows and its result.

## ⛔ Not yours
The engineer vehicle, `BridgeCarried`, the LayBridge / RecoverBridge orders and behaviours (backend, BL-S3) · the floating bridge's own scenario
(BL-S4) · SUMO · load classes.

## Gates (CLAUDE.md rule 8)
The touched classes' suites + the obstacle bake's own rails (`StaticObstacleBake*`, `ObstacleRemovalReplayTests`, `FeatureKindsTests`) + the road
blockage rails (`BridgeClosureTests`) + the area-effect rails + the two scenarios; the parallel env on every new / changed system; the world guard.
The bake and the road step are cross-node (every node derives them) ⇒ name the integration suite you ran (row 8). Report the ids you allocated.

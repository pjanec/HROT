<!--STATUS
state: LIVE — DISPATCHED at the commit that adds this file (2026-10-16); scope frozen there.
updated: 2026-10-16
current-answer: the whole file. §1 is the queue, in order; §2 what changed today that you will meet; §3 the traps that cost time.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/RESUME_Backend_Lane.md — the lane's state doc; its §0000 points here while this handoff is live
  - docs/designs/weather/DESIGN_Weather_And_Time.md — item 1 (CE-3216): READY-TO-BUILD, R-3012; §5 is the slice table
  - docs/blueprints/batches/HANDOFF_Backend_To_Behaviors_Live_Rerun_Results.md — the last backend → behaviors handoff (what they owe back)
  - docs/blueprints/batches/HANDOFF_Backend_To_Behaviors_Javelin_Top_Attack_Fixed.md — CE-2222's re-run request to behaviors
-->

# HANDOFF — backend → backend: the continuation (a new session takes the lane)

**For:** a new backend session on branch **`backend`**. **From:** the backend session of `2026-10-15/16` (CE-2209/2210/2217/2218/2222,
the weather design). ⭐ **You are the ACTIVE backend session; the sender stops** (🔒 user, `2026-10-16`: *"pls write handoff to another
session (also backend) so i can continue there"*). Dispatched at the commit that adds this file; scope frozen there.

🔒 **The user's standing mode for this lane** (`2026-10-16`): *"pls keep going autonomously, merging behaviors branch as you go, reading and
processing their handoffs, keep writing handoff fo behavior branch, they act similarly (merging yours, checking and processing handoff),
keep working autonomously"*.

## 0. Start here

1. RULE ZERO: read [`RULINGS.md`](../RULINGS.md), run `python3 scripts/design-digest.py` and `python3 scripts/rulings-check.py`, open the reply
   with the per-lane `DESIGN BRIEF` (lane: backend).
2. `git fetch origin backend ui behaviors` → start at **`backend@`** the commit that adds this file. Merge `origin/behaviors` and `origin/ui` if
   they moved (`git merge-tree --write-tree` first; gate the suites their diff touches, in the BACKGROUND). Push a `chore: started …` marker
   naming both shas before writing code (rule 1b).
3. Ids: tracker next free **`CE-3218`**; rulings **`R-3013`** (backend blocks `CE-3000…3999`, `R-3000…3999`). New tracker rows go at the END
   of the backend area (after `CE-3217`).

## 1. The queue, in order

| # | item | what it is | state · lean |
|---|---|---|---|
| **1** | ⭐ **CE-3216 S1-0** — [Weather and Time](../../designs/weather/DESIGN_Weather_And_Time.md) §5 | the FIRST positionless replicated entity: a **World Environment** TKB type (no `SimTransform`) carrying a `ScenarioClock` component (`StartUtcTicks`, `DisplayUtcOffsetMinutes`) on its own descriptor (`dtScenarioClock`, Brain group) | **not started.** ⭐ The two-node rail FIRST (C-4): created on CGF through `ScenarioEntityCreationRequestSource`, edited at runtime through `UpdateEntityAttributeCommand` (→ the owner's `UpdateEntityAttributeRequestSystem`), read on SimHost; UTC = start + sim time agrees on both. Home: `SplitAuthoritySpawnTests` (the split-authority suite) — ⚠ explicit DDS domain ≤ 232 (33 is IT-SA-4's; use 35–39). Pattern to mirror for the component + descriptor + sender / receiver: `DoorState` (`EntityDoorState*Translator` in `SharedTranslatorPack`, its `NedOwnershipGroupBinding` mapping, the `Door` TKB type in `BdcTkbCatalog`). ⚠ W-8: the start time is AUTHORED — saved as is, never from the wall clock, never rewritten at save; a runtime edit sets a new start time; a scenario with none gets a FIXED built-in default at load (C-3) |
| **2** | **CE-3216 S1** | `WeatherConditions` (`dtWeather`) on the World Environment and on a new **Weather Region** area type (`EditablePolyline` + centroid `SimTransform`, its own type like `TerrainZone` 8804); `WeatherField` (§4 — pure, no system); W-1 blend (centroid-to-edge along the polygon, smoothstep), W-2 overlap (regions add, global fills, wind as a vector); retire the `Wind` singleton (`Descent.cs:81`; readers `DescentModels.cs:313`, `AirborneDrillBehaviors.cs:406`) | after S1-0. Rails per §5. Then a **ui handoff**: the Scenario Details view (start time + global weather) and CE-3217 |
| **3** | **CE-3217** (for ui) | an empty-map click shows nothing in the Scenario Details panel: the map-background context (BP-542: `SelectionOrigin.MapBackground`, `ZonesDetailsView`) is built but never registered / raised | filed for ui; bundle it into the S1 ui handoff (the Scenario view goes beside the Zones view) |
| **4** | behaviors' re-runs (theirs) | `ua-javelin-lock` ① after CE-2222 (the Javelin top-attacks from 150 m) · `ua-eject` ② (canopy 3.96 s vs the check's 3.5 s — their check reads the phase over HTTP) · other behaviours' answer to `Masked` (they did the vehicle node, CE-2221) | nothing to do unless their handoff reports back a backend cause |
| **5** | the backlog | the tracker's backend area (`[ ]` rows) and [RESUME_Backend_Lane.md](../RESUME_Backend_Lane.md) §2 | ⛔ CE-3200 (the scale-out path request drops `TurnRadius`) waits by its own lean for a separate solver node |

## 2. What changed today that you will meet

| change | where it bites |
|---|---|
| `ShotOrigin.Eye` / `ShotOrigin.From` — the ONE origin rule for a straight round (barrel, else eye) | `FireProcessingSystem` and `LineOfFire.MuzzleMasked` both read it; change one, you change both |
| `LineOfFire.MuzzleMasked` → `AimAndFireExecutor` holds with **`ActionReason.Masked`** (aimed and blind releases) | a shooter whose own round would stop within 2 m (or a warhead's blast-injury radius) does not fire; `PeekAndFire` burns that spot (`MaskedSince`), behaviors' vehicle node burns its firing point |
| `BodyPartHit.Segment(..., arcFaces: true)` for VEHICLE parts | a vehicle part's face is the armour ARC it arrives from (front within 60°), not the box face entered — people keep the face entered (Body Parts §9b) |
| `MissileDto.TurnRateDegPerSec` / `ClimbAngleDeg` (0 = the engine's 30°/s, 30°), carried on `GuidedRound` | the Javelin is 120°/s, 45° — top attack from 150 m (Missiles and Ammo §3a′) |
| `EntityDescentEgressTranslator` scans EVERY lifecycle | the descent a body is BORN with crosses the CGF → SimHost hand-off (CE-2210); ⚠ 6 other egresses still scan Active only — harmless today (Brain group), but any Muscle-group component the creator fills at birth needs the same |
| `EjectionSystem` reads the jet through `ReplicatedMotion.Of` | the brain never owns the jet's motion |
| `MapRouteEgressTranslator` has no `SimTransform` filter | a route needs only `TkbIdentity 8802` + `RoutePlan` (CE-2209) |
| `ActionStatusOf.Label` / `IsHold` cover every reason | `ActionStatusTests.EveryReason_HasALabel` reddens when a new reason has no word |
| Map.Common: three test classes that call `ComponentTypeRegistry.Clear()` are in a non-parallel `SerialTests` collection | a NEW test class there that clears the registry must join it |

## 3. Traps that cost this session time

| trap | do this |
|---|---|
| a CycloneDDS domain id **> 232** fails to create a participant (`DdsException: Failed to create participant`) | pick an explicit free domain ≤ 232; the shared counters in `SplitAuthoritySpawnTests` (229→) are already at the edge |
| the live demo checker REFUSES a runner older than any source (`the runner … is older than …`) | rebuild `Hrot/Runner/Hrot.ClusterRunner` after every source edit, and ⛔ do not edit sources while a background build or run is in flight (one run here built half an edit and had to be re-gated) |
| `| tail -N` inside a loop over demos cuts the PASS/FAIL lines of a long check | capture each run to its own file, then grep `PASS|FAIL` |
| `xunit` `ITestOutputHelper` lines are indented (`" f  3: …"`) | match with `f +[0-9]+:`, not `f ?[0-9]+:` |
| `world.HasAuthority(e, long packedKey)` binds the INSTANCE `HasAuthority(Entity, int)` when called as an instance method on `EntityRepository` | call `AuthorityExtensions.HasAuthority(world, e, key)` explicitly |
| a probe test file left untracked trips the stop hook every turn | park probes in the scratchpad between runs; turn the keeper into a rail, delete the rest |

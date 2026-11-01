<!--STATUS
state: LIVE — coordinator → backend: scenario-rules SLICE S0 (the reference / place / orientation types + the one walker + CE-4200's 32-bit ids, TkbTypeId, NodeId), an overnight batch
updated: 2026-11-01
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Scenario_Rules.md — §00 THE BUILD SPEC (S0's row in §00.6; the module and class diagrams §00.3 / §00.5) · §9.8.0 THE TYPES (current answer) · §9.8.8 / §9.8.9 / §9.8.10 the declarations · §9.8.6 SR-138 what tools see · §9.8.3 SR-128 / SR-129 / SR-132 the walker and the rails
  - ../../DESIGN_Semantic_Value_Types.md — SV-2 TkbTypeId, SV-3 NodeId (bundled into CE-4200); §3 the shared type pattern
  - ../DESIGN_Entity_Reference.md — OWNS EntityRef / EntityRefRemap (D1…D7) — the walker you generalise
  - ../DESIGN_Map_Picking_Unification.md — P5 "the type makes a location pickable" — PickableGeoPoint is retired into PointLink
  - ../../DESIGN_Scenario_Entity_Keys.md — CE-4153 (stable GUIDs, S1–S7 built) · §0a finding ③ (a scenario migration must be a C# tool driven by the type plan)
  - ../Architect_Question_107_NED_Wire_Frame_And_Names.md — row E (wire ids long) is SUPERSEDED by CE-4200 · row B (local metres) · row I (SI)
  - ../../reference/NED_DDS_Protocol.md — update the id width and the type names
  - ../Blueprint_Issues_Tracker.md — CE-4200 (the row is part of the spec)
-->
# HANDOFF — coordinator → backend: **scenario-rules S0 — types and ids** *(overnight)*

**Dispatched at** the commit that adds this line (`coordinator`); ⛔ **scope frozen there** — documents that change after it are FYI only; a later
document that invalidates an item ⇒ STOP that item and report it (never adapt, never revert). Merge `coordinator` first, then `ui` and
`behaviors` (this touches files every lane owns); push an empty `chore: started S0 types-and-ids at <sha>` before any code.

🔒 **User, `2026-11-01`:** *"Let's give slice 0 to a different session. For example, the backend one, as an overnight task."* · the types:
*"Yes approved"* (SR-125…SR-141), *"Yes, approved"* (SR-142…SR-143) · the ids: *"I would like it to be switched to 32 bytes"* → *"Should we unify
it as well? We can change the gateways. This is no problem at all."* (CE-4200) · *"accepting your suggestions"* (SV-2 / SV-3 into CE-4200).

## What and why — one paragraph

Every value a PERSON authors that names an entity, a place or an area becomes an AUTHORED type carrying *how* it was written (`EntityLink`,
`PointLink`, `AreaLink` — the resolved answer + an unmanaged `RefSpec`); every value CODE writes stays an INTERNAL type, now with meaning in the
type (`EntityRef` 4 B, `WorldPoint`, `WorldRotation`, `Bearing`). One type-plan walker (today's `EntityRefRemap`, generalised) finds them all by
type for pick, resolve and (later, S3) relocate. In the SAME sweep entity ids become `int32` in the engine AND on the NED wire, with `TkbTypeId`
and `NodeId` typed — so the ~650 files are touched once. ⭐ Design basis: `DESIGN_Scenario_Rules.md` §9.8.0 (read it first — it is the summary
of revs a–f; the subsections below it are the record and contain SUPERSEDED declarations — use §9.8.8 / §9.8.9 / §9.8.10 for the shapes).

## Step 0 — design the details yourself, in the design (frame delegation)

⭐ The decisions are taken; the DETAILS are yours: the assembly each type lives in (lowest one that its users can reference — `SimTransform`'s
assembly decides `WorldPoint`'s), the converters' exact read / write rules per context (file · live wire · diagnostics, SR-138 ③), the
classification of every `Vector3` / `float` angle / `long` id field (the rails' allow-lists), the migration tool's shape. ⭐ Write them INTO
`DESIGN_Scenario_Rules.md` §9.8 as an as-built sub-section (with its own small classDiagram if a box changes) and mark anything you deviate from
SUPERSEDED there — not only in your report (CLAUDE.md, "THE DESIGN MUST REFLECT THE AS-BUILT").

## The items, in this order

| # | what | design basis | ⛔ do not |
|---|---|---|---|
| **1** | ⭐ **the internal types**: `WorldPoint` (12 B, `Vector3`-identical memory + JSON `[x,y,z]`, implicit both ways, `X/Y/Z`), `WorldRotation` (16 B, `Quaternion`-identical; `HeadingDeg` ROUTES to `SimTransformBridgeSystem.HeadingDegToRotation / RotationToHeadingDeg`, `ToString` ROUTES to `OrientationDisplay`), `Bearing` (4 B compass degrees, 0 = N, clockwise) | §9.8.9 SR-141 · §9.8.10 SR-142 / SR-143 | ⛔ a second copy of the compass convention or the HPR text |
| **2** | ⭐ **the authored types**: `RefSpec` (36 B, explicit layout: `FixedString32 Key` overlaid with `Guid Exact` at 0, `RefForm Form` at 32), `EntityLink { EntityRef Ref; RefSpec Spec }`, `PointLink { WorldPoint Local; RefSpec Spec }`, `AreaLink { EntityRef Area; RefSpec Spec; Vector3 Center; Vector2 Size; float HeadingDeg }` + `AreaLink.Contains(world, point)` (circle · oriented rectangle · area entity via `PolygonMath.Contains`); converters: the JSON SHAPE says the form (number · GUID string · `"@Key"` · array · `{circle}` / `{rect}`); resolution FILLS the answer and KEEPS the form; diagnostics write both (`{"NetworkId": n, "Ref": "@Key"}`) and read back (HN-002) | §9.8.8 SR-140 · §9.8.4 SR-134 · §9.8.6 SR-138 | ⛔ any managed field or table (rev c was withdrawn for that); ⛔ a `"kind"` field in JSON |
| **3** | ⭐ **`RefKey`** (saved component, `FixedString32`, unique per scope — loud on a duplicate) + resolution of `@Key` / GUID at SCENARIO scope during load (`StagingEntityExtractor` path), a missing key LOUD (*"no entity with key 'X' here"*) | SR-63…SR-66 · §9.8.3 SR-126 | ⛔ macro-scope lookup — that is S3 |
| **4** | ⭐ **the ONE walker**: generalise `EntityRefRemap`'s type plan — leaves `EntityRef` · `EntityLink` · `WorldPoint` · `PointLink` · `AreaLink` · `WorldRotation` · `Bearing`; appliers remap · resolve · relocate (relocate is unused until S3 — prove it with rails now: translate + rotate a params blob and a component) | §9.8.3 SR-128 / SR-136 · `DESIGN_Entity_Reference.md` D3 | ⛔ a second plan or walker beside it |
| **5** | ⭐ **retype the params / predicate DTOs**: the 21 `EntityRef` params fields → `EntityLink`; the 38 point `Vector3` + 5 `PickableGeoPoint` + 2 lat/lon pairs → `PointLink` (local metres — ⛔ no geodetic form; delete `PickableGeoPoint` and `OrderGeo`'s conversions); bearings → `Bearing`; `SpatialBoundingPredicateDto.Bounds` → `AreaLink`; the 2 raw `long` targets (`HsmFireActivityParams.TargetNetworkId`, `HillAttackDtos.cs:243`) → `EntityLink` if authored; confirm `HeardId` / `CachedEqsRequestId` are working state | §9.8.3 SR-132 · §9.8.4 SR-135 | ⛔ an attribute instead of a type (D5 / P5) |
| **6** | ⭐ **retype the components**: point `Vector3` fields → `WorldPoint` (classify all ~124; directions stay `Vector3`), `SimTransform.Rotation` / `NetworkTransform.LastRotation` → `WorldRotation`, world bearings (`EngagementArea`'s `Left / Right / WatchBearing`, `*BearingDeg` …) → `Bearing`; internal `EntityRef`s STAY `EntityRef` | §9.8.9 · §9.8.10 | ⛔ `EntityLink` in a component (it would always be empty) |
| **7** | ⭐ **CE-4200**: `NetworkIdentity.Value` / `EntityRef` → `int`; every NED entity-id key and entity-naming field → `int32`; `IdRequest` / `IdResponse` `Start / Count` → `uint`; the allocator refuses an id above `int.MaxValue` LOUDLY; the 69 `checked((int)…)` narrowing casts go; ⭐ `TkbTypeId` (`long`, 54 sites) and `NodeId` (`int`, one width — today 26 `int` / 17 `long`) | CE-4200 row · `DESIGN_Semantic_Value_Types.md` SV-2 / SV-3 | ⛔ the local `Entity` handle (index + generation, node-local) |
| **8** | ⭐ **the rails** (each red-proved by a compiling mutation): a bare `Vector3` / `Vector2` / lat-lon / `long` id in a params / predicate DTO fails unless allow-listed · an `EntityRef` in a params / predicate DTO fails (must be `EntityLink`) · a scenario-saved component with a vector member and no "point / not spatial" classification fails · a `float` named `*Bearing* / *Heading* / *Azimuth* / *Yaw*` must be `Bearing` or allow-listed · an id above `int.MaxValue` is refused at the allocator (re-point `AQ107E_AnEntityIdAboveInt32_SurvivesEgressDdsAndIngress`) | SR-132 · SR-143 · CE-4200 | ⛔ a generic sweep detector (CLAUDE.md: one per concept) |
| **9** | ⭐ **migrate the content**: a committed, idempotent **C# tool driven by the same type plan** (load with the old reader, save with the new writer) over `scenarios/`, `recipes/`, `test-data/scenario-corpus/` and the goldens; ⛔ no backward compatibility — an old-form value in a file is a LOUD load error after the migration (CE-4153 S10). Recordings change layout — say so in the report | `DESIGN_Scenario_Entity_Keys.md` §0a finding ③ (a Python pass found 2 false hits) | ⛔ a Python / regex migration |
| **10** | ⭐ **the docs**: `NED_DDS_Protocol.md` (id width, the new types on the wire), AQ107 E → SUPERSEDED by CE-4200, `tools/ai-debug-mcp/SKILL.md` one paragraph on the three JSON forms (SR-138 ④), the as-built folds of step 0 | CLAUDE.md as-built rule | — |

⭐ **Sonnet** for items 5, 6, 7's mechanical retypes and 9's corpus run (CLAUDE.md "SONNET DELEGATION": one warm worker, reused; hand it the
classification you measured — file:line, the type to use, the rail that decides). ⭐ **Your own model** for items 1–4 and 8 (the types, the
converters, the walker, the rails — design judgment) and for every review. ⭐ Commit in small, buildable steps (types → walker → params →
components → ids → content), so a stop leaves a green tree.

⚠ **Stop rules (`R-106`: stop the ITEM, never the batch):** if the implicit-conversion approach fails somewhere (a `ref` alias, a generic
constraint, the blob layout generator, the DDS IDL codegen) — stop THAT item, report the case with file:line, carry on with the rest.
⚠ This is a cross-lane sweep: name every other lane's file you touched in the commit messages and the report.

## Gates *(CLAUDE.md Rule 8 — the report substitutes for the coordinator's run; every row)*

| gate | must report |
|---|---|
| ⭐ **T-1, the features' own suites FIRST** | `Fdp.Toolkits.Tests` `EntityRefTests` + `BehaviorRemappingTests` (the walker) · the map-picking rails named in `DESIGN_Map_Picking_Unification.md` §5 (`ComponentEditDrawerTests.P5_*` in `Fdp.Presentation.Tests`, `MissionPanelRegistryTests` in `Hrot.Presentation.Tests`, the shared-adapter rails) · the trigger PoC (`Fdp.Toolkits.Tests` `TriggerPoc`, 38) · the CE-4153 GUID rails · `Hrot.IG.Tests` `EntityMasterTranslatorTests.AQ107E_*` · the NED translator tests · ⏭ `Hrot.SystemTests` `DeterminismRails` is E2E (T3) — run it in the BACKGROUND, never as a blocker |
| each touched test project's whole suite, `--no-build` after ONE build of the TEST project | pass / fail / skip + delta vs baseline; ⚠ `dotnet restore` each test project once after the merges (the third stale-binary trap) |
| ⭐ **the integration suites that hold the cross-node invariant** — ids and positions cross nodes | `ClusterRunner.Integration.Tests` `--filter`ed to the replication / spawn / ownership / time-sync classes (the full batch hangs — CE-4158, pre-existing: say so with the base sha) · `Hrot.SimHost.Integration.Tests` · `Hrot.IG.Tests` |
| ⭐ **a wire capture** | `ddsmonitor` on a `-m all` or multi-process run: ids are `int32`, a scenario's entities spawn and move on a Player node |
| display rails touched by the pickers / map | under `xvfb-run -a`, real counts (a skip is not a pass) |
| golden movement | as a DIFF SHAPE (files, lines, what changed) — the content migration will move many |
| every red | confirmed PRE-EXISTING against the base sha, named |
| `tracker-counts.py --check` · `design-digest.py --check` · `rulings-check.py` · `mermaid-check.mjs` on every touched design | ✅ / ❌ |
| the working tree after every suite run | clean |
| ids you allocated | from the `backend` block |

## Done when

S0's row in `DESIGN_Scenario_Rules.md` §00.6 holds: the repo's params and components use the types; every rail above is green and red-proved;
a scenario round-trips byte-for-byte except the migrated fields; engine, wire and gateways carry `int32` ids; the multiplayer (CE-4080's
scenario, as far as it runs today) and NED suites pass; the design carries the as-built. ⭐ Report: the table above + what you deviated from and
where the design now says so.

_coordinator lane, 2026-11-01_

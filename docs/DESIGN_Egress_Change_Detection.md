<!--STATUS
state: LIVE
updated: 2026-11-04
build-state: DESIGN — a question for the user / backend; NOT ready to build (no UML until a lean is approved)
current-answer: §2 the measured hazards, §3 the options with a lean
stale-below: none
known-rot: docs/designs/dds-to-ecs/DESIGN.md:468 says "the egress translator checks the chunk version and skips unchanged entities" — only NavigationIntentEgressTranslator does (measured 2026-11-04)
known-conflict: none
related-designs:
  - docs/DESIGN_Evaluation_Cost_And_Cadence.md — OWNS the measurement that put egress at ≈ 15 ms/frame on Crossfire (§0) and filed this as C4 / T10 (CE-2391)
  - docs/designs/dds-to-ecs/DESIGN.md — OWNS egress (translators, ScanAndPublish); its chunk-version line (:468) is the aspiration this design measures
  - docs/projects/FDP/Core/Fdp.Core.md — OWNS the chunk-version rules and QueryDelta (:261, :398, :422–424, :847–876)
  - docs/DESIGN_Ownership_Groups_And_Grants.md — OWNS descriptor authority (a node gaining a descriptor marks it dirty, :400) — the case a version gate would miss
  - docs/DESIGN_Replay_Time_Axis_And_Review.md — OWNS the post-restore ForceMarkAllDirty (:323, :819) — another case a version gate would miss
-->

# Egress change detection — what a "skip unchanged entities" gate would have to get right

> **Tracker:** [CE-2391](blueprints/Blueprint_Issues_Tracker.md) (T10 of [DESIGN_Evaluation_Cost_And_Cadence](DESIGN_Evaluation_Cost_And_Cadence.md) §5 C4).
> 🔒 User, `2026-11-04`: *"the system has to handle thousands entities … if there is anything what would help all entities, it would be welcome"*.

## 0. Why

📐 Crossfire (365 → 524 entities), `--mode all`: `CycloneEgressSystem` ≈ **15 ms/frame** over the four in-process worlds. Every
translator (`EntityInfoEgressTranslator.cs:101–120` is the typical shape) queries **every** entity carrying its component **every frame**,
asks `HasAuthority` (2–3 component reads, root resolution for parts) and only then its own dirty test (`SmartEgressUtil.ShouldPublish`, or a
per-translator cache). ⇒ the cost is translators × entities per frame, whether anything changed or not — linear in the entity count that
must reach thousands.

## 1. INVENTORY *(an explorer pass, 2026-11-04 — graph + grep; ⚠ ~10 translator bodies read, the rest classified by pattern)*

| question | answer |
|---|---|
| the translator interface | `IDescriptorTranslator` (`Fdp.Core/Abstractions/IDescriptorTranslator.cs:82`); `CycloneEgressSystem` calls `INetworkTranslator.ScanAndPublish` (`CycloneEgressSystem.cs:99–111`); `search_graph` `.*EgressTranslator.*` = 93 classes incl. tests |
| how each decides to publish | `ShouldPublish` (reliable, dirty): EntityInfo, MapVisualOverlay, SensorConfig, EntityMission · own change cache: ~25 map translators (Lean, Damage, DoorState, TurretPose, WeaponPosture/Task, ObstacleShape, Possession, Gesture, Ammo …), MapRoute (`RoutePlan.Version`), 8 animation, BrainIntent, EqsSensorConfig, Macro · once per entity: EntityMaster · change + heartbeat: GeoSpatial (600 ticks), NavigationStatus (300) · `QueryDelta` + IntentId: NavigationIntent · always: `MultiInstanceCycloneTranslator` · event-driven (no scan): WeaponFire*, MunitionDetonation, DamageAssessed, spawn / destroy / update commands … |
| who marks dirty | `SmartEgressUtil.MarkDirty` (`:164–182`) from `EcsPatchContext:173`, `OwnershipApplier:105–106`, `UnitHierarchySystem`, `MissionControlExecutionSystem`, `SquadSensing:125`, `UpdateEntityDescriptorRequestSystem:214`, `SimHostApp:863` — ⚠ it mutates a HashSet in place: no chunk version moves |
| chunk versions | one per (component type, chunk of that type), a GlobalVersion STAMP on `GetRW` / `Set` / `Add` / the ECB (`NativeChunkTable.cs:158–161`); NOT on RO reads, raw pointers, or `RemoveUnmanagedComponent`; managed `SetRawObject` / `Clear` bump by +1 (can sit below the current tick); `QueryDelta` (`EntityRepository.DeltaQuery.cs:13–177`) skips a header chunk unless the first slot's type version moved |

## 2. The hazards — why a plain "T's chunk did not change ⇒ skip" gate is WRONG

| # | case | what a T-only version gate does |
|---|---|---|
| H1 | ⛔ **authority arrives** (a grant / takeover makes this node the owner) | the data did not change ⇒ skipped ⇒ the new owner never publishes; `HasAuthority` reads the ROOT's `NetworkAuthority` / `DescriptorOwnership`, a different entity for a part |
| H2 | ⛔ **heartbeats** (GeoSpatial 600 ticks, NavigationStatus 300) | must visit unchanged entities |
| H3 | ⛔ **re-publish after a seek / restore** (`ForceMarkAllDirty`, `WorldEpoch.Moved` cache clears) | touches no chunk |
| H4 | ⛔ **descriptors from several components** (EntityInfo also reads `UnitSubordinate`; GeoSpatial `SimVelocity`) | the gate must OR every input type |
| H5 | ⛔ **managed components mutated in place** (`GetManagedComponentRO` then a write; the +1 bump below the tick) | never seen |
| H6 | ⛔ **`QueryDelta`'s one-slot test** (`DeltaQuery.cs:118–120` assumes T's chunk is wider than the header's — unverified) | can miss a change in the rest of the chunk |
| H7 | ⛔ **GeoSpatial writes its own shadow with `GetComponentRW`** (`:168`) | stamps the chunk every visit ⇒ the gate never skips it |
| H8 | ⛔ **`MarkDirty` moves no version** | an explicit dirty is invisible to the gate |

## 3. Options — ⭐ lean for the user

| option | what | cost / risk |
|---|---|---|
| ⭐ **E1 — a per-world "egress candidates" set, fed by the writers that already exist** | each translator keeps its scan for the FIRST frame and after `ForceMarkAllDirty` / an epoch move; otherwise it visits (a) entities whose input chunks moved since its last scan (OR over its input types), (b) entities `MarkDirty` named, (c) entities whose authority changed (the `OwnershipApplier` / ingress events), (d) its heartbeat slice (1/N of the entities per frame, so a 600-tick heartbeat costs 1/600 of a scan) | ⭐ the only option that covers H1–H4, H8 by construction; H5 needs managed writes to go through `Set` (CLAUDE.md's immutable-managed rule already says so); H7 needs GeoSpatial's shadow write moved out of the gate's input set; H6 needs the per-entity version (`GetVersionForEntity`), not the one-slot test |
| E2 — gate on chunk versions only | the aspiration of dds-to-ecs :468 | ⛔ wrong on H1, H2, H3, H8 — loses publishes silently |
| E3 — cheaper per-entity checks, same scan | hoist `ShouldPublish` / the own cache BEFORE `HasAuthority`; C1's cached type ids (CE-2389, built) already cut each lookup | ⭐ safe and small, but still linear in entities × translators — a first step, not the fix |

⭐ **Lean: E3 now (no semantics change), E1 as its own built design with a rail per hazard H1–H8.** ⛔ Not E2.
⚠ **What would change the lean:** a measurement on a real cluster node (one world, not four) showing egress below ~2 ms at a few thousand
entities — then E3 alone is enough.

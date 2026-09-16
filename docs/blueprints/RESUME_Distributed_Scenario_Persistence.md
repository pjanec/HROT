<!--STATUS
state: LIVE
updated: 2026-09-14
current-answer: THE IMPLEMENTATION RESUMPTION for the distributed-scenario-persistence build
  (CE-275) + the deferred transfer feature (CE-276). Design is DONE and READY-TO-BUILD; NO code
  written yet. Read §1 (decided facts, do NOT re-derive), then §2 (the staged plan), then start
  §2 Stage A. Branch: claude/reset-working-branch-qd1qpv.
related-designs:
  - ../DESIGN_Distributed_Scenario_Persistence.md — the design being built (READY-TO-BUILD).
  - ../reference/BDC_NED_SST_Descriptor_Rules.md — the authoritative wire spec (Stage D compliance).
-->

# RESUME — Distributed Scenario Persistence build (CE-275) + transfer (CE-276)

⭐ **State on `2026-09-14`:** the design is complete, reviewed with the user over many rounds, and
`build-state: READY-TO-BUILD`. **No implementation code written yet.** Gates green (design-digest,
mermaid×4, tracker-counts open 100/done 347). Branch **`claude/reset-working-branch-qd1qpv`**.

⛔ **DO NOT re-open the design questions in §1 — they are USER-RULED.** Read
[`DESIGN_Distributed_Scenario_Persistence.md`](../DESIGN_Distributed_Scenario_Persistence.md) end to
end first; this doc is the build plan, not the design.

## 1. ⭐⭐⭐ DECIDED FACTS — do NOT re-derive (all user-ruled `2026-09-14`)

| # | ruling |
|---|---|
| 1 | **The save gate is per-entity, network-agnostic:** `save entity ⇔ IsPrimaryOwner(entity) AND NOT ScenarioIgnoreTag`. `IsPrimaryOwner` = entity-level `HasAuthority(entity)` = `NetworkAuthority.PrimaryOwnerId == LocalNodeId`, **absent NetworkAuthority ⇒ owned**. ⛔ It must NOT name `EntityMaster`/any wire descriptor (layer: `ScenarioSerializer` is in `Fdp.Toolkits`). Placement: `CollectSaveableEntities` (`Fdp.Toolkits/Scenario/ScenarioSerializer.cs:523-541`). |
| 2 | **Editor is ALREADY a single-node cluster** — it self-hosts `ClusterMaster`+`StorageGatewayModule`+`ClusterSlave` (`EditorSubsystem.cs:318/2074/2092`, ticked `:2590`). It registers only LOAD handlers; save still bypasses via `IEditorLogic.SaveScenarioAs` (`:3864/:3958`). ⇒ NO orchestrator to build; just register the save handler + reroute the save trigger. |
| 3 | **Load model = R-A** (approved in full): the loading brain owns ALL persistable at load; per-node/role ownership preservation (R-B) is a FUTURE feature. Editor round-trip re-owns to brain — accepted. |
| 4 | **Globals → brain file:** the complete non-entity set is `$meta`, `Header.TkbName`, `Zones` (sim-time is already orchestrator-owned). ⛔ CGF composes NO `IZoneManagerService` today (`CgfSubsystem:1053`) → Stage C must give it one or zones are lost. |
| 5 | **Format recognition already exists:** `$meta.docType` graceful-skip in `ScenarioSerializer.Deserialize:327-343`. A host loads only files it recognises; external incompatible-format hosts keep their own. No new mechanism. |
| 6 | **The merge is SAFE:** `NetworkOwnership` and `NetworkAuthority` are field-identical (`{PrimaryOwnerId, LocalNodeId, HasAuthority}`); the 2 production readers repoint identically (cleanup query already `if(!HasAuthority)continue`; `OwnsDescriptor` absent⇒false ≡ ghost(-1)⇒false). Delete `NetworkOwnership`, keep `NetworkAuthority`. `PrimaryOwnerId` STAYS in `NetworkAuthority` (not promoted to Fdp.Core). |
| 7 | **BDC compliance (Stage D / OQ12):** the primary owner IS the `EntityMaster` descriptor owner per the spec; `PrimaryOwnerId` is its network-agnostic ECS mirror. The generic `OwnershipUpdate` transfers it and can originate EXTERNALLY. `OwnershipIngressSystem` today writes `DescriptorOwnership.Map`+`AuthorityMask` but NOT `PrimaryOwnerId` (`:27/:42`) → on `DescrTypeId==dtEntityMaster` it must ALSO write `PrimaryOwnerId=NewOwner` (map `NodeId{Domain,Node}`→int). Egress already stops-without-disposing on loss, so this one write suffices to RECEIVE. |
| 8 | **Two ownership axes stay separate:** ① entity/save = `PrimaryOwnerId` (NetworkAuthority); ② per-component runtime = `AuthorityMask` (Fdp.Core header) moved by grants/auto-takeover. The Muscle taking `SimTransform` never touches axis ①. |
| 9 | **Checkpoint save/load is OUT OF SCOPE** — everyone saves/loads everything, unchanged. |

## 2. ⭐⭐ THE STAGED PLAN — user-approved order (feature-first, cleanup last)

⭐ **Delegation policy (user-approved `2026-09-14`):** *"ok to sonnet for mechanical stuff, but you
still hard check the result."* ⇒ delegate mechanical rewrites (Stage E's ~22 test-fixture edits;
mirror-pattern rails) to a **Sonnet** subagent; **Opus reviews the real diff and re-runs gates.** Do
the semantic/novel spots hands-on.

⭐ **Per-stage discipline** *(CLAUDE.md)*: T-1 find & run the feature's OWN rails first; build the
**affected project** (~8 s) not the solution (~115 s); `--no-build` after the first build; run builds
& E2E in the **BACKGROUND**; each stage = its own commit + push with the gate table in the message.

| stage | scope | key files | verify | risk |
|---|---|---|---|---|
| **A · OQ12 (CE-275 ④)** — do FIRST (tiny, independent, BDC compliance) | on incoming `OwnershipUpdate` with `DescrTypeId==dtEntityMaster`, write `NetworkAuthority.PrimaryOwnerId = NewOwner` (map `NodeId{Domain,Node}`) | `FDP/Toolkits/Fdp.Toolkits/Replication/Systems/OwnershipIngressSystem.cs` | new rail: external EntityMaster `OwnershipUpdate` → receiving node's gate flips to owned, prior owner to not-owned | low |
| **B · Save gate (CE-275 ②)** | add the §1-rule-1 gate to `CollectSaveableEntities` | `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs` | OQ8 rail (editor saves the FULL set) · OQ7 VERIFY parent/child parts ride the gate (`HasAuthority` resolves child→parent) · existing `ScenarioSerializerTests`, `NodeRolePersistenceRails`, `TransientSpawnTagRails` | medium (core) |
| **C · Distributed wiring (CE-275 ③)** | per-node scenario-serialize handler into `FanOutSerializeLocal`; reroute editor save through its own `_clusterMaster` (retire direct `ScenarioFileService.SaveScenario`); give CGF a real `IZoneManagerService` | `Hrot.Orchestrator/ClusterMaster.cs` (+ new handler beside `ReferenceArchiveHandler`), `EditorSubsystem.cs`, `CgfSubsystem.cs` | **T3 E2E `--mode all` (BACKGROUND/async)**: disjoint per-node files, brain carries the tanks, SimHost empty; then a load round-trip. See `docs/RUNBOOK_Cluster_Debugging_Over_Http.md` | higher (structural) |
| **D** | *(D folded into A — OQ12 is the only BDC-receive fix; the transfer INITIATION side is CE-276, deferred)* | — | — | — |
| **E · Merge (CE-275 ①)** — do LAST (mechanical cleanup, delegate) | delete `NetworkOwnership` struct; repoint ~9 prod + ~22 test type-refs to `NetworkAuthority`; drop registration (`HrotSharedComponentRegistry:41`) + drop **NetworkOwnership's** bit-140 entry from the static mask (`StagingEntityExtractor:56`; ⛔ keep NetworkAuthority's bit 51); drop owner-write in `NetworkSpawningSystem:158-162` (keep `:163`). ⛔⛔ **The merge changes NO `DataPolicy` flag** (delete `NetworkOwnership`, repoint, drop its bit-140 static-mask entry). ⭐ MEASURED `2026-09-14` (`DataPolicySaveContextMeasurement`): `DataPolicy` has 3 disjoint bits — `NoScenario` is scenario-ONLY (`ScenarioSerializer`/`GetSaveableMask`), the `.fdp` checkpoint keys on `NoReplay` (`RecorderSystem`/`GetRecordableMask`). ⇒ "NoScenario breaks checkpoint" is FALSE; whether to scenario-exclude `NetworkAuthority` via `NoScenario` is a separate measured-safe CE-277 decision, NOT part of the merge. See design §7 | `FDP/Toolkits/.../NetworkComponents.cs` + ~31 sites across 8 projects | build affected projects green; ALL ownership rails green (list in T-1 below); **grep sweep incl. `HrotStrideApp.Windows`** (out-of-solution) | low semantically, LARGE mechanically |

⚠ **Stage E blast radius MEASURED `2026-09-14`:** ~9 production + ~22 test type-references (the design's
"~8 sites" counted production READERS only). ⭐ Semantic spots to do HANDS-ON: the `.With<NetworkOwnership>()`
query in `CycloneNetworkCleanupSystem.cs:47` (repoint to `NetworkAuthority` — safe because the loop already
`if(!HasAuthority)continue`), the `[DataPolicy]` add, the registration/mask removal, the `NetworkSpawningSystem`
writer. ⭐ Delegate the ~22 mechanical test-fixture rewrites (`new NetworkOwnership{…}`→`new NetworkAuthority(…)`,
`<NetworkOwnership>`→`<NetworkAuthority>`) to Sonnet; hard-check the diff. Use **Roslyn** for the type removal
(`roslyn_find_references` for the true set), never text-replace. ⚠ `NetworkOwnershipExtensionsTests.cs` targets
the existing helpers, not a hidden class — no separate reader.

## 3. T-1 — the feature's own rails (run these first, per stage)

- **Ownership / merge (Stage E):** `Fdp.Toolkits.Tests/Replication/{AuthorityExtensionsTests,ComponentTests,DataPolicyTests}.cs`,
  `Fdp.ModuleHost.Tests/Network/NetworkOwnershipExtensionsTests.cs`, `Fdp.Toolkits.Tests/NetworkSpawning/*`,
  `Hrot.SimHost.Tests/CycloneNetworkCleanupSystemTests.cs`.
- **Save gate / persistence (Stage B):** `Hrot.SimHost.Tests/NodeRolePersistenceRails.cs`,
  `Fdp.Toolkits.Tests/NetworkSpawning/TransientSpawnTagRails.cs`, the `ScenarioSerializer` tests,
  `Hrot.Map.Common.Tests/Replication/Egress/NedEntityCreationRequestEgressRails.cs`.
- **Distributed (Stage C):** `Hrot.Orchestrator.Tests/*Archive*/*Consensus*`, `ClusterRunner.Integration.Tests/DebugApiScenarioLoadTests.cs`.

## 4. First action on resume

1. `git fetch origin claude/reset-working-branch-qd1qpv` and confirm HEAD carries this doc; tree clean.
2. Re-read [`DESIGN_Distributed_Scenario_Persistence.md`](../DESIGN_Distributed_Scenario_Persistence.md) §6/§6a/§6c and §8.
3. **Start Stage A** (OQ12): T-1 the ownership rails, then the one-write change in `OwnershipIngressSystem`, then a rail. Small, independent, proves the loop.
4. Commit+push per stage with the gate table; keep `build-state` and CE-275's tracker row in step.

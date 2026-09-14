<!--STATUS
state: LIVE
updated: 2026-09-14
build-state: BUILDING
build-progress: Stage A (CE-275 ④ / OQ12) + Stage B (CE-275 ② the save gate) BUILT & GREEN `2026-09-14`.
  Stage A: OwnershipIngressSystem mirrors an EntityMaster-ordinal transfer into NetworkAuthority.PrimaryOwnerId
  (master ordinal injected network-agnostically via DescriptorOwnershipMap.PrimaryOwnerDescriptorOrdinal,
  set by NedReplicationModule; 3 rails in OwnershipTests). Stage B: CollectSaveableEntities now gates on
  HasAuthority (absent⇒owned) as well as ScenarioIgnoreTag; OQ7 (parts ride parent) + OQ8 (editor saves all)
  RESOLVED with rails in AuthorityExtensionsTests + ScenarioSerializerTests. Fdp.Toolkits.Tests 82/82,
  Hrot.Network.NED build clean.
  Stage C1+C2 BUILT & GREEN `2026-09-14` (see §4 AS-BUILT): one host-neutral ScenarioSaveCore; the ONE
  HrotScenarioSaveHandler; ScenarioFileService reduced to a shim; ClusterMaster SaveScenarioJson routing;
  EditorScenarioSession.SaveAs/SaveCurrent reroute (editor AND CGF go through the cluster, no local write);
  handler registered on editor + CGF. Rails: HrotScenarioSaveHandlerTests 3, EditorScenarioSessionSaveTests 3,
  ScenarioFileService shim tests green, orchestration struct tests 55/55.
  Stage C3 BUILT & GREEN `2026-09-14`: the SAME handler now registered on ALL FOUR hosts (editor, CGF, SimHost,
  IG — IG included per user ruling, its file empty by the gate not a missing handler); NodeRolePersistenceRails
  rewritten (IG must not hold an UNGATED checkpoint handler, but DOES hold the gated scenario handler; R-140
  enforced by the gate) — 10/10; Node_Roles §7.1 marked superseded. IG + SimHost + editor + CGF build clean.
  ⇒ scenario save is UNIFIED across every host; there is no editor-only save path.
  REMAINING: CE-277 follow-ons (OQ1 CGF zone service; retire raw-path SaveTo; multi-process staging+NAS pull;
  T3 --mode all E2E), Stage E (NetworkOwnership→NetworkAuthority merge — ⛔ NO NoScenario flag, §7).
current-answer: §4 save flow, §5 load flow (R-A, ruled §8.1), §6 the ONE gate — keyed on the
  NETWORK-AGNOSTIC primary-owner fact (NetworkAuthority.PrimaryOwnerId, entity-level HasAuthority;
  absent⇒owned), NEVER a wire descriptor, §6a globals (brain-owned), §6b format recognition, §6c the
  ECS↔network seam (primary owner is an ECS fact; transport DERIVES EntityMaster from it; transfer
  updates PrimaryOwnerId ⇒ save follows), §7 the NetworkAuthority/NetworkOwnership merge, §1a paradigm
  correction. ⭐ ALL blockers + OQ6 closed `2026-09-14`; OQ9 corrected (editor is ALREADY a single-node
  cluster). §6c reconciled with the BDC/NED spec: EntityMaster ownership (transferable via the generic
  OwnershipUpdate, possibly EXTERNAL) IS the primary owner; PrimaryOwnerId is its ECS mirror. OQ12 (the
  OwnershipUpdate→PrimaryOwnerId sync for receiving an external transfer) is PULLED INTO the build
  (CE-275); the transfer INITIATION feature stays deferred (CE-276). Build = CE-275 (4 steps: merge,
  gate+rail, per-node handler+editor reroute+CGF zone service, OQ12 sync); build-time OQ7 (verify parts)
  + OQ8 (rail). ⇒ BUILDABLE; awaiting go-ahead.
stale-below: nothing yet (new document).
known-rot: nothing known.
known-conflict: DESIGN_Node_Roles_And_Policies.md §7.1 says IG-persistence is enforced "by an
  ABSENCE" (IG registers no save handler). ⭐ THIS design supersedes that enforcement with a
  UNIFORM per-entity GATE (§6): every host runs the same gated save; IG's file is empty by the
  gate, not by a missing handler. §7.1's ABSENCE becomes belt-and-suspenders, not the mechanism.
superseded-by: —
design-basis:
  - docs/DESIGN_Node_Roles_And_Policies.md §4 (ownership axes), §5 (persistence policy R-140),
    §7.2/§7.3 (the ungated save, measured), §8 ① (the parked save question)
  - docs/designs/cgf-scn-2/DESIGN.md (scenario serialization correctness; DataPolicy.NoScenario)
  - docs/designs/cgf-scn/DESIGN.md (CGF as authoritative genesis source on LOAD)
  - docs/DESIGN_Entity_Creation_Unification.md (the shared creation pipeline; §3.4b level mismatch)
  - docs/UX/UX_Feature_Authority_Aware_Writes.md §335/§342/§343 (the NetworkAuthority/NetworkOwnership
    duplication named as debt; "pick NetworkAuthority"; absent-component = owned)
  - docs/blueprints/RULINGS.md R-138 (fully distributed), R-140 (IG passive/non-persisting)
  - docs/reference/BDC_NED_SST_Descriptor_Rules.md (AUTHORITATIVE wire spec: entities-as-descriptors,
    EntityMaster lifecycle, per-descriptor ownership, the generic OwnershipUpdate transfer — §6c maps it
    onto our ECS and records the PrimaryOwnerId-mirror compliance gap)
related-designs:
  - DESIGN_Node_Roles_And_Policies.md — owns the ROLE/ownership POLICY (who may own what, R-138/R-140);
    THIS doc owns the SAVE/LOAD MECHANISM that enforces it and the ownership-component unification.
  - docs/designs/cgf-scn-2/DESIGN.md — owns per-COMPONENT-TYPE save correctness (which components are
    NoScenario, serializer truncation); THIS doc owns per-ENTITY save selection (which entities each node saves).
  - docs/designs/cgf-scn/DESIGN.md — owns the LOAD genesis pipeline (scenario JSON → creation requests);
    THIS doc owns which FILE(S) each node loads and re-ownership at load.
  - DESIGN_Entity_Creation_Unification.md — owns the CREATION pipeline whose OwnerNodeId stamps the
    save-ownership THIS doc gates on. ⭐ ALSO owns the measured analysis that `NetworkAuthority` is
    runtime-only. ⚠ SUPERSEDED `2026-09-14`: the `2026-09-02` withdrawal (which said "use the extractor
    context-mask, NOT a global flag") is REVERSED by CE-277(e) — `[DataPolicy(DataPolicy.NoScenario)]` IS now
    applied to `NetworkAuthority` + `DescriptorOwnership` (the process-local pair; NOT `NetworkIdentity`/
    `TkbIdentity`, whose data the LOAD path reads from the file — measured, 10 rails). Stage E adds NO flag; §7.
  - DESIGN_Role_Affinity_Ownership.md — owns WHO OWNS WHICH COMPONENT (the role-affinity tables
    REGISTER = owned ∪ read, AUTHORITY = owned; the per-component AuthorityMask). It DECIDES the
    per-component/per-entity ownership; THIS doc only READS the primary-owner fact at save time and
    never decides it. The per-component AuthorityMask it owns is axis ② here (§3); PrimaryOwnerId is axis ①.
  - DESIGN_Entity_Genesis_End_To_End.md — owns the end-to-end genesis STAGE SEQUENCE and vocabulary
    (request → spawn → grant → ghost → takeover → Active). Its takeover stage is where per-component
    authority moves and where a future primary-ownership transfer (§6c) would sit; THIS doc consumes the
    resulting owner at save time, it does not move it.
  - UX_Feature_Authority_Aware_Writes.md — owns the AUTHORITY-GATED WRITE UX and first named the
    NetworkAuthority/NetworkOwnership duplication (§335/§342); §7 here is the merge it deferred.
-->

# ⭐⭐⭐ Unified Distributed Scenario Persistence & the Single Ownership Component

> **Two decisions, one document.**
> **① Scenario SAVE and LOAD are ONE cluster-orchestrated, ownership-gated operation** — the editor
> is just the degenerate single-node (all-in-one, brain-perspective) case, not a separate path.
> **② `NetworkOwnership` is merged into `NetworkAuthority`** — one component, one responsibility:
> *entity-level primary / save ownership*. Per-**component** runtime authority stays a separate axis.
> **⛔ Checkpoints are OUT OF SCOPE** — they save/load *everything* on *every* host regardless of
> ownership, by design; nothing here changes them.

📌 **Why this document exists.** The save path has **no ownership gate** — `CollectSaveableEntities`
filters only `ScenarioIgnoreTag` (measured, `DESIGN_Node_Roles §7.3 ④`). That is harmless with ONE
saver (the editor owns everything) and **wrong** the moment two nodes save: each writes its ghosts too.
And two field-identical ownership components (`NetworkAuthority`, `NetworkOwnership`) encode one concept,
named "debt" in `UX_Feature_Authority_Aware_Writes.md` but never merged.

---

## 1. ⭐⭐ THE MODEL IN ONE PARAGRAPH

Every host runs the **same** gated save with **no role exceptions**. A host writes to its **own**
per-node scenario file **exactly the non-transient entities it is the entity-level primary owner of**
(`view.HasAuthority(entity)`), whatever its role — a host owns an entity because it *created* it, and a
host creates entities because of the roles it was assigned, **not** because it is "a brain" or "passive".
⛔ There is **no** "IG never persists" special case: if any host owns a savable (non-transient) entity —
e.g. the editor's or an IG's 2D-map role authored a persistable tactical drawing — that host saves it,
full stop (see §1a). A host that happens to own nothing savable writes an empty file — harmless. The
**editor** is the degenerate single node that hosts **all** roles, so it owns everything and writes the
whole scenario as **one** file; that file equals a brain-role file **only circumstantially** — because
the other roles usually own little that is savable, not by definition. **One rule — save an entity iff
you are its non-transient primary owner — makes the editor, the brain, the IG and every future partition
fall out of the same code.** ⚠ **The multi-file ↔ single-file LOAD round-trip is NOT yet fully resolved —
see §8 OQ11, the central open question.**

### 1a. ⭐⭐⭐ PARADIGM CORRECTION `2026-09-14` — passivity is EMERGENT, not designed

> 🔒 **User, verbatim:** *"'IG is passive, non-persisting' — this is the old paradigm. Hosts are not
> passive by design; passivity results from whether they create their own entities, which comes from the
> roles they are assigned. Any single host can create entities (thus be their primary owner) so if that
> happens and the entity is savable to scenario (non transient) also the IG must save it to the scenario,
> no exceptions, no differences, unified rules and implementation."*

⛔ This **refines `R-140`** (`DESIGN_Node_Roles §5`, "IG is passive and non-persisting"): R-140's
*behaviour* still usually holds — an IG typically owns only transient entities — but as an **emergent
consequence of its role assignment, NOT a hard rule the save path enforces.** ⇒ the save path is
**uniform**; the only gate is ownership + transience. `DESIGN_Node_Roles §5/§7` must be updated to say
"passivity is emergent" rather than "IG may not persist" (recorded as a follow-up there).

---

## 2. 🔴 INVENTORY — measured 2026-09-14 (grep + code read; codebase-memory MCP flaky this session)

> ⚠ codebase-memory MCP dropped mid-session; the enumerations below are grep/`find.sh` + direct reads.
> Re-verify the exhaustive claims (reader counts) with `search_graph` when the graph is back.

| # | thing | measured |
|---|---|---|
| ① | the save gate | `ScenarioSerializer.CollectSaveableEntities` (`Fdp.Toolkits/Scenario/ScenarioSerializer.cs:523-541`) skips **only** `ScenarioIgnoreTag`; no ownership/ghost filter |
| ② | the entity-JSON writer | `ScenarioFileService.SaveScenario` → `ScenarioSerializer.Serialize` (`Hrot.Presentation/.../ScenarioFileService.cs:114-120`); built by the **editor** (`EditorSubsystem:1314`) AND **CGF** (`CgfSubsystem:1058`, over CGF's own world via `EditorScenarioSession`) |
| ③ | the distributed cluster save today | `ClusterMaster.SaveScenario` → `FanOutSerializeLocal(all active nodes)` (`ClusterMaster.cs:983-987`) → each node's `SerializeLocal` handler is **`ReferenceArchiveHandler`**, which only **reports** an existing per-node `node_{id}.fdp` — ⛔ **it does NOT run the scenario-JSON serializer.** ⇒ there is **no per-node gated scenario-JSON writer today**; the fan-out collects **checkpoint recordings** |
| ④ | the save-ownership helper (already exists) | `AuthorityExtensions.HasAuthority(view, entity)` (`Fdp.Toolkits/Replication/Extensions/AuthorityExtensions.cs:9`): resolves part→parent, skips the per-descriptor override at key 0, returns `NetworkAuthority.HasAuthority`; **absent component ⇒ true (owned)** (`:33-40`) |
| ⑤ | the two ownership structs | `NetworkAuthority` (`.../Components/NetworkAuthority.cs`) and `NetworkOwnership` (`.../Components/NetworkComponents.cs:20`) are **field-identical** `{PrimaryOwnerId, LocalNodeId, HasAuthority}`; `NetworkOwnership`'s per-descriptor `Map` was removed (BATCH-07) |
| ⑥ | `PrimaryOwnerId` (the owner node-id) | **write-once at spawn today**: `NetworkSpawningSystem.cs:160/163` (owner), `EntityMasterIngressTranslator.cs:149` (ghost = `-1`). It is the **network-agnostic source of truth** for primary/save ownership (`NetworkAuthority`, `Fdp.Toolkits`, no DDS). ⚠ Nothing updates it after spawn **yet** — an entity-transfer FEATURE would (§6c). The transport (`EntityMaster`) is **derived** from it; the per-descriptor `DescriptorOwnership`/`AuthorityMask` transfer path is for per-COMPONENT authority, not entity ownership |
| ⑦ | `NetworkAuthority` readers | **~57** production sites via `.HasAuthority`; present on owner AND ghost |
| ⑧ | `NetworkOwnership` readers | **2** production sites — `CycloneNetworkCleanupSystem.cs:47/52` (query then `if(!HasAuthority) continue`) and `OwnershipExtensions.OwnsDescriptor*` (absent ⇒ false) |
| ⑨ | LOAD authority | `CgfScenarioLoadHandler` / `CgfEpisodeLoadHandler` call `StagingEntityExtractor.Extract(serializer, json, idAllocator)` → `EntityCreationRequest[]` → genesis pipeline; the extractor **strips** `NetworkAuthority`/`NetworkOwnership`/`NetworkIdentity` (`HROT-Engine-Guide §166`, `StagingEntityExtractor:52/56`) |
| ⑩ | global scenario data | `ScenarioFileService` also writes a **`Zones`** section from `IZoneManagerService` (`:122-125`). ⛔ **CGF composes NO zone service** (`CgfSubsystem:1053`, `zoneService: null`) — zones are **editor-only** today |
| ⑪ | IG persistence enforcement | IG registers **no** save handler (`DESIGN_Node_Roles §7.1`); SimHost/CGF/ExCon **do** register `ReferenceArchiveHandler` |

---

## 3. ⭐⭐⭐ THE OWNERSHIP MODEL — one component per axis

> **Diagram first.** The class diagram is the design; the prose says only *why*.

```mermaid
classDiagram
    class NetworkAuthority {
        <<struct · SURVIVOR>>
        +int PrimaryOwnerId
        +int LocalNodeId
        +bool HasAuthority()  «PrimaryOwnerId == LocalNodeId»
        note "ENTITY-level = SAVE / primary ownership.
        Present on owner AND ghost. Write-once at spawn.
        NoSave. absent ⇒ owned (editor/AllInOne)."
    }
    class NetworkOwnership {
        <<struct · DELETED>>
        +int PrimaryOwnerId
        +int LocalNodeId
        note "field-identical duplicate; 2 readers repointed to NetworkAuthority"
    }
    class DescriptorOwnership {
        <<managed>>
        +Map~long,int~ Map
        note "per-DESCRIPTOR / per-component authority override. Drives AuthorityMask. NOT the entity primary owner."
    }
    class AuthorityMask {
        <<entity-header bits>>
        note "per-component authority bits. Flipped by SetAuthority()."
    }
    NetworkOwnership ..> NetworkAuthority : merged into
    DescriptorOwnership --> AuthorityMask : drives
    NetworkAuthority --> AuthorityMask : entity-level vs per-component (independent)
```

*Caption — what the picture shows that prose hid:* two distinct axes. **① entity / SAVE ownership** =
`NetworkAuthority.PrimaryOwnerId` — the **network-agnostic** owner node-id, *who deletes and who saves*.
**② per-component runtime authority** = `AuthorityMask` (core header) — *who simulates a component this
frame*, moved by the grant / `DescriptorOwnership`. The Muscle taking `SimTransform` flips an `AuthorityMask`
bit and never touches axis ① — so the save gate is immune to runtime authority moves. A genuine **primary
transfer moves axis ① (`PrimaryOwnerId`)**, and the save gate follows because it reads that fact (§6c). The
transport's `EntityMaster` ownership is **derived** from axis ①, not a third store. `NetworkOwnership` was a
redundant copy of axis ① and is deleted.

| axis | store | layer | mutated by | answers |
|---|---|---|---|---|
| **① entity / SAVE ownership** | `NetworkAuthority.PrimaryOwnerId` | toolkit (transport-agnostic) | default at spawn; a **transfer** feature (rare) | *do I delete / save this entity?* |
| **② per-component runtime authority** | `AuthorityMask` (+ `DescriptorOwnership`) | core header + toolkit | grant / auto-takeover (per frame) | *do I simulate this component now?* |

---

## 4. ⭐⭐⭐ SAVE — the unified, ownership-gated, cluster-orchestrated flow

```mermaid
sequenceDiagram
    autonumber
    actor Op as Operator / editor UI
    participant CM as ClusterMaster (orchestrator)
    participant Brain as Brain node (CGF)
    participant Muscle as Muscle node (SimHost)
    participant IG as IG node
    participant NAS as Shared store + manifest

    Op->>CM: SaveScenario(name)
    CM->>Brain: SerializeLocal
    CM->>Muscle: SerializeLocal
    CM->>IG: SerializeLocal
    Note over Brain,IG: SAME gated code on every host
    Brain->>Brain: for each live entity:<br/>keep iff IsPrimaryOwner(entity)<br/>AND not ScenarioIgnoreTag
    Brain->>NAS: node_brain.scn (owned slice + globals)
    Muscle->>Muscle: owns nothing persistable
    Muscle->>NAS: node_muscle.scn (EMPTY — fine)
    IG->>IG: owns only transient (ScenarioIgnoreTag)
    IG->>NAS: node_ig.scn (EMPTY — fine)
    NAS->>CM: manifest {brain, muscle, ig}
    CM-->>Op: saved (scenario = the file SET)
```

*Caption:* the fan-out already exists (`ClusterMaster.FanOutSerializeLocal`, INVENTORY ③); **what is new
is the per-node handler that RUNS the gated `ScenarioSerializer` instead of only reporting a checkpoint
recording.** Every host runs identical code; **content differs only by what each host owns.** The editor
collapses this to one node that owns everything ⇒ one non-empty file.

### Module / registration view — who runs the save, and the ONE dead edge

```mermaid
graph TD
    subgraph SGsave["Scenario save · ownership-gated · THIS design"]
      CM["ClusterMaster<br/>FanOutSerializeLocal(all nodes)"]
      H["HrotScenarioSaveHandler (NEW, one class every host registers)<br/>CanHandle(SerializeLocal) on a ScenarioSaveHandlerPayload<br/>writes via the shared host-neutral ScenarioSaveCore (gated)"]
      G["view.HasAuthority(entity)<br/>the ONE gate"]
      CM -->|SerializeLocal| H
      H --> G
      H -->|per-node .scn| NAS["manifest + shared store"]
    end
    subgraph SGcp["Checkpoint · OUT OF SCOPE · unchanged"]
      RC["ReferenceCheckpointHandler<br/>+ CheckpointIOWorker"]
      RC -->|"node_id.fdp · EVERYTHING, no gate"| NAS
    end
    IGX["IG node · SAME handler, no exception"] -. "saves whatever it OWNS<br/>(often nothing savable → empty; a savable drawing → saved)" .-> H
    style RC fill:#eee,stroke:#999,stroke-dasharray:5 5
```

*Caption — what the picture shows that prose hid:* the checkpoint lane (grey) is a **separate** mechanism
that ignores ownership; do not fold it in. ⭐ **Every host — IG included — runs the identical handler with
no role branch**; the content of each file is purely *what that host owns*. This retires
`Node_Roles §7.1`'s "enforce by a missing IG handler": there is no missing handler and no IG special case
(§1a). An IG file is empty *when* the IG owns nothing savable, not *because* it is an IG.

#### ⭐ AS-BUILT (Stage C, `2026-09-14`)

- **One host-neutral save core.** `ScenarioSaveCore.Write` (`Hrot.Core`, namespace `Hrot.Map.Common.Scenario`)
  is the ONLY scenario-save implementation — gated `ScenarioSerializer` + `$meta` + this host's zones. ⛔ There
  is NO editor-specific save: `ScenarioFileService.SaveScenario` is now a THIN SHIM over the same core, and
  every host's one `HrotScenarioSaveHandler` calls it. *(The §4 diagram's `NodeScenarioSerializeHandler` frame
  name is realised as `HrotScenarioSaveHandler` reusing the shared core.)*
- **Trigger → orchestrator.** `EditorScenarioSession.SaveAs/SaveCurrent` (the ONE session class — editor AND
  CGF) publish `ExecuteStorageOpIntent{ Operation=SaveScenarioJson, ScenarioName }`;
  `ClusterMaster.ProcessStorageOpIntent` fans `SerializeLocal` out with a `ScenarioSaveHandlerPayload`
  (distinct from the `.fdp` archive payload, so `ReferenceArchiveHandler` and the scenario handler coexist).
- **Name, not path.** The operator picks a relative name / subfolder under the NAS scenarios root; no
  filesystem path is ever chosen. The raw-path `EditorScenarioSession.SaveTo` has no production caller and is
  scheduled for removal (C3).
- **File target.** For the single-authoritative-node case (editor / CGF brain owning all persistable, R-A) the
  handler writes `<scenariosRoot>/<name>/scenario.json` in place and reports NO manifest (nothing to pull).
  ⚠ Multi-process staging + NAS pull + per-node file names remain a follow-on for a true multi-owner save.
- **Registered by every host** (unification, no IG exception): editor + CGF built `2026-09-14`; SimHost + IG
  land in C3 with the `NodeRolePersistenceRails` update (IG carries the handler; the gate — not a missing
  handler — keeps its file empty).

---

## 5. ⭐⭐ LOAD — per-node file, brain canonical  ✅ R-A (ruled §8.1)

```mermaid
sequenceDiagram
    autonumber
    participant CM as ClusterMaster
    participant Brain as Brain (CGF)
    participant Muscle as SimHost
    CM->>Brain: LoadScenario(set)
    CM->>Muscle: LoadScenario(set)
    Brain->>Brain: read node_brain.scn
    Brain->>Brain: StagingEntityExtractor makes EntityCreationRequests<br/>ownership STRIPPED in file, loader re-stamps
    Brain->>Brain: genesis create and OWN, PrimaryOwnerId = Brain
    Brain-->>Muscle: replicate to ghosts, PrimaryOwnerId = -1
    Muscle->>Muscle: read node_muscle.scn empty, nothing
    Note over Brain,Muscle: runtime Brain GRANTS per-component authority<br/>AuthorityMask to Muscle, PrimaryOwnerId unchanged
```

*Caption:* load re-establishes save-ownership on the loading (brain) node because the file **strips**
ownership (INVENTORY ⑨) and the genesis pipeline stamps `OwnerNodeId = loader`. A muscle's empty file is
correct — it receives entities by **replication**, and per-component authority arrives later by **grant**,
never changing `PrimaryOwnerId`. This is why the round-trip is stable: *save-owner in = save-owner out*.

---

## 6. ⭐⭐⭐ THE ONE GATE

```
save/keep entity  ⇔  IsPrimaryOwner(entity)  AND  NOT ScenarioIgnoreTag(entity)
                       // IsPrimaryOwner = HasAuthority(entity) at entity level = NetworkAuthority.PrimaryOwnerId
                       //                  == LocalNodeId, with absent NetworkAuthority ⇒ owned
```

- ⭐⭐⭐ **Key on the NETWORK-AGNOSTIC primary-owner FACT, never on a wire descriptor** — see §6d. The save
  gate reads the ECS-level owner (`NetworkAuthority.PrimaryOwnerId`, a transport-agnostic component), so it
  works identically under NED, BDC, or no transport at all. ⛔ It must **not** name `EntityMaster` — that is
  a NED wire concept, and the gate lives below the transport (`ScenarioSerializer` is in `Fdp.Toolkits`).
- `HasAuthority(entity)` (INVENTORY ④): **absent `NetworkAuthority` ⇒ owned** (editor / AllInOne); else
  `PrimaryOwnerId == LocalNodeId`.
- **Editor / AllInOne** ⇒ owns everything ⇒ saves everything. No mode flag; the gate is always on.
- **Ghost** ⇒ `PrimaryOwnerId = -1` ⇒ false ⇒ skipped ⇒ no duplication across per-node files.
- `ScenarioIgnoreTag` still gates **my own transient** entities (a sketch I own but must not persist).
  The ownership gate makes the *ghost* case redundant, but the *own-transient* case keeps it necessary.

⚠ **Placement:** the gate goes in `CollectSaveableEntities` — one site, editor and cluster both inherit it.
`HasAuthority` and `NetworkAuthority` are both in `Fdp.Toolkits` (same assembly as `ScenarioSerializer`)
⇒ **no new dependency and no layer seam.** *(This reverts an earlier draft that keyed on the EntityMaster
descriptor and needed a NED ordinal injected — that was the wrong layer; see §6d.)*

### 6a. ⭐⭐ GLOBAL / NON-ENTITY DATA — the complete set, owned by the brain file *(ruling `2026-09-14`)*

📐 **Measured — a scenario carries exactly this much that is NOT a per-entity component:**

| datum | where it lives today | owner under this design |
|---|---|---|
| `$meta` (`docType`, `schemaVersion`) | DOM envelope (`ScenarioSerializer.Serialize:200`) | **brain file** |
| `Header.TkbName` (active TKB database) | DOM `Header` (`:198-199`) | **brain file** |
| `Zones` (tactical zones) | DOM `Zones`, from `IZoneManagerService` (`ScenarioFileService:124`) — ⛔ **editor-only today**; CGF passes `zoneService: null` | ⭐⭐ **brain** *(user ruling: "zones should for sure be handled by brain")* ⇒ the brain node must compose a real `IZoneManagerService` |
| scenario **sim-time** | already **cluster/manifest** level (`GlobalContextClusterOpHandler.ScenarioTimeSeconds`), **not** in any per-node DOM | **orchestrator context** — already global, no change |

⭐ **The rule:** all per-node-DOM globals ride the **brain-role file** (the canonical scenario), because
the brain is the node that owns the scenario as a whole. ⛔ **The one build consequence:** the brain node
(CGF) must gain a real `IZoneManagerService` — today only the editor has one, so a headless-CGF save would
drop zones. Sim-time needs nothing — it is already orchestrator-owned.

### 6b. ⭐⭐ FORMAT RECOGNITION — a host loads only files it understands *(ruling `2026-09-14`)*

> 🔒 **User:** *"There is still a case the host uses incompatible scenario format (host being external
> software, interacting with others on orchestration network level only); in such a case that host
> requires its own editor that maintains that host-specific scenario file; meaning our current scenario
> editor capabilities must recognize the scenario file format and load just those compatible ones."*

⭐⭐ **The mechanism already exists.** Every scenario file stamps a format tag — `$meta.docType` (legacy:
`Header.SubsystemType`) — and `ScenarioSerializer.Deserialize` **gracefully skips** a file whose tag does
not match the loader's `_subsystemType` (`ScenarioSerializer.cs:327-343`, `return;` — no entities created).

| ⭐ the rule | |
|---|---|
| a host loads **only** the partial files whose **format tag it recognises** | ⇒ an external host that speaks a **different scenario format** keeps its OWN file, maintained by **its own editor**, and interacts with the cluster only at the **orchestration-network** level |
| our editor/CGF **skips** a foreign-format file silently | ✅ this is today's `Deserialize` behaviour — no new mechanism, just make it explicit in the load rule |
| ⚠ within R-A, "compatible" partials all funnel into the loading brain | ⛔ incompatible ones do **not** — they are that host's responsibility, the one place "different hosts save/load their own content" still literally holds |

⇒ ⭐ **R-A unifies the SAME-format hosts; the format tag is the boundary** past which a host is on its own.

### 6c. ⭐⭐⭐ THE ECS ↔ NETWORK SEAM — where ownership lives, who publishes, how transfer works

> 🔒 **User:** *"the ECS core is fully network agnostic. Where is the primary owner stored now in the ECS?
> How does the ECS host know it should publish EntityMaster? How can an ECS host initiate entity transfer
> resulting in the ownership transfer of the EntityMaster network descriptor?"*

📐 **Measured layering — three layers, and ownership is split across them:**

| layer | assembly | ownership state it holds |
|---|---|---|
| ⭐ **ECS core — network-AGNOSTIC** | `Fdp.Core` | **`AuthorityMask`** (`BitMask512` in the entity header, `EntityMetadataCold.cs:17`) — a **per-component boolean**: *"do I own component X?"* ⛔ **No owner node-id.** The core also reserves the component-id constants (`NetworkAuthority=51`, …) but does **not** define the structs |
| ⭐⭐ **replication toolkit — transport-agnostic** | `Fdp.Toolkits/Replication` | **`NetworkAuthority.PrimaryOwnerId`** (the owner **node-id** — plain int, no DDS knowledge) and **`DescriptorOwnership.Map`** (per-descriptor override). This is where *"who is the primary owner"* actually lives |
| ⛔ **transport — NED/BDC** | `Hrot.Network.NED` | the **`EntityMaster`** DDS descriptor + translators. The **only** layer that knows the word "EntityMaster" |

⭐⭐⭐ **The correcting insight:** the primary owner has **two faces of ONE fact** — the **network-agnostic ECS
mirror** `NetworkAuthority.PrimaryOwnerId` (an int, transport-agnostic) and the **wire owner** of the
`EntityMaster` descriptor (BDC-authoritative). They are kept in sync **bidirectionally**: for an entity we own,
the wire is **published from** `PrimaryOwnerId`; for an **incoming** transfer, the `OwnershipUpdate` is the
source and **writes** `PrimaryOwnerId` (the compliance sync, §6c rule 3). The **save gate reads the ECS mirror**
so it stays network-agnostic. So:

- **Q1 — where is it stored?** As component data: the owner **node-id** in `NetworkAuthority.PrimaryOwnerId`
  (toolkit, transport-agnostic), and the per-component **authority bits** in the core `AuthorityMask`
  (`Fdp.Core`, fully network-agnostic). ⛔ It is **not** `EntityInfo` (that holds only `Name`/`ForceId`), and
  there is **no owner-id in the core header** — only the boolean mask. *(Open choice below: should the owner-id
  move into the core header to be truly core-level?)*
- **Q2 — how does a host know to publish `EntityMaster`?** It doesn't, at the ECS level. The **network layer's**
  `EntityMasterEgressTranslator` runs each replication tick, scans the ECS, and **derives** the decision from
  the authority state — it publishes/deletes `EntityMaster` for entities this node is authoritative over
  (`EntityMasterEgressTranslator.cs:67-74`). The mapping *"primary owner → publish EntityMaster"* is **entirely
  in the transport layer**; the ECS core never names the descriptor.
- **Q3 — how does an ECS host initiate a transfer?** ⭐⭐⭐ **The native, consistent way: transfer OWNERSHIP
  OF THE `NetworkAuthority` COMPONENT** — since `PrimaryOwnerId` is a field of `NetworkAuthority`, entity
  ownership is just *"who owns the `NetworkAuthority` component,"* and it moves by the **same generic
  per-component authority-transfer** mechanism as everything else (`DeferredTakeOwnershipCommand` →
  `OwnershipUpdate` → `AuthorityMask`). **No special "entity transfer" primitive.** The new owner then holds
  authority over `NetworkAuthority` and reflects it in `PrimaryOwnerId`; the save gate and the derived
  `EntityMaster` wire ownership both follow.
  ⚠ **Measured gap (so this is a FEATURE, not a claim it works today):** `NetworkAuthority` is **not** a
  replicated descriptor and is **not** a `TargetComponent` of any translator (so the generic mechanism can't
  yet reach it), and its VALUE is set locally (owner at spawn `NetworkSpawningSystem:158`; ghost `= -1`
  `EntityMasterIngress:149`), never replicated. ⇒ the transfer feature must make `NetworkAuthority`
  **ownership-tracked / replicated** (register it as a descriptor target, or replicate its value) so the
  generic transfer moves it and the new `PrimaryOwnerId` propagates.

#### ⭐ The rule this design commits to — so transfer is ENABLED, not prevented

1. ⭐⭐⭐ **`PrimaryOwnerId` (the network-agnostic ECS fact) is the SINGLE SOURCE OF TRUTH for entity primary
   / save ownership.** The **save gate reads it** (§6, entity-level `HasAuthority`), never a wire descriptor.
2. ⭐⭐ **The transport DERIVES `EntityMaster` ownership from `PrimaryOwnerId`** (publish + delete-order), and
   an **entity transfer UPDATES `PrimaryOwnerId`** cluster-wide — the transport re-derives from it.
   ⇒ **the save gate follows a transfer automatically**, because both read the same ECS fact.
3. ⭐⭐ **`EntityMaster` ownership transfer is a FIRST-CLASS BDC operation, and our ECS must MIRROR it into
   `PrimaryOwnerId`.** The BDC/NED spec ([`reference/BDC_NED_SST_Descriptor_Rules.md`](reference/BDC_NED_SST_Descriptor_Rules.md))
   defines a generic **`OwnershipUpdate{EntityId, DescrTypeId, DescrInstanceId, NewOwner}`** message that
   transfers ownership of **any** descriptor — including `EntityMaster` — and it **may originate from ANY node,
   including an EXTERNAL system handing an entity to us.** ⇒ this is *the* compliant way to move the primary
   owner (not a per-component `DescriptorOwnership` override we invent). Our rule: an `EntityMaster`
   `OwnershipUpdate` **is** the primary-ownership transfer, and applying it **must write `PrimaryOwnerId`** so
   the ECS mirror (and the save gate) stays consistent with who now owns/publishes `EntityMaster`.
   ⚠ **Per-component grants are different** — a non-master descriptor's ownership (the Muscle taking `SimTransform`)
   moves `AuthorityMask` only and does **not** change entity ownership (spec §Disposal: a non-master dispose
   *returns* ownership to the master's owner).

#### ⚠⚠ RECONCILIATION with the BDC/NED spec — and the COMPLIANCE GAP *(measured `2026-09-14`)*

🔒 **The spec is authoritative** *(user-provided, [`reference/BDC_NED_SST_Descriptor_Rules.md`](reference/BDC_NED_SST_Descriptor_Rules.md))*:
> *"EntityMaster descriptor controls the life of the entity."* · *"Owner is the one updating the entity… the
> owner is whoever published the current value."* · `OwnershipUpdate` is *"for the current and the new owner
> only"*: current owner stops writing (does **not** dispose); new owner writes to confirm.

⇒ **The primary owner IS the `EntityMaster` owner, per the spec** — there is **no** contradiction with "we
don't move `PrimaryOwnerId`"; rather, `PrimaryOwnerId` is the **ECS mirror** of the `EntityMaster` owner, and
the two must be **kept in sync at the NED boundary.** The save gate reads the network-agnostic `PrimaryOwnerId`
(§6) and is thereby **spec-compliant** — *provided the mirror is maintained.*

⛔⛔ **The current gap — our mirror is NOT maintained today:**

| what happens on an incoming `OwnershipUpdate{EntityMaster, NewOwner}` | measured |
|---|---|
| the message is delivered generically (no descriptor-type filter) | ✅ `OwnershipUpdateTranslator` ingress, `OwnershipIngressSystem` |
| `DescriptorOwnership.Map[EntityMasterKey] = NewOwner` + `AuthorityMask` for `{NetworkIdentity, TkbIdentity}` flipped ⇒ the **egress follows** (new node publishes, old stops) | ✅ `OwnershipIngressSystem.cs:27/42` |
| ⛔ **`NetworkAuthority.PrimaryOwnerId` is NOT written** ⇒ the **save gate stays stale** — we'd publish `EntityMaster` as the new owner but *not save the entity* | 🔴 `OwnershipIngressSystem` never touches `PrimaryOwnerId` |

⇒ 🔴 **Today our system is only PARTIALLY compliant for an `EntityMaster` transfer:** it honours the egress
side but not the ECS primary-owner mirror. **The fix (compliance requirement, folded into the deferred transfer
feature): when an `OwnershipUpdate` for the `EntityMaster` descriptor is applied, also write
`NetworkAuthority.PrimaryOwnerId = NewOwner`** (and, per spec, the new owner writes `EntityMaster` to confirm;
the old owner stops without disposing). Then an **externally-originated** transfer lands the entity — including
its save-ownership — on us correctly, with no code above the NED boundary needing to know a transfer happened.

⭐ **This does not change the save gate** (still the network-agnostic `PrimaryOwnerId`, §6) — the BDC mapping
lives exactly where it should, at the `OwnershipUpdate` ingress on the NED boundary. ⚠ It also does **not**
block the immediate scenario-save build: no external transfer occurs in our current runs, so the mirror is
never stale today; but the sync is a **named compliance requirement** so we are ready when one does.

✅ **IN THE INITIAL BUILD (`CE-275`, OQ12) — receiving an external transfer:**
- ⭐⭐ **Sync `PrimaryOwnerId` at the `EntityMaster` `OwnershipUpdate` ingress** — when `OwnershipIngressSystem`
  applies an update whose `DescrTypeId == dtEntityMaster`, write `NetworkAuthority.PrimaryOwnerId = NewOwner`
  (today it writes only `DescriptorOwnership.Map` + `AuthorityMask`) so the save gate follows. Map the spec's
  `NewOwner` `NodeId{Domain, Node}` → our int. The egress already stops-without-disposing on authority loss
  (spec-correct: a dispose would mean *entity deleted*), so no handoff change is needed just to RECEIVE.

⚠ **DEFERRED — the transfer INITIATION feature (`CE-276`, a later design):**
- **Make `NetworkAuthority` ownership-tracked so WE can initiate a transfer** — register it / replicate its
  `PrimaryOwnerId` value (today the ghost carries the `-1` sentinel, not the real owner id) so a node can hand
  an entity away by emitting the `EntityMaster` `OwnershipUpdate` and have peers converge.
- **The confirm-write handoff on initiation** — spec §Ownership updates: the new owner writes `EntityMaster`
  to confirm; ensure the gaining node re-publishes and the losing node stays quiet without disposing.
- **Does transfer also move per-component authority?** Independent axis; the feature decides whether an
  `EntityMaster` transfer drags the other descriptors' `AuthorityMask` bits along or leaves them as separate
  grants (spec allows partial owners).

✅ **DECIDED `2026-09-14` — `PrimaryOwnerId` stays in the `NetworkAuthority` component** (not promoted into
the `Fdp.Core` header). 🔒 User: *"leave PrimaryOwnerId in NetworkAuthority."* ⭐ Reconciled with the BDC spec:
the **wire** mechanism is the `EntityMaster` `OwnershipUpdate` (spec-authoritative, possibly external); the
**ECS** effect is that `NetworkAuthority.PrimaryOwnerId` moves to match. "Transfer the `NetworkAuthority`
component" and "`OwnershipUpdate` on `EntityMaster`" are the ECS and wire faces of the **same** transfer, joined
by the ingress sync (deferred sub-item 1). ⇒ **for THIS design, nothing to build for transfer** — the
requirement "do not prevent it" is met by rule 1 (the gate reads the ECS `PrimaryOwnerId` fact); the
`OwnershipUpdate→PrimaryOwnerId` sync is the later transfer feature's compliance fix.

---

## 7. ⭐⭐ THE MERGE — delete `NetworkOwnership`, keep `NetworkAuthority`

**Safe** (INVENTORY ⑤⑥⑦⑧; `UX_Authority_Aware_Writes §342` already elects `NetworkAuthority`). Both
readers repoint with identical behaviour: the cleanup query gains ghost matches but already drops them
via `if(!HasAuthority) continue`; `OwnsDescriptor`'s `absent⇒false` becomes `ghost(-1)⇒false`.

| step | site |
|---|---|
| delete struct + `[ComponentId(140)]` + `[DataPolicy(NoSave)]` | `NetworkComponents.cs:14-31` |
| drop owner-side write; keep the `NetworkAuthority` add | `NetworkSpawningSystem.cs:158-163` |
| repoint 2 readers → `NetworkAuthority` | `CycloneNetworkCleanupSystem.cs:47/52`, `OwnershipExtensions.cs:36/38/69/71` |
| drop from the static save mask (NetworkAuthority already masked) | `StagingEntityExtractor.cs:56` |
| drop registration | `HrotSharedComponentRegistry.cs:41` (+ example `DistributedTankScenario.cs:320`) |
| doc/comment fixes | `DebugApiRouteDocs.cs:713`, `GroundKinematicsModule.cs:34`, dds-to-ecs/mgmt docs |

⛔ **The merge does NOT change any `DataPolicy` flag** — it just deletes `NetworkOwnership`, repoints its 2
readers to `NetworkAuthority`, and drops `NetworkOwnership`'s bit-140 entry from `StagingEntityExtractor`'s
static mask. Whether `NetworkAuthority` should be *scenario*-excluded is a **separate deliberate decision**
(CE-277), not a rider on a struct deletion.

⭐⭐⭐ **MEASURED `2026-09-14` — `DataPolicy` has THREE disjoint bits, one per save context**, so "checkpoint"
and "scenario" do NOT share a flag *(rail: `Fdp.Toolkits.Tests/Scenario/DataPolicySaveContextMeasurement.cs`)*:

| bit *(name from `2026-09-14`)* | mask | ONLY production consumer | context |
|---|---|---|---|
| `NoPreview` *(was `NoSnapshot`)* | `GetSnapshotableMask` | live rewind/preview | in-memory snapshot |
| `NoReplay` *(was `NoRecord`)* | `GetRecordableMask` | **`RecorderSystem` → `.fdp`** | the CHECKPOINT recording |
| `NoScenario` *(was `NoSave`)* | `GetSaveableMask` | **`ScenarioSerializer` (only)** | the SCENARIO persist |

⇒ **`NoScenario` is scenario-ONLY** — it cannot touch the `.fdp` checkpoint (that keys on `NoReplay`). So the
earlier claim *"a global scenario-exclusion would break the Checkpoint pipeline"* is **measurably FALSE**, and
the claim *"the scenario-save exclusion already lives in the extractor mask"* is also false for THIS save path:
`StagingEntityExtractor.BuildStaticMask` is a **different** (CGF staging / load-side) path; the
`ScenarioSerializer` path this design uses had **no** exclusion for `NetworkAuthority`, so scenario save
genuinely **wrote** it before this change (measured round-trip). ⇒ there was a GAP, not a duplicate.

##### ✅ APPLIED `2026-09-14` (CE-275) — **the rename, and CE-277(e) part 1 (TWO flags, not four)**

⭐⭐ **Two changes landed together:**
1. **Rename** (Roslyn, root solution + out-of-solution sed for `Hrot.MuscleCharacter.Animation.Tests`;
   `Stride/` has **0** references, `HrotStrideApp.Windows` verified 0): `NoSnapshot`→`NoPreview` ·
   `NoRecord`→`NoReplay` · `NoSave`→`NoScenario`. Full-solution build green.
2. **CE-277(e) part 1 — the declarative flags on the PROCESS-LOCAL pair only:**
   `[DataPolicy(DataPolicy.NoScenario)]` added to **`NetworkAuthority`** and **`DescriptorOwnership`** —
   network-managed ownership re-established from the live topology on load, never read back from the file.
   Rail: `DataPolicySaveContextMeasurement.cs` asserts the applied policy (excluded from scenario, kept in
   checkpoint — independence proven).

⛔⛔ **MEASURED CORRECTION `2026-09-14` — the flag does NOT go on all seven `BuildStaticMask` members.**
📌 The `BuildStaticMask` 7 split into TWO kinds, and this was proven by **10 red `StagingEntityExtractorTests`**:

| kind | members | in scenario file? | why |
|---|---|---|---|
| ⭐ process-local runtime state | `NetworkAuthority` · `DescriptorOwnership` (+ already-flagged `GhostStateTracker`/`NetworkOwnership`/`PendingNetworkAck`) | ⛔ NO → `NoScenario` | re-established by spawn/replication systems; the file value is meaningless |
| 🔴 **CONSUME-AND-STRIP data** | **`NetworkIdentity`** · **`TkbIdentity`** | ✅ **YES — must be saved** | the LOAD path READS them out of the DOM (`StagingEntityExtractor.cs:239/280/298/305`) to recover the network id + TkbType, THEN strips them from `InitialComponents`. `NoScenario` on them read the id/type back as `0` and broke 10 rails |

⇒ ⭐⭐ **`BuildStaticMask` (strip-from-`InitialComponents`) is NOT the same set as the non-saveable mask** —
two of its members must be *in the file*. **The load-side strip and the save-side exclusion are different
concerns.**

##### ⛔ CE-277(e) part 2 — **DISPROVEN: the hardcoded exclusion CANNOT be replaced by `NoScenario`**

⭐⭐⭐ The user's question — *"if proper flags are used, no special hardcoded exclusion would be necessary?"* —
is answered **NO, by measurement.** `BuildStaticMask` must keep `NetworkIdentity`/`TkbIdentity` **in the
scenario file** while stripping them from a loaded entity's `InitialComponents` (their id/type is consumed to
build the creation request). A `NoScenario`-derived mask would drop them from the file and break load. ⇒ the
consume-and-strip pair is the **irreducible core** of the hardcoded list; it does something `NoScenario`
cannot. *(The other 5 members are redundant with their `NoScenario`/`Transient` flags and could be trimmed
from `BuildStaticMask` as belt-and-suspenders cleanup — cosmetic, not required.)*

⚠ ⭐ Keep the **name** `NetworkAuthority` (renaming ~57 sites buys only cosmetics — optional later follow-up).
Stage E (the `NetworkOwnership`→`NetworkAuthority` merge) is still separate and adds **no** flag.

---

## 8. ⛔⛔⛔ OPEN QUESTIONS & FLAWS — **the design is NOT buildable until these are closed**

| # | question / flaw | severity | lean |
|---|---|---|---|
| **OQ1** | ✅ **DECIDED `2026-09-14`** — global data (the complete set: `$meta`, `Header.TkbName`, `Zones`; sim-time is already orchestrator-owned — §6a) rides the **brain-role file**. 🔒 User: *"zones should for sure be handled by brain."* ⛔ **Build consequence:** the brain node (CGF) must compose a real `IZoneManagerService` — today only the editor has one (INVENTORY ⑩). | ✅ closed | brain owns all per-node-DOM globals; give CGF a zone service. |
| **OQ2** | ✅ **DECIDED `2026-09-14`** — **crash of the primary owner = the entity dies with it; no resolution; silent loss ACCEPTED.** 🔒 User: *"crash of primary owner means the entity dies with it, it has no resolution, silent loss accepted (no idea how to make brain nodes more stable than others)."* ⇒ **no** "reclaim orphan" rule, **no** brain-stability assumption. | ✅ closed | none — accepted risk, documented. |
| **OQ3** | ✅ **DECIDED `2026-09-14`** — a distributed scenario is a **set** of per-node files; **reuse the existing cluster-wide collection** (`FileManifestResult`/NAS-pull is file-agnostic — INVENTORY ③), **brain file is canonical "the scenario"**, each node loads its own slice. 🔒 User: *"nothing new… the design as well as implementation is counting with that already."* | ✅ closed | verify at build that the collection path is truly format-agnostic (it pulls whatever file the handler reports — it is). |
| **OQ4** | ✅ **CLOSED via R-A (§8.1)** — editor file ≡ the loaded union; old single-file scenarios load unchanged; distributed set loads with the brain file canonical. | ✅ closed | — |
| **OQ11** | ✅ **RESOLVED `2026-09-14` = R-A (§8.1).** Loading brain owns all persistable at load; multi-file reconverges; per-node/role ownership preservation (R-B) deferred as a future feature. Plus §6b format recognition. | ✅ closed | R-A accepted in full. |
| **OQ5** | ✅ **APPROVED `2026-09-14`** — add a per-node scenario-serialize handler that runs the gated `ScenarioSerializer` over the node's world, wired into `FanOutSerializeLocal`, distinct from the checkpoint recorder; reuse the archive/NAS collection. ⚠ Runs on **every** host including the editor (see OQ9). | ✅ core build | new handler, no editor exception. |
| **OQ6** | ✅ **RESOLVED `2026-09-14` — transfer is ENABLED by construction (§6c).** The primary owner is a **network-agnostic ECS fact** (`NetworkAuthority.PrimaryOwnerId`); the transport **derives** `EntityMaster` ownership from it. The save gate reads that fact (§6, entity-level), so an entity transfer — which updates `PrimaryOwnerId` cluster-wide — **carries save-ownership with it for free**. The transfer *feature* (a propagation message + DDS handoff) is deferred (§6c). ⛔ Rule: transfer moves `PrimaryOwnerId`, **not** a per-descriptor override. `request-to-owner` (`Node_Roles §5/§6`) picks an owner *at creation*. | ✅ closed | gate reads the ECS primary-owner fact (done, §6); build no transfer machinery now; do not couple the gate to a wire descriptor. |
| **OQ7** | ✅ **RESOLVED `2026-09-14` (Stage B).** `HasAuthority` resolves a `PartMetadata` child to its root entity before reading authority, so a multi-part entity gates as a UNIT: parent owned ⇒ parent+children saved; parent foreign ⇒ both skipped. No orphan part is ever emitted (a saved part always rides a saved parent). Rail: `AuthorityExtensionsTests.HasAuthority_ChildPart_FollowsParentOwnership`. | ✅ closed | children ride the parent — proven at the `HasAuthority` layer the gate calls. |
| **OQ8** | ✅ **RESOLVED `2026-09-14` (Stage B).** The absent-`NetworkAuthority` ⇒ owned arm is what makes the editor / AllInOne save everything; proven directly by `ScenarioSerializerTests.Serialize_AppliesTheOwnershipGate_SavingOnlyOwnedEntities` (an entity with no `NetworkAuthority` is saved, a foreign-owned one is dropped, a locally-owned one is saved). | ✅ closed | rail added; editor-saves-all is the absent-authority arm. |
| **OQ9** | ✅ **DECIDED `2026-09-14` — NO editor exception; unified BY CONSTRUCTION.** 🔒 User: *"no direct write in the editor… same code everywhere, driven by role/host config… the plumbing resulting naturally from using the same (unified) code (orchestration handlers etc.)."* ⇒ the editor runs the **same orchestration** as a single-node, all-roles cluster; scenario save goes through the fan-out + per-node handler, never `ScenarioFileService.SaveScenario` directly. ✅ **CORRECTED `2026-09-14` — the editor is ALREADY a single-node cluster.** An earlier draft here claimed the editor "has no orchestrator" and would need one built; that was WRONG — it read the mode roster, not the EditorSubsystem's internals. Measured: `EditorSubsystem` self-hosts `ClusterMaster` (`:318`, `:2074` `new ClusterMaster(_orchestrationBus, offlineConfig)` — "offline single-node orchestrator"), `StorageGatewayModule` (`:323/:2092`), and a `ClusterSlave` (`:1301`) on which it registers cluster handlers (`:1422-1623`), ticked at `:2590`. The config's "no editor + orchestrator" ban (`HrotRunnerConfiguration.cs:181-187`) merely prevents a **second** orchestrator, not a missing one. ⇒ **the real build is small:** the editor's slave registers only LOAD handlers today; save still bypasses the orchestrator via `IEditorLogic.SaveScenarioAs` (`:3864/:3958`). Work = **register the new save handler on every slave (editor included)** + **route the editor save through `_clusterMaster`** instead of the direct call. | 🟢 **small build, plumbing exists** | register the save handler uniformly; reroute the editor save trigger; retire the direct `ScenarioFileService.SaveScenario`. |
| **OQ10** | ✅ **CLOSED via R-A** — CGF/brain owns ALL persistable at load; per-component authority granted at runtime; muscles never own persistable entities. | ✅ closed | R-A. |
| **OQ12** | ✅ **PULLED INTO THE INITIAL BUILD `2026-09-14`** (🔒 user). BDC compliance: on an incoming `OwnershipUpdate` whose `DescrTypeId == dtEntityMaster`, `OwnershipIngressSystem` must also write `NetworkAuthority.PrimaryOwnerId = NewOwner` (mapping `NodeId{Domain,Node}` → our int) so the save gate follows an EXTERNAL transfer. The egress already stops-without-disposing on authority loss, so this one write is self-contained. Tracked: **CE-275**. | ✅ in build | build step 2c + a rail (external EntityMaster `OwnershipUpdate` → receiving node saves, prior owner stops). The transfer INITIATION side stays deferred → **CE-276**. |

⭐⭐ **ALL BLOCKERS CLOSED `2026-09-14`.** OQ1 (globals → brain), OQ2 (orphan loss accepted), OQ3 (reuse
collection, brain canonical), OQ4/OQ10/OQ11 (R-A round-trip), OQ5 (per-node handler), OQ9 (editor is
**already** a single-node cluster — small build), plus §6b format recognition (mechanism exists).
⭐ **BUILD-TIME QUESTIONS NOW CLOSED (Stage B, `2026-09-14`):** OQ7 (parent/child parts ride the gate — proven
at the `HasAuthority` layer) and OQ8 (editor saves all — the absent-authority arm, railed). ⇒ the only OPEN
items are the **later features**: OQ12's initiation half (**CE-276**) and Stage C's distributed wiring.
⇒ **the design is BUILDABLE**; Stages A+B are BUILT.

### 8.1 ✅ OQ11 — THE ROUND-TRIP: **RESOLVED `2026-09-14` = R-A**

> ✅ **RULING (`2026-09-14`): R-A, accepted in full.** 🔒 User: *"R-A approved and accepted in full. Per
> node/role partial loading might be a future feature when needed… for now we rather unify to same
> consistent approach. IG-authored drawing going through editor load and resaved gets loaded as brain
> owned, this is fine (this only happens for persistable entities)."*
> ⇒ ⭐ **the loading brain (editor: the editor) owns all persistable at load**; per-component authority is
> granted at runtime; the multi-file save reconverges on load. **R-B (ownership preserved by role) is a
> FUTURE feature, not now.** §5 is therefore no longer provisional. ⭐ **New rider — format recognition —
> see §6b:** a host with an *incompatible* scenario format keeps its own file via its own editor; ours
> loads only files it recognises. The original question and the R-A/R-B comparison are kept below as the
> rationale.

> 🔒 **User, verbatim (the question that produced R-A):** *"The distributed saving on CGF and IG nodes when both produce their own partial
> save file for their owned entities is not yet resolved — how to load these into editor? load all in
> sequence, likely, or integrating the IG saved stuff into the 'master' scenario file, and when saving the
> scenario loaded this way from editor, the tactical drawings natively become saved to the single editor
> saved scenario file. This needs some thinking, i feel it is still incomplete and needs clearer rules."*

**The asymmetry that must be ruled on.** SAVE splits by owner → N partial files. The EDITOR is one world
that owns everything. So a load must funnel N partials into that one world — and the question is **what
owns each entity after a load**, which decides whether an IG-authored drawing stays IG-owned or becomes
brain/editor-owned across a round-trip.

| | ⭐ **R-A — re-converge on load** *(lean)* | **R-B — preserve ownership by role** |
|---|---|---|
| who owns a loaded entity | the **loading brain** (editor: the editor; distributed: CGF) owns **all** persistable; per-component authority granted to muscles at runtime | each entity is re-owned by **the host whose ROLE owns it** — the drawing reloads IG-owned, the tank brain-owned |
| load of N partials | **load all partials in sequence** into the loader's world (or merge into one master first) — the editor and CGF both just ingest the union | each node loads **only the partials for the roles it hosts**; the editor hosts all roles ⇒ loads all |
| editor round-trip | ✅ natural — editor owns all ⇒ re-saves as one file; a drawing "natively" lands in the single editor file | ⚠ editor still collapses to one owner (it IS all roles), so a re-save loses the per-role split unless it re-derives roles per entity |
| matches current code | ✅ **yes** — CGF genesis owns all at load today (INVENTORY ⑨) | ⛔ **no** — needs role-driven per-entity ownership at load; a substantial load-pipeline change |
| cost of the model | cheap; the multi-file save is a save-time distribution detail that reconverges on load | the "truly distributed, each host loads its own content" ideal, but a real redesign of the genesis pipeline |
| what it loses | **entity-level authorship is not preserved** across a round-trip (IG drawing → brain-owned after reload) | nothing — authorship survives, at the cost of complexity |

⭐ **My lean: R-A** — it matches the editor (which is unavoidably one owner), matches today's brain-centric
genesis, and makes "load all partials in sequence" + "editor re-saves the union as one file" fall out
exactly as you described. ⛔ **The cost is explicit and must be accepted:** a drawing authored+owned on an
IG, once it passes through an editor save, reloads **brain-owned**, not IG-owned. In a purely distributed
run (no editor in the loop) R-A still means the **loading brain owns everything** — a non-brain partial
file is only produced by **runtime** authorship (e.g. an IG draws live), and on the next load that content
folds into brain ownership too.

⚠ **What would push to R-B:** a requirement that a host **reload and re-own its own authored content**
without a central brain — i.e. each host is truly responsible for loading its slice and owning it. Your
earlier "different hosts are responsible for loading their content" leans this way; R-A contradicts it for
the *ownership* half while honouring it for the *file* half. **This is the tension to rule on.**

**Sub-questions inside OQ11, once R-A vs R-B is chosen:**
1. **Load mechanism** — load partials *in sequence* into one world, or *merge* them into a master file
   first? (R-A works with either; sequence is simpler and needs no merge step.)
2. **Is there a persistent "master" file** distinct from the per-role partials, or is "the scenario" always
   just the manifest-linked set with the brain file as its head?
3. **Editor re-save shape** — always one file (R-A), or re-split per role on save (only meaningful under R-B)?

⇒ 🔴 **Until OQ11 is ruled, the LOAD half of §5 is provisional** — §5's diagram currently assumes R-A
(brain owns all, muscles get ghosts). If you choose R-B, §5 and the genesis pipeline change materially.

---

## 9. ⭐ RELATIONSHIP TO EXISTING DESIGNS (supersessions are marked in those files)

| document | what changes |
|---|---|
| `DESIGN_Node_Roles_And_Policies.md` §5, §7.1/§7.2/§7.3, §8 ① | §7.2 "ownership is DISCARDED at save time (the good half)" is **superseded for distributed save** — ownership IS now consulted (§6). §7.1's "enforce by a missing IG handler" is retired — there is no IG special case (§1a). ⭐ **§5's R-140 "IG is passive and non-persisting" is REFINED (§1a): passivity is EMERGENT from role-driven creation, not a save-path rule** — needs a follow-up edit in that file. §8 ① (the parked question) is **answered here**. |
| `docs/designs/cgf-scn-2/DESIGN.md` | unchanged in scope (per-component-TYPE correctness); gains a pointer — per-ENTITY selection lives HERE. |
| `docs/designs/cgf-scn/DESIGN.md` | unchanged (LOAD genesis); gains a pointer — which FILE each node loads lives HERE. |
| `docs/UX/UX_Feature_Authority_Aware_Writes.md` §335/§342 | the "do not merge them here" debt is **scheduled here**; §7 is the merge. |
| `docs/DESIGN_Entity_Creation_Unification.md` | unchanged; the `OwnerNodeId` it stamps IS the save-ownership this doc gates on. |

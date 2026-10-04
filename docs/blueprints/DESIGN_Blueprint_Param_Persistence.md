<!--STATUS
state: LIVE
build-state: BUILT
updated: 2026-10-04
current-answer: ⭐ §10 (CE-3044, R-191 — params persist as JSON by name; its own class + sequence UML) for the
  PERSISTED FORMAT; §6 the MCP wire and §7 QA-023 still hold. ⛔ §3/§4/§5 describe the SUPERSEDED byte format. §2 INVENTORY (measured). §6 the MCP wire. §7 QA-023. §8 out of scope.
design-basis: Architect_Question_61 (the reframe + A/B/C/D leans) · EXPLAINER_Where_Parameters_And_State_Live.md
  §"two supply shapes, one concept" (line ~287 — the ruling that resolver shape > Overrides dict) ·
  BLUEPRINT-SCENARIO-DESIGN.md §6 (the ORIGINAL Overrides intent, deferred for UX) · DESIGN_Parameter_Model.md
  §3.3 (parse-before-commit, InitDefault-then-params order) · HANDOFF_Blueprint_Param_Persistence.md (the FRAME).
known-conflict: touches Fdp.Toolkits + Hrot.SimHost (backend lane's neighbourhood) — fenced to the MCP lane
  this batch per the handoff §4; backend's concurrent batch is fenced OFF these exact files.
known-rot: §3 THE DECISION (persist the resolved param BYTE region, base64 + ParamsStructureHash) is SUPERSEDED 2026-10-04 by R-191 —
  🔒 user: "The params should be saved as json to the scenario and translated to dto structs as needed. Never saved as bytes
  to scenario." ⇒ the scenario carries the params as a JSON object keyed by parameter name (only non-default fields); load
  goes through the existing ParseParams (defaults, then overlay by name); an emitted inverse writes the JSON on save.
  Build: CE-3044 · docs/DESIGN_Sensors_And_Doctrine.md §7.5. 🔒 user: "Bytes can not be easily migrated on json level. The previous
  decision must have been wrong." — WHY it was wrong: a byte region is readable only while the layout is unchanged; the hash guard
  turns any layout change into losing EVERY authored value, where JSON by name loses only the changed field. No scenario in the repo
  carries byte params (measured 2026-10-04) ⇒ no legacy reader.
-->
# DESIGN — **Persisted instance-blueprint parameters + the MCP wire** *(MX-030..036)*

> 🎯 Make per-entity instance-blueprint **params survive save→reload** (they are dropped today), then ship the
> **run-state-aware attach/detach + list** MCP wire that becomes worth having once params persist. Fold `QA-023`.

## 1. ⭐⭐⭐ THE FRAME — the finding
🔴 Assigning an instance blueprint **with persisted per-entity params is impossible today by ANY path**. The
runtime params pipeline is built (`AttachToEntity(paramsJson)` → `ParseParams` → param region @16), but
**save drops params**: `BlueprintStateTranslator.Extract` writes `AssetId` only; `BlueprintMaterializationSystem`
calls `InitDefault` only; `BlueprintAssignmentDto.Overrides` is dead. ⇒ a parametric assignment is hollow.

## 2. ⭐⭐ INVENTORY — measured `2026-08-26`
| query / symbol | home | role | measured |
|---|---|---|---|
| `BlueprintInstanceService.AttachToEntity` | Fdp.Toolkits | the write seam; **the ONLY source of params** — copies resolved bytes to `payload+ParamsOffset` after `InitDefault` | ⭐ the pattern the round-trip reuses |
| `BlueprintDefinition{ ParamsOffset=16, ParamsSize, StateSize, StructureHash, InitDefault, ParseParams }` | Fdp.Toolkits | payload layout `[Cursor 16][Params N][State M]`; ⛔ **no bytes→JSON inverse of `ParseParams`** exists | 📐 `ParseParams` is JSON→bytes only |
| `BlueprintAssignmentDto{ AssetId, Overrides }` | Fdp.Toolkits | `Overrides` **dead** — only two DOC-COMMENT references, zero code reads/writes | 📐 grep, `2026-08-26` |
| `BlueprintStateTranslator.Extract/Inject` | Hrot.SimHost | save snapshots slots→`AssetId` only; load parses the array→`InitialBlueprintsIntent`. **Inject only matches `JsonArray`** ⇒ `QA-023` | 📐 `Test5b` passes a `JsonElement` |
| `BlueprintMaterializationSystem` | Hrot.Common(SimHost) | load-time: `TryAttach`+`InitDefault` per blueprint into a pre-provisioned aggregate tier — **no params applied** | ⭐ where load-apply lands |
| `BlueprintBlackboardPartitions.{GetSlot,TryGetSlotOffset,TryAttach}` · `BlueprintSlotEntry.{BlueprintId,PayloadOffset,StructureHash}` | Fdp.Toolkits | slot table read/alloc; a slot's `PayloadOffset` locates its payload | ⭐ Extract reads the live params via it |
| `AttachBlueprint/DetachBlueprint` (Group Q) · `AttachInstanceBlueprintEvent` | Hrot.Editor | MCP attach — always publishes the next-**tick** event; ⛔ never lands in frozen Edit state | G1 |
| `EntityBlueprintsPanel.ExecuteCommitPlan` | Hrot.Blueprints.Editor | `timing = _isRunning ? Running : Paused`; **paused → direct `BlueprintInstanceService`**, running → event | ⭐ the branch MCP must mirror |

## 3. ⭐⭐⭐ THE DECISION — persist the RESOLVER SHAPE (bytes), not the `Overrides` dict
📄 **[`EXPLAINER_Where_Parameters_And_State_Live.md`](./EXPLAINER_Where_Parameters_And_State_Live.md) §"two supply
shapes, one concept"** rules it: a name→value `Overrides` dict and the resolver's byte region are **two
implementations of one concept** *(ruling 9)*; the **resolver shape wins** — it already carries defaults, overlay
and world-context, which `Overrides` carries none of. `BLUEPRINT-SCENARIO-DESIGN.md` §6's per-variable `Overrides`
was **deferred for authoring-UX reasons, not chosen** — so we do NOT revive it.

⇒ ⭐⭐ **The persisted format is the resolved param BYTE REGION** `[ParamsOffset .. ParamsOffset+ParamsSize)` — the
exact bytes `AttachToEntity` produces and the tick reads. **One source of truth** (the live slot; a *snapshot of a
live component*, AQ61 §1), **no side table**, **no second parser**. Since no bytes→JSON inverse exists, a JSON form
would require inventing one (a second representation) — rejected.

| decision | choice | why |
|---|---|---|
| format | **resolved param bytes** (`byte[]`, JSON-serialized as base64) | resolver shape; EXPLAINER §287; no inverse serializer to build |
| DTO | **replace dead `Overrides`** with `byte[]? Params` + `ulong? ParamsStructureHash` | ruling 9 — one mechanism, not a dead field beside a live one |
| what to persist | **only params that DIFFER from `InitDefault`** | keeps scenarios clean; a default assignment stays `{AssetId}` only |
| layout guard | apply on load **only if `ParamsStructureHash == def.StructureHash`**, else `InitDefault` + warn | bytes are layout-versioned; a recompiled blueprint must not read stale bytes |
| tradeoff | ⚠ the blob is **opaque** in the scenario file (vs the rejected human-readable dict) and **layout-versioned** | accepted — the ruling decides shape; a recompile falling back to defaults is safe, not silent-wrong |

## 4. ⭐⭐ CLASS DIAGRAM *(existing shown as `<<exists>>`; drawn after the INVENTORY)*
```mermaid
classDiagram
    direction LR
    class BlueprintAssignmentDto {
        +Guid AssetId
        +byte[] Params  «NEW — replaces dead Overrides»
        +ulong ParamsStructureHash  «NEW — layout guard»
    }
    class BlueprintInstanceService {
        <<exists · Fdp.Toolkits · static>>
        +AttachToEntity(world, registry, bpId, entity, paramsJson) BlueprintAttachResult
        +DetachFromEntity(world, bpId, entity) bool
        +ReadParamsRegion(payload, def) byte[]  «NEW»
        +WriteParamsRegion(payload, def, bytes) void  «NEW»
        +GetDefaultParamsRegion(def) byte[]  «NEW»
    }
    class BlueprintDefinition {
        <<exists · Fdp.Toolkits>>
        +int ParamsOffset
        +int ParamsSize
        +ulong StructureHash
        +InitDefault
        +ParseParams
    }
    class BlueprintStateTranslator {
        <<exists · Hrot.SimHost>>
        +Extract(repo, entity, resolver) Dictionary
        +Inject(repo, entity, data, resolver) void
    }
    class BlueprintMaterializationSystem {
        <<exists · Hrot.SimHost>>
        +Execute(view, dt) void
    }
    class DebugApiService {
        <<exists · Hrot.Editor>>
        +AttachBlueprint(id, bp, paramsJson) run-state-aware
        +DetachBlueprint(id, bp) run-state-aware
        +GetEntityBlueprints(id) JsonNode  «NEW — list route»
    }
    BlueprintStateTranslator ..> BlueprintInstanceService : Extract reads params (ReadParamsRegion/GetDefaultParamsRegion)
    BlueprintStateTranslator ..> BlueprintAssignmentDto : emits Params + hash
    BlueprintMaterializationSystem ..> BlueprintInstanceService : WriteParamsRegion after InitDefault
    BlueprintMaterializationSystem ..> BlueprintAssignmentDto : reads Params + hash (guarded)
    BlueprintInstanceService ..> BlueprintDefinition : ParamsOffset/ParamsSize/StructureHash
    DebugApiService ..> BlueprintInstanceService : paused/Edit → direct attach/detach
```

## 5. ⭐⭐ SEQUENCE — save→reload param round-trip
```mermaid
sequenceDiagram
    autonumber
    participant SV as save (scenario)
    participant EX as BlueprintStateTranslator.Extract
    participant BIS as BlueprintInstanceService
    participant DTO as BlueprintAssignmentDto
    participant LD as load (Inject then Materialization)
    participant MAT as BlueprintMaterializationSystem

    SV->>EX: Extract(entity)
    EX->>EX: for each slot, get PayloadOffset
    EX->>BIS: ReadParamsRegion(payload, def) and GetDefaultParamsRegion(def)
    EX->>EX: diff live vs default
    alt params differ from default
        EX->>DTO: Params = live bytes, ParamsStructureHash = def.StructureHash
    else default
        EX->>DTO: AssetId only
    end
    EX-->>SV: BlueprintAssignments array

    LD->>MAT: InitialBlueprintsIntent materialized
    MAT->>MAT: TryAttach then InitDefault
    alt DTO.Params present and hash matches def.StructureHash
        MAT->>BIS: WriteParamsRegion(payload, def, DTO.Params)
    else absent or hash mismatch
        MAT->>MAT: defaults stand (warn on mismatch)
    end
```

## 6. ⭐ THE MCP WIRE *(Q61-A + Q61-B)*
- **Run-state-aware attach/detach (A)** — `AttachBlueprint`/`DetachBlueprint` branch on **sim advancing**
  (`_preview.IsInPreviewMode && !_time.IsPaused`): advancing → publish the event (today); **frozen/Edit/paused →
  `BlueprintInstanceService.AttachToEntity/DetachFromEntity` directly (same frame)**, surfacing the
  `BlueprintAttachStatus` (`ParamsParseFailed`→400, `NotInstanceKind`→400, `Attached`/`AlreadyAttached`→200). ⭐ ONE
  route that mirrors the panel's own branch *(ruling 9)* — ⛔ NOT a parallel `/assign`. The reply names the path taken.
- **List (B)** — `GET /entities/{networkId}/blueprints` → `GetEntityBlueprints` reads the slot table (the source
  `Extract` uses) → the attached instance blueprints `[{ blueprintId, name, assetId, tier }]`.
- Node wrappers for the list tool; attach/detach RouteDocs updated to note the branch **and that params now persist
  through save** (Q61-C is CLOSED by §3–§5, so the old "overrides don't survive" caveat is removed).

## 7. ⭐ QA-023 — `Inject` mixed-keys
🔴 `Inject` matches only `rawValue is JsonArray`, but the value arrives as a `JsonElement` (Array) ⇒ intent never
set (`Test5b_BackwardCompat_MixedOldAndNewKeys_OnlyAssignmentsApplied` red). ✅ Fix: deserialize from **`JsonArray`
OR `JsonElement`(Array) OR any `JsonNode`**. Legacy blackboard keys stay black-holed.

## 8. ⛔ OUT OF SCOPE — the same blueprint twice on one entity
Slot identity is `blueprintId` **alone** and attach is idempotent on it; "two Patrols, different waypoints" needs
`(blueprintId, instanceKey)` — a separate, larger identity change *(EXPLAINER §"slot identity"). Single-instance
persist only.*

## 9. ✅ AS-BUILT — Batch HN-124, `2026-08-26` *(obligation ⑤)*
Built as designed; ids **MX-030..036**. The §4/§5 UML holds — one deviation, one addition, both minor:

| # | as-built vs §4/§5 | why |
|---|---|---|
| **D1** | **Materialization keeps its low-level aggregate-tier path** (`TryAttach`+`InitDefault`) and **adds `WriteParamsRegion` after InitDefault**, rather than delegating to `AttachToEntity`. | `AttachToEntity` chooses a per-blueprint tier; Materialization must pre-provision the **aggregate** tier so multiple blueprints share one component. Both write params through the SAME `WriteParamsRegion`, so there is still one param writer. |
| **D2** | The list route reports `payloadSize` (not `tier`). | `SlotSummary` carries no tier field; `payloadSize` is the useful per-slot fact it does carry. |
| **D3** | ⭐ Round-trip rail added: `BlueprintScenarioIntegrationTests.ParamPersistence_NonDefaultParams_SurviveSaveThenReload` — attach → set non-default param → Extract → Inject → Materialize → param survives; **inverse-edit red-proof** noted in the test. QA-023 (`Test5b`) green. | acceptance |

⭐ **Q61-C is CLOSED, not deferred** — the FRAME elevated the param-persistence engine work into this batch, so
the AQ61 §3 "defer C" lean is superseded by the handoff. **Gates:** Fdp.Toolkits + Hrot.SimHost + Hrot.Editor +
ClusterRunner build clean; `EveryRouteIsDocumented` 4/4; blueprint scenario 6/0 (+2 pre-existing skips); SimHost
translator/materialization/genesis 25/0; Fdp.Toolkits blueprint 38/0 + DTO 2/0; `gen:catalog`/`gen:skill`/
`test:catalog` green (98 tools).

## 10. ⭐⭐⭐ R-191 — params persist as JSON BY NAME *(`CE-3044`, `2026-10-04`; SUPERSEDES §3–§5's byte format)*

> 🔒 *"The params should be saved as json to the scenario and translated to dto structs as needed. Never saved as
> bytes to scenario."* · *"Bytes can not be easily migrated on json level."*

```mermaid
classDiagram
    direction LR
    class BlueprintAssignmentDto {
        +Guid AssetId
        +JsonObject Params  «CHANGED: was byte[]; only non-default fields, keyed by name»
        ParamsStructureHash  «DELETED: a name-keyed form needs no layout guard»
    }
    class ParamsJson_Generated {
        <<emitted per Instance with parameters · InstanceEmitter>>
        +ParseParams(json, memory, capacity, world, self)  «exists: defaults, then overlay by name»
        +FormatParams(memory, capacity) string  «NEW: fields that differ from the declared defaults»
        +string[] ParamNames  «NEW»
    }
    class BlueprintDefinition {
        <<exists · Fdp.Toolkits>>
        +ParseParamsDelegate ParseParams
        +FormatParamsDelegate FormatParams  «NEW»
        +IReadOnlyList~string~ ParamNames  «NEW»
    }
    class FormatParamsDelegate {
        <<NEW · beside ParseParamsDelegate, Fdp.Toolkit.Behavior>>
        string? (byte* memory, int capacity)
    }
    class BlueprintInstanceService {
        <<exists · static>>
        +AttachToEntity(..., paramsJson)  «unchanged»
        +ApplyParams(payload, def, json, world, entity) string?  «NEW: parse into scratch, then WriteParamsRegion»
        +UnknownParamKeys(def, JsonObject) list  «NEW: the renamed-field warning»
        +WriteParamsRegion  «kept: the one region writer»
        GetDefaultParamsRegion  «DELETED: no reader»
    }
    class BlueprintStateTranslator { <<exists · Hrot.SimHost>> +Extract  «FormatParams of the live region» }
    class BlueprintMaterializationSystem { <<exists · Hrot.SimHost>> «ApplyParams ALWAYS — bakes declared defaults too» }
    BlueprintDefinition --> FormatParamsDelegate
    ParamsJson_Generated ..> BlueprintDefinition : registered onto
    BlueprintStateTranslator ..> BlueprintDefinition : FormatParams
    BlueprintStateTranslator ..> BlueprintAssignmentDto : Params JSON
    BlueprintMaterializationSystem ..> BlueprintInstanceService : ApplyParams + UnknownParamKeys
    BlueprintInstanceService ..> BlueprintDefinition : ParseParams
```

*What the picture shows that prose hid:* ONE parser on every path — attach and reload both go through the emitted
`ParseParams` — and its inverse sits beside it on the same generated class, so a parameter added to the blueprint is in
both by construction.

```mermaid
sequenceDiagram
    autonumber
    participant EX as BlueprintStateTranslator.Extract
    participant DEF as BlueprintDefinition
    participant DTO as BlueprintAssignmentDto
    participant MAT as BlueprintMaterializationSystem
    participant BIS as BlueprintInstanceService

    EX->>DEF: FormatParams(payload + ParamsOffset, ParamsSize)
    DEF-->>EX: JSON of the non-default fields, or null
    EX->>DTO: Params = that object (absent when null)
    MAT->>MAT: TryAttach, then InitDefault
    MAT->>BIS: UnknownParamKeys(def, DTO.Params)
    BIS-->>MAT: keys the blueprint no longer declares, each logged, the field keeps its default
    MAT->>BIS: ApplyParams(payload, def, DTO.Params or empty)
    BIS->>DEF: ParseParams into a scratch buffer (defaults, then overlay by name)
    BIS->>BIS: WriteParamsRegion (a parse failure leaves the region untouched, logged)
```

| decision | why |
|---|---|
| ⭐ reload ALWAYS runs `ParseParams`, with `{}` when nothing was saved | 🔴 found while building: materialization ran only `InitDefault`, which bakes STATE defaults, never PARAM defaults ⇒ a reloaded instance had all-zero params where a fresh attach had the declared defaults. Latent only because no shipped instance declares parameters |
| ⭐ the diff is by the field's JSON, against the DECLARED defaults | the same baseline `ParseParams` bakes, so save then load is the identity |
| ⭐ unknown key ⇒ warned and dropped, never an error | a renamed field loses ONLY itself (the reason R-191 exists); the other fields load |
| ⛔ no legacy reader | no scenario in the repo carries byte params (measured `2026-10-04`) |
| ⏳ behaviours / doctrines | the snapshot (`CE-3042`) reuses `FormatParamsDelegate` for their start record |

### 10.1 As-built `2026-10-04` *(`CE-3044`)*

⭐ §10's two diagrams hold as built. What the build added:

| finding | |
|---|---|
| ⭐ the generated `FormatParams` returns `string`, the delegate `string?` | generated code has no `#nullable` context (`CS8669` in `Hrot.AI.Behaviors`'s generated sources) |
| ⭐ a `Vector3` param is the canonical ARRAY `[x, y, z]`, a fixed list a plain array | `FdpJsonOptionsRegistry.DefaultRelaxed`'s converters — the same options `ParseParams` reads with, so the round-trip is exact |
| ⭐ the one-supply-path rail (`InstanceParamsSeamTests.ExactlyOneParameterSupplyPathExists`) excludes `FormatParams` | it READS the region; it is pinned to the shared `FormatParamsDelegate` instead |
| ⭐ goldens: 3 emit snapshots, +95 / −0 | purely additive — `FormatParams` + `ParamNames` beside each `ParseParams` |

| gate | result |
|---|---|
| new rails | `InstanceParamsJsonTests` (compiled blueprint: only non-default fields by name, save → reload identical) · ClusterRunner `ParamPersistence_*` ×3 (non-default survives as JSON · a default assignment reloads with the DECLARED defaults · a renamed field keeps its default, the others load) |
| Toolkits · SimHost · Blueprints · Editor | 2626/0 · 1083/0 · 4130/0 · 463/0 |
| ClusterRunner `--filter Blueprint` | 22/0 |

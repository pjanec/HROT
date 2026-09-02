# ADA-BATCH-13: Live Mutation / Fault Injection (Group L) + MCP tools

**Batch Number:** ADA-BATCH-13
**Tasks:** ADA-P8-T01 (attribute patch, primary) + ADA-P8-T02 (StructEdit component edit, escape hatch) +
Group L MCP tools (**closes ADA-06-D01**)
**Phase:** Phase 8 — discoverable attribute patching + arbitrary component edit (fault injection)
**Estimated Effort:** ~14 hours
**Executor:** sonnet
**Priority:** HIGH (last API group; completes the mutation surface)
**Dependencies:** Phase 1 + P-MCP + BATCH-07..12.

---

## Onboarding & Workflow

Two write paths: (L1) a discoverable, authority-aware **attribute patch** via the JSON→ECS compiler (the
primary, curated path), and (L2) a **StructEdit** escape hatch for arbitrary component fields outside the
registered paths. Together they let the AI mutate/fault-inject sim state.

### Required reading (IN ORDER)
1. `.dev/.guides/DEV-GUIDE.md`
2. `.dev/_DONE/ai-debug-api/reviews/ADA-BATCH-11/12-REVIEW.md` (the live-reproduce gate; the **recurring
   false-green-verify failure — RE-RUN `npm run verify` to a real tally before reporting**).
3. **Design:** `.dev/_DONE/ai-debug-api/DESIGN.md` — Group L (live mutation / fault injection).
4. **Task detail:** `.dev/_DONE/ai-debug-api/TASK-DETAIL.md` — ADA-P8-T01, ADA-P8-T02.

> No codebase-memory MCP (hangs — Grep/Glob/Read). No git commit. Report HONESTLY — the lead re-runs
> `dotnet test --filter DebugApi`, `npm run verify`, and a real headless reproduce (patch a field → read it
> back changed; invalid value → 400, unchanged). Run the FULL build.

### Existing infra to reuse (confirmed PUBLIC APIs)
- **`JsonAttributeCompiler`** (`FDP/Toolkits/Fdp.Toolkits/Replication/Patching/`, public): `RegisteredPaths`
  (`IReadOnlyList<string>`), `ExportSchema() → string` (JSON Schema), `CreatePatchContext(EntityRepository
  repo, Entity entity) → EcsPatchContext`, `Compile(string? json, IEntityPatchContext context)`. **Simplest
  path: call the compiler DIRECTLY on the job queue** (`CreatePatchContext` + `Compile`) — do NOT wire the
  DDS-oriented `UpdateEntityAttributeRequestSystem` (its offline ctor needs request-source/ack-sink plumbing;
  more complexity for no benefit here). Study `JsonAttributeCompilerTests` for construction + usage. Find how
  the editor constructs/obtains a compiler instance (with the registered paths); construct one if needed.
- **StructEdit** `IComponentEditService` via `new ComponentEditServiceBuilder().Build()` (the editor already
  builds these — `EditorSubsystem.cs:1013, 2088`). Flow: `Open(component, type)` → apply patch to the
  `EditDocument` → `Commit()` (runs `IComponentValidator`) → write the boxed value back to the component.
- Entity resolution via `NetworkEntityMap` (main thread). Read-back via the existing `DumpEntity` path.

---

## Endpoints (authoritative spec in TASK-DETAIL / DESIGN Group L)
### Attribute patch (T01)
- `GET /attributes/schema` → `{ registeredPaths: [...], schema: <ExportSchema()> }`.
- `POST /entities/{networkId}/attribute {patchJson}` → resolve entity, `CreatePatchContext(repo, entity)`,
  `Compile(patchJson, ctx)` on the job queue. Authority-aware; unregistered keys safely ignored (no error).
  Return the applied result / updated entity summary.

### StructEdit component edit (T02)
- `POST /entities/{networkId}/component {componentType, patch}` → resolve the component instance, `Open` →
  apply `patch` to the `EditDocument` → `Commit()` (validator). Invalid value → `400`, component unchanged.
  Never write `NativeChunkTable` memory directly.

## MCP tools (Group L — CLOSES ADA-06-D01)
- Add `get_attributes_schema`, `patch_attribute`, `edit_component` (1:1). Update README tool table; mark
  ADA-06-D01 fully RESOLVED (all groups A–N have MCP tools). Extend `verify.mjs` with a patch round-trip.

## Verification (prove the mutation lands)
- **Tier-1 (EditorHarness):**
  1. `GET /attributes/schema` lists patchable paths (Name, Affiliation, GeoPosition.*, Heading).
  2. `POST …/attribute {"Name":"Alpha"}` → `GET /entities/{id}` shows `EntityInfo.Name == "Alpha"`.
  3. `{"Heading":90}` updates rotation; an unregistered key is ignored without error.
  4. StructEdit: editing a component field outside the registered paths succeeds and is visible; an invalid
     value is rejected (400) and the component is unchanged.
- **Tier-2 (live headless / MCP `verify.mjs`):** load test-move → `get_attributes_schema` non-empty →
  `patch_attribute {networkId:1000, patchJson:{"Name":"Alpha"}}` → `get_entity {1000}` shows the new name.
  Re-runnable; no orphans. RE-RUN `npm run verify` to a real PASS tally before reporting.
- `dotnet build IOS-IG-SimHost.sln`; `dotnet test … --filter "FullyQualifiedName~DebugApi"`.

## Constraints (hard)
- Attribute patch is authority-aware; unregistered keys ignored (not an error). StructEdit validates via the
  type's `IComponentValidator`; invalid → 400, no mutation. All mutation marshalled to the main thread.
- Global registry (not per-TKB-type) — discovery exposes the global schema (optionally intersect with the
  entity's live components). Frozen `TestAssets`; never the production scan path; never regenerate snapshots.

## Deliverables
- Code + green tests; extended MCP `verify.mjs`; README updated (ADA-06-D01 fully resolved).
- `.dev/_DONE/ai-debug-api/reports/ADA-BATCH-13-REPORT.md` (DEV-GUIDE format): built, decisions (compiler-direct vs
  request-system), FULL `dotnet test` summary, the live reproduce output (Name patch lands + invalid→400),
  blockers, debt → DEBT-TRACKER (close ADA-06-D01).

# ADA-BATCH-05: TKB Entity-Type Catalog + World/Coordinate Info

**Batch Number:** ADA-BATCH-05
**Tasks:** ADA-P1-T07 (TKB catalog, Group M), ADA-P1-T08 (world/coordinate info + geo↔local convert, Group N)
**Phase:** Phase 1 — Slice 1 Surface (scenario-authoring catalog/coords)
**Estimated Effort:** ~12 hours
**Executor:** sonnet (T08 adds a small engine seam — an origin getter on the geo transform — and coordinate
math with orientation; care needed)
**Priority:** HIGH (completes the Phase 1 usable surface)
**Dependencies:** BATCH-04 (DebugApiService/Host patterns, `/entities/spawn` for the T07 round-trip),
BATCH-01 (`EventSerializationHelper`, `JsonShapeDescriber`)

---

## Onboarding & Workflow

These two endpoints give the AI agent the data it needs to *author* scenarios: what entity types exist
(TKB catalog) and how to place them in the world (coordinate frame + geo↔local conversion). Both are
read-only / stateless and therefore off-thread-safe — no `RunOnMainThread` needed for the queries
(confirm this for each; if any path touches `_world`, marshal it).

### Required reading (IN ORDER)
1. `.dev/.guides/DEV-GUIDE.md`
2. `.dev/ai-debug-api/reviews/ADA-BATCH-03-REVIEW.md` and `ADA-BATCH-04-REVIEW.md` (patterns, the real-headless
   gate, the "assert the spec, not just the implementation's reach" lesson).
3. **Design:** `.dev/ai-debug-api/DESIGN.md` — Group M (TKB catalog) and Group N (world/coordinate info),
   plus New Work #6.
4. **Task detail:** `.dev/ai-debug-api/TASK-DETAIL.md` — ADA-P1-T07, ADA-P1-T08 (authoritative spec + Success
   Conditions).

> No codebase-memory MCP (it hangs — use Grep/Glob/Read). No git commit. Report honestly — the lead re-runs
> `dotnet test` + the real headless reproduce and reads the diff (it has caught false/narrowed "done" claims
> four times now). Do NOT narrow a test to the implementation's reach to make it pass; if you can't meet a
> Success Condition, log debt and say so.

### Existing code to study / reuse
- `DebugApiService` / `DebugApiHost` (BATCH-02/04) — extend, don't fork. JsonNode payloads; envelope via `Ok`/`Fail`.
- `EditorSubsystem.Initialize` — the editor already builds a `tkbDb` (local var) and a geo transform
  (`CreateGeoTransform`). Retain references and pass them into the service (mirror how `_preview`/`_time`/etc.
  were wired in earlier batches).
- `TkbDatabase.GetAll()` / `GetEntitiesByCategory` / `TkbTemplate.GetAllDescriptors()` — the catalog source.
  Reuse the **dynamic projection**, NOT any hardcoded `TkbCatalogEntry[]`.
- `WGS84Transform` / `IGeographicTransform` — `ToCartesian` / `ToGeodetic`. Find where the editor constructs
  it and what origin (lat/lon/alt) it is given; expose that origin.
- `SimTransformBridgeSystem.HeadingDegToRotation` / `RotationToHeadingDeg` — orientation conversion.
- `SpatialHashGrid` via `CognitiveSpatialModule` — grid extent for `/world/info`.
- `EventSerializationHelper.SerializeToJson` — for descriptor bags / DTO output (NOT the host CamelCase path).

---

## Endpoints (authoritative spec in TASK-DETAIL.md / DESIGN Group M, N)

### Group M — TKB catalog (T07)
- `GET /tkb/types?category=` → `[{ tkbType, name, categoryPath, disType }]` from `TkbDatabase.GetAll()`
  (filter by category when provided).
- `GET /tkb/types/{tkbType}` → `{ mandatoryComponents, childBlueprints, disType, descriptors }` —
  descriptor bag (`GetAllDescriptors()`) serialized via `EventSerializationHelper`. No spawning.

### Group N — world/coordinate info (T08)
- Add an `Origin` (lat/lon/alt) getter to `IGeographicTransform`/`WGS84Transform` (or capture the origin the
  editor passes at `CreateGeoTransform`). Minimal, additive — do not change conversion behavior.
- `GET /world/info` →
  `{ geo:{origin}, spatialGrid:{cellSize,originX,originY,width,height,extent}, terrain:null, navmesh:null }`.
  Grid from `SpatialHashGrid`. Report `terrain`/`navmesh` as **null** in editor — do NOT fabricate.
- `POST /world/geo-to-local {lat,lon,alt,headingDeg?}` → `{x,y,z, rotation?}`
  (`ToCartesian` + `HeadingDegToRotation` when heading given).
- `POST /world/local-to-geo {x,y,z,rotation?}` → `{lat,lon,alt, headingDeg?}`
  (`ToGeodetic` + `RotationToHeadingDeg`).

## Verification (ship tests; loop to green)
- **Tier-1 (EditorHarness):** the harness must expose the `tkbDb` and geo transform (extend `EditorHarness`
  + `BuildDebugApiService` as needed, mirroring prior batches).
  - `GET /tkb/types` lists the registered templates (e.g. the Urban Combat set: ids + names non-empty).
  - `GET /tkb/types/{id}` returns mandatory components + readable descriptor DTOs (no spawn).
  - **Round-trip:** a `tkbType` from the catalog is accepted by `POST /entities/spawn` (entityCount grows).
  - `GET /world/info` returns the configured origin (Berlin) + the 1000×1000 grid extent.
  - `geo-to-local` of the origin lat/lon ≈ (0,0,0); round-trip `geo→local→geo` within tolerance.
  - `headingDeg:90` → rotation whose `RotationToHeadingDeg` ≈ 90 (East); North = 0. (Note the known
    `RotationToHeadingDeg` degenerate-pitch bug — out of scope to fix; avoid that pitch in the test.)
- **Tier-2 (headless smoke, extend `DebugApiHeadlessSmokeTests`):** after scenario load, `GET /tkb/types`
  non-empty and `GET /world/info` returns a non-null origin. Keep it ENV-gated like the existing smoke.
- `dotnet build IOS-IG-SimHost.sln`; `dotnet test … --filter "FullyQualifiedName~DebugApi"`.

## Constraints (hard)
- DTO / `EventSerializationHelper` path for domain data; never the host CamelCase serializer for descriptors.
- The Origin getter is additive only — do not alter `ToCartesian`/`ToGeodetic` results.
- Read-only catalog + stateless conversions → off-thread-safe; confirm no `_world` touch, else marshal.
- Reuse the dynamic TKB projection, never a hardcoded entry array.
- Frozen `TestAssets` fixtures; never the production scan path; never regenerate snapshots.
- Path-naming: `/tkb/types`, `/tkb/types/{id}`, `/world/info`, `/world/geo-to-local`, `/world/local-to-geo` —
  align with the DESIGN API table.

## Deliverables
- Code + tests green; extended smoke.
- `.dev/ai-debug-api/reports/ADA-BATCH-05-REPORT.md` (DEV-GUIDE format): built, decisions/deviations,
  FULL `dotnet test` summary, the headless smoke output (tkb/types non-empty + world/info origin non-null),
  blockers, debt → DEBT-TRACKER.

> Out of scope for this batch: ADA-04-D02 managed-event discovery (tracked as T06b) and ADA-04-D01 ack-wait —
> do not attempt them here.

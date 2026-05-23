# Task Tracker — ECS 512-Component Expansion

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions.

---

## Phase 1: Prerequisites

**Goal:** Widen component IDs from `byte` to `int` and update capacity constants — zero runtime
behavior change, pure type promotion.

- [x] **TASK-E001** Widen Component ID Type: Attribute and Constants [details](./TASK-DETAIL.md#task-e001--widen-component-id-type-attribute-and-constants)
- [x] **TASK-E002** Configuration Update: Capacity and Format Version [details](./TASK-DETAIL.md#task-e002--configuration-update-capacity-and-format-version)

## Phase 2: New Data Structures

**Goal:** Introduce `BitMask512` and `EntityMetadataCold` as standalone, fully tested structures
with no changes to how existing code uses them yet.

- [x] **TASK-E003** New Data Structure: BitMask512 [details](./TASK-DETAIL.md#task-e003--new-data-structure-bitmask512)
- [x] **TASK-E004** New Data Structure: EntityMetadataCold [details](./TASK-DETAIL.md#task-e004--new-data-structure-entitymetadatacold)

## Phase 3: Core Rewrite

**Goal:** Replace the single `NativeChunkTable<EntityHeader>` in `EntityIndex` with the parallel
hot/cold tables; delete `EntityHeader`.

- [x] **TASK-E005** EntityIndex Rewrite: Hot/Cold Parallel Tables [details](./TASK-DETAIL.md#task-e005--entityindex-rewrite-hotcold-parallel-tables)

## Phase 4: Query and Repository Layer

**Goal:** Wire up the new hot-first traversal in queries and update the repository to use split
hot/cold accessors.

- [x] **TASK-E006** EntityQuery and QueryBuilder: Hot-First Traversal [details](./TASK-DETAIL.md#task-e006--entityquery-and-querybuilder-hot-first-traversal)
- [x] **TASK-E007** EntityRepository: Split Header Access [details](./TASK-DETAIL.md#task-e007--entityrepository-split-header-access)

## Phase 5: Flight Recorder

**Goal:** Update the Flight Recorder to serialize and restore the two separate index streams.

- [ ] **TASK-E008** RecorderSystem: Dual-Stream Entity Index [details](./TASK-DETAIL.md#task-e008--recordersystem-dual-stream-entity-index)
- [ ] **TASK-E009** PlaybackSystem: Route Hot/Cold Streams [details](./TASK-DETAIL.md#task-e009--playbacksystem-route-hotcold-streams)

# JSON Migration System — Onboarding

Welcome. This subproject builds a generic **versioned JSON document migration system** for the HROT engine. Customers author scenarios (and other versioned JSON assets — blueprints, TKB definitions, road networks, replay metadata) in the editor; those files must remain readable and editable as the engine ships new binary versions — including across customer-initiated *downgrades*. This subproject is the infrastructure that makes that contractually possible.

If you only read one thing first, read [Migration-system.md](./Migration-system.md) §1–§3 of doc 01 (the overview).

---

## What you'll be building

A small library plus a phased rollout:

- **Phase 1 — core library** in `Fdp.Core.Serialization.Migrations`: envelope reader, JSONPath dialect, registry, pipeline, journal computation, sidecar storage, two adapters (read-only for the cluster, persistent for the editor and CLI).
- **Phase 2 — envelope rollout**: every existing JSON read/write path is rerouted through the migration adapters, every file picks up a unified `$meta` envelope; no schema versions change yet.
- **Phase 3 — first migrator pair**: one real `v1 → v2` schema change goes through the full pipeline.
- **Phase 4 — editor & CLI integration**: warning UI in the editor, `Hrot.ClusterRunner --mode migrate` batch CLI.
- **Phase 5 — steady state**: ongoing corpus/migrator additions, no end date.

You will most likely be assigned tasks in one of the phases above. The full work order lives in [TASK-TRACKER.md](./TASK-TRACKER.md); the per-task detail (deliverables, success conditions, design refs) lives in [TASK-DETAILS.md](./TASK-DETAILS.md).

---

## Where the design lives

All design content is in this folder: `.dev/json-migration/`.

| File | What it contains |
|---|---|
| [Migration-system.md](./Migration-system.md) | The complete 7-part design document (5500+ lines). Sub-docs: 01 overview, 02 wire formats, 03 interfaces, 04 behavioral specs, 06 test plan, 07 rollout plan. (Doc 05 — integration patches — is deferred to Phase 2 per JM-P2-001.) |
| [TASK-DETAILS.md](./TASK-DETAILS.md) | One section per task with success conditions and design refs. Start here once you know which task you're picking up. Also lists **codebase-fit corrections C-1..C-9** — small drifts between design and current code that the tasks already reflect. |
| [TASK-TRACKER.md](./TASK-TRACKER.md) | Phase-by-phase checklist with `[ ]`/`[x]` status. |
| [DEBT-TRACKER.md](./DEBT-TRACKER.md) | Where any deferred tech debt found mid-task gets logged. |
| [DEV-GUIDE.md](./DEV-GUIDE.md) | Working-style guidelines all developers on this subproject follow. **Read this before opening a PR.** |

Architectural decisions are numbered (`D-01`, `D-02`, …) and resolutions are numbered (`M-1`, `M-2`, `M-3`, `B-1`, `B-2`). When the design or tasks reference one, look it up in *01 §3* (decisions) or *03 §11* / *04 §8* (resolutions).

---

## Where the components live in the codebase

```
FDP/Engine/Fdp.Core/
├── Serialization/
│   ├── FdpJsonOptionsRegistry.cs            (existing; serialization options)
│   ├── FdpDocumentTypes.cs                  (new — Phase 1)
│   └── Migrations/                          (new namespace — Phase 1)
│       ├── JsonEnvelope.cs                  (JM-P1-002)
│       ├── DocumentMeta.cs                  (JM-P1-001)
│       ├── MigrationRegistry.cs             (JM-P1-005)
│       ├── MigrationPipeline.cs             (JM-P1-006)
│       ├── IJsonDocumentMigrator.cs         (JM-P1-005)
│       ├── IMigrationStorage.cs             (JM-P1-009)
│       ├── FileSystemMigrationStorage.cs    (JM-P1-010)
│       ├── InMemoryMigrationStorage.cs      (JM-P1-009)
│       ├── UnknownsJournal.cs               (JM-P1-008)
│       ├── Adapters/
│       │   ├── ReadOnlyMigrationAdapter.cs  (JM-P1-011)
│       │   └── PersistentMigrationAdapter.cs(JM-P1-012)
│       ├── Bootstrap/
│       │   └── MigrationBootstrap.cs        (JM-P1-013)
│       └── Internal/
│           ├── JsonPath*.cs                 (JM-P1-003)
│           ├── ScopePathStack.cs            (JM-P1-004)
│           ├── DiffToJournalConverter.cs    (JM-P1-008)
│           ├── HashUtilities.cs             (JM-P1-008)
│           └── Diff/
│               ├── DiffNode.cs              (JM-P1-007 — extracted)
│               ├── DiffObject.cs            (JM-P1-007 — extracted)
│               ├── DiffValue.cs             (JM-P1-007 — extracted)
│               └── DomDiffer.cs             (JM-P1-007 — extracted)

FDP/Engine/Fdp.Core.Tests/
└── Serialization/
    └── Migrations/                          (all T1, T2, T3 tests live here)

FDP/Toolkits/Fdp.Toolkits/
└── ReplayBrowser/Diff/
    └── ComponentDiffService.cs              (existing; rewired to consume extracted types in JM-P1-007)

Hrot/Engine/Hrot.Common/Scenario/
├── HrotSubsystemTypes.cs                    (existing; expanded into HrotDocumentTypes — JM-P2-002)
├── HrotDocumentTypes.cs                     (new — JM-P2-002)
└── Migrations/                              (new — Phase 2+)
    ├── PassthroughFormatsModule.cs          (JM-P2-002)
    ├── ScenarioMigrationModule.cs           (JM-P2-002 skeleton; first migrators in JM-P3-001)
    ├── BlueprintMigrationModule.cs          (JM-P2-002 skeleton)
    ├── TkbMigrationModule.cs                (JM-P2-002 skeleton)
    ├── RoadNetworkMigrationModule.cs        (JM-P2-002 skeleton)
    ├── Helpers/                             (EntityPatch, CasingPolicy, NestedJsonPatch)
    └── Migrators/                           (the actual migrators ship here from Phase 3 onward)

Hrot/Engine/Hrot.Common.Tests/               (new project — created in JM-P2-002)
└── Migrations/                              (HROT-side module tests)

Hrot/Subsystems/Hrot.SimHost/
├── NodeBootstrapper.cs                      (existing; MigrationServices wired in role-driven manner — JM-P2-009)
└── Orchestration/Handlers/
    ├── HrotScenarioLoadHandler.cs           (existing; patched in JM-P2-003)
    └── TkbLoadClusterStateHandler.cs        (existing; patched in JM-P2-005)

Hrot/Engine/Hrot.Presentation/
└── ScenarioEditor/Services/
    └── ScenarioFileService.cs               (existing; patched in JM-P2-003; UI hooks in JM-P4-001..003)

Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/
└── BlueprintJsonServices.cs                 (existing; patched in JM-P2-004)

Hrot/Runner/Hrot.ClusterRunner/
└── Program.cs                               (existing; --mode migrate added in JM-P4-004)

FDP/Toolkits/Fdp.Toolkits/CarKinem/Road/
└── RoadNetworkLoader.cs                     (existing; patched in JM-P2-006)

FDP/Tools/Fdp.Tools.RecordingDumper/
└── Program.cs                               (existing; patched in JM-P2-007)

FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/
├── ReplayBrowserContext.cs                  (existing; patched in JM-P2-007)
├── Federation/TransientMasterBuilder.cs     (existing; patched in JM-P2-007)
└── RecordingExportService.cs                (existing; patched in JM-P2-007)
```

For the corresponding write paths and the additional passthrough writers, see [TASK-DETAILS.md JM-P2-001..008](./TASK-DETAILS.md#phase-2--envelope-rollout).

---

## Key conventions you must follow

- **Logging:** use `FdpLog<T>` (the engine's NLog wrapper at `FDP/Engine/Fdp.Core/Logging/FdpLog.cs`). Index-based templates `{0}`, `{1}` for up to four args; use `IsInfoEnabled` guards for anything heavier.
- **Exceptions:** `MigrationException` extends `InvalidOperationException`. Migration failures are **fail-loud**; no silent fallthrough.
- **JSON DOM:** `System.Text.Json.Nodes.JsonObject` throughout. **No typed DTOs** in migrators (per D-05).
- **Tests:** xUnit only (no FluentAssertions — matches `Fdp.Core.Tests` convention). Test names use `MethodOrFeature_Scenario_ExpectedBehavior`. Fixtures live under `TestFixtures/` in the test assembly.
- **Migrator authoring:** follow [Migration-system.md doc 07 §10 — Migrator authoring guidelines](./Migration-system.md). Especially: scope discipline, idempotency, never touch `$meta`, one log line per migrator run, atomic per-entity changes.
- **Determinism:** migrators must be deterministic. No wall-clock, no env vars, no unseeded random.

---

## How to build

The engine builds with standard .NET tooling. From the repo root:

```powershell
# Restore + build everything
dotnet build IOS-IG-SimHost-FDP.sln

# Run the migration tests specifically (Phase 1+)
dotnet test FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj --filter "FullyQualifiedName~Migrations"

# Run the HROT-side module tests (Phase 2+)
dotnet test Hrot/Engine/Hrot.Common.Tests/Hrot.Common.Tests.csproj
```

The cluster runner CLI, useful for end-to-end smoke tests once Phase 4 lands:

```powershell
dotnet run --project Hrot/Runner/Hrot.ClusterRunner -- --mode migrate --input-dir test-data/scenario-corpus/multi-version/v1_complete --dry-run
```

---

## Reading order for newcomers

1. **[DEV-GUIDE.md](./DEV-GUIDE.md)** — how to behave in this subproject (PR style, sign-off gates, definition of done).
2. **[Migration-system.md](./Migration-system.md)** §1–§7 of doc 01 (~450 lines) — the design's load-bearing decisions.
3. **[TASK-DETAILS.md](./TASK-DETAILS.md)** — find the task you've been assigned. The success conditions and design refs at the top of each task are the contract.
4. The relevant sub-doc of `Migration-system.md` referenced by your task (doc 02 for wire formats, doc 03 for interfaces, doc 04 for behavioral specs, doc 06 for tests, doc 07 for sequencing).
5. The existing implementation of nearby engine components (`HrotScenarioLoadHandler`, `BlueprintJsonServices`, `ComponentDiffService` if you're working on the M-1 extraction).

---

## Where to ask for help

- Architectural questions / decision changes: design-talk channel; reference the relevant `D-NN` or `M-N` ID.
- Codebase navigation: use the **codebase-memory MCP** (`search_graph`, `trace_path`, `get_code_snippet`) rather than raw grep — the graph is comprehensive and faster.
- PR style and review escalation: see [DEV-GUIDE.md](./DEV-GUIDE.md).

Welcome aboard.

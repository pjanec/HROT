# Onboarding — Scenario Editor Pack & HROT Editor Refactoring (`packs-2`)

Welcome to the `packs-2` workstream. This document gets you up to speed quickly.

---

## What Are We Building?

We are continuing the modular ECS architecture refactoring started in `packs-1`.
The goal of `packs-2` is to create the **HROT Editor** — a standalone, all-in-one scenario
authoring tool — and to enable a **Feature Switch** that allows the Editor to run either
fully offline (internal FDP SimHost) or connected to a remote HROT SimHost over the network.

**In short:**

- The IG map tools (`CreationTool`, `EditTool`, etc.) currently still produce concrete DDS
  messages (`CreateEntityRequest`). This must be purged so the tools only emit pure FDP domain
  events (`SpawnEntityCommand`, `UpdateEntityCommand`, `DestroyEntityCommand`).
- The purified map tools and render layers will be extracted into a new shared project
  `Hrot.ScenarioEditor`, usable by both the existing IG and the new Editor without duplication.
- Each application (IG, ExCon, Editor) keeps its own bespoke ImGui panels. Only the underlying
  map tools and rendering layers are shared.
- Local scenario Save / Load / New operations are wired into the `ScenarioEditorModule`,
  operating entirely on the local `EntityRepository` (zero DDS I/O).
- A runtime **Feature Switch** hot-swaps the internal `SimHostCoreLogicPack` for the network
  ACL Translator Packs, transforming the all-in-one Editor into a distributed CGF/ExCon hybrid
  without changing a single line of UI code.

The five phases in order:

1. **Phase 1** — Decouple map tools from DDS (purge `CreateEntityRequest`, `UpdateEntityDescriptorRequest`, and `DeleteEntityRequest` from tool source).
2. **Phase 2** — Extract shared `Hrot.ScenarioEditor` Logic Pack (tools + render layers + file I/O).
3. **Phase 3** — Formalize host-specific UI Packs (IG, ExCon, Editor).
4. **Phase 4** — Validate local scenario file operations end-to-end.
5. **Phase 5** — Assemble the HROT Editor composition root and implement the Feature Switch.

---

## Key Documents

| Document | Purpose |
|----------|---------|
| [design_talk.md](./design_talk.md) | Full design conversation — read this to understand the *why* |
| [DESIGN.md](./DESIGN.md) | Formal design — phases, architecture decisions, data contracts |
| [TASK-DETAIL.md](./TASK-DETAIL.md) | Per-task specifications with success conditions (unit test specs) |
| [TASK-TRACKER.md](./TASK-TRACKER.md) | Quick progress checklist |
| [DEV-GUIDE.md](../.guides/DEV-GUIDE.md) | **Read this before starting any work.** Developer workflow, batch system, reporting format |
| [DEBT-TRACKER.md](../DEBT-TRACKER.md) | Technical debt log |

**Context:** `packs-1` introduced strict CQRS boundaries between Brain (CGF) and Muscle (SimHost)
tiers, purged DDS writes from ExCon panels, and purified the Orchestration layer. `packs-2` extends
that work to the IG map tools and builds the Editor on top of the resulting clean architecture.

---

## Solution Structure

Workspace root: `d:\Work\IOS-IG-SimHost-FDP-2`

```
IOS-IG-SimHost.sln                         ← main solution
FDP/FDP.sln                                ← FDP engine sub-solution

── FDP engine (pure domain — zero CycloneDDS) ─────────────────────────────────────────
FDP/Kernel/                                  ← EntityRepository, FdpEventBus, IEcsModule, ISimulationView
FDP/Toolkits/FDP.Toolkit.NetworkSpawning/    ← SpawnEntityCommand, UpdateEntityCommand, DestroyEntityCommand
                                               NetworkSpawningSystem (consumes them locally)
FDP/Toolkits/FDP.Toolkit.Vis2D/              ← MapCanvas, IMapTool, render layer abstractions
FDP/Toolkits/FDP.Toolkit.Scenario/          ← ScenarioSerializer, ScenarioSerializerBuilder, IEntityScenarioTranslator

── Hrot integration layer ─────────────────────────────────────────────────────────────
Hrot.IG/                                     ← IgApplication, IgSubsystem
Hrot.IG/Tools/                               ← CreationTool, EditTool, RouteEditTool, MeasureTool
                                               (Phase 1: purge DDS; Phase 2: move to Hrot.ScenarioEditor)
Hrot.IG/Systems/                             ← MapCommandController, ContextMenuSystem, render layers
                                               (Phase 1: purge DDS; Phase 2: render layers move to ScenarioEditor)
Hrot.IG/UI/                                  ← IgDebugPanel, PerformanceOverlay, MiniExConPanel
                                               (IG UI Pack — stays here)
Hrot.ExCon/                                  ← ExConLogic, IExConLogic
Hrot.ExCon/Panels/                           ← OrbatPanel, MissionPanel, ConfigPanel, etc.
                                               (ExCon UI Pack — stays here)

── NEW in packs-2 ─────────────────────────────────────────────────────────────────────
Hrot.ScenarioEditor/                         ← Shared Logic Pack: ScenarioEditorModule
Hrot.ScenarioEditor/Tools/                   ← Purified tools (moved from Hrot.IG)
Hrot.ScenarioEditor/Rendering/               ← Purified render layers (moved from Hrot.IG)
Hrot.ScenarioEditor/Services/                ← ScenarioFileService (Save/Load/New)
Hrot.Editor/                                 ← HROT Editor executable (Program.cs, composition root)
Hrot.Editor/UI/                              ← ScenarioBrowserPanel, EditorToolbarPanel, etc.

── ACL (Translator Packs) ─────────────────────────────────────────────────────────────
Hrot.Map.Common/Replication/Egress/          ← SpawnEntityCommandEgressTranslator (NEW)
                                               UpdateEntityCommandEgressTranslator (NEW)
                                               DestroyEntityCommandEgressTranslator (NEW)
Hrot.Map.Common/Replication/Ingress/         ← EntityInfoIngressTranslator, EntityMasterIngressTranslator
                                               GeoSpatialIngressTranslator

── DDS wire layer (external/read-only from FDP perspective) ───────────────────────────
Hrot.NED/                                    ← DDS structs (CreateEntityRequest, UpdateEntityDescriptorRequest etc.)
                                               Hrot.ScenarioEditor must NEVER reference this directly
```

---

## Build Instructions

```powershell
# Build the full solution
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln

# Build FDP engine only
cd FDP
dotnet build FDP.sln

# Run unit tests
dotnet test Hrot.IG.Tests/Hrot.IG.Tests.csproj
dotnet test Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj

# (After Phase 2) Build and test the new Scenario Editor pack
dotnet build Hrot.ScenarioEditor/Hrot.ScenarioEditor.csproj
dotnet test Hrot.ScenarioEditor.Tests/Hrot.ScenarioEditor.Tests.csproj  # (when created)
```

---

## Key Concepts to Understand Before Starting

**`SpawnEntityCommand` / `UpdateEntityCommand` / `DestroyEntityCommand`:**  
Pure managed events on the `FdpEventBus`. Consumed locally by `NetworkSpawningSystem` when no
network is active, or intercepted by ACL egress translators and forwarded to DDS.

**`MapCommandController`:**  
Orchestrates the IG remote-tool-activation flow from ExCon. Receives `MapCommandRequest` from
ExCon over DDS, pushes the correct tool onto the `MapCanvas`, and ACKs back via `MapCommandAck`.
After Phase 1.D it no longer directly writes `CreateEntityRequest` to DDS — it publishes
`SpawnEntityCommand` to the bus, which the ACL translator forwards.

**`ScenarioEditorModule`:**  
The new Logic Pack that acts as the composition root for shared tools and render layers. Installed
into any `ModuleHostKernel` that needs map-editing capabilities (Editor, or as a plugin to IG).

**Feature Switch:**  
A runtime composition root reconfiguration. The `ModuleHostKernel` RCU hot-plug API
(`InstallModulesAsync`/`UninstallModulesAsync`) safely transitions between the offline All-In-One
topology (Brain + Muscle + Editor, no network) and the External topology (Editor + Translator
Packs, remote SimHost) without stalling the 60 Hz render loop.

---

## Developer Workflow

Read **[DEV-GUIDE.md](../.guides/DEV-GUIDE.md)** before writing any code. Key points:

- Work is delivered in **batches** (see `batches/` subfolder for batch instruction files).
- Each batch is self-contained and independently testable.
- Submit a batch report using the template in `.dev/.guides/BATCH-REPORT-TEMPLATE.md`.
- Questions go in `questions/` as markdown files; wait for answers before proceeding.
- Never push code directly; the Development Lead reviews reports before merging.

---

## Important Constraints

1. **Retain IG + ExCon remote-control functionality.** The `MapCommandController` remote
   tool-activation path must work identically after Phase 1 refactoring. The new ACL translator
   (`SpawnEntityCommandEgressTranslator`) ensures `CreateEntityRequest` still reaches DDS.
2. **Do not share ImGui panels** between projects. `Hrot.ScenarioEditor` contains zero ImGui
   panels. Panels live in their host applications only.
3. **`Hrot.ScenarioEditor` must have zero `Hrot.NED` dependency** — not even transitive.
   Enforce via `.csproj` explicit `<PackageReference Exclude>` if needed.
4. **The Feature Switch uses HROT SimHost**, not Bagira/BDC SST. Bagira support is a future workstream.
5. **All tests must stay green** after each phase. Run `dotnet test` before declaring a task done.

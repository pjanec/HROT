# BATCH-01: Blueprint Subsystem — Phase 0 Infrastructure

**Batch Number:** BATCH-01
**Tasks:** TASK-P0-001, TASK-P0-002, TASK-P0-003
**Phase:** Phase 0 — Infrastructure
**Estimated Effort:** 12-16 hours
**Priority:** HIGH
**Dependencies:** None (all new files; no modifications to existing production code except project files and solution)

---

## Onboarding & Workflow

### Developer Instructions

This is the first batch of the Blueprint Subsystem workstream.  Your goal is to establish
the build skeleton: four new C# projects compiled, the asset schema types in place, and a
round-trip test suite green.  All work this batch is NEW files (plus project/solution edits).
Do NOT touch any existing production code beyond what the task explicitly requires.

### Required Reading (IN ORDER)

1. **Roadmap:** `.dev/blueprints-1/Blueprint_Subsystem_Implementation_Roadmap_v1.1.md`
   — Read §2 (filesystem layout) and §4 M0, M1 in full.
2. **Architecture:** `.dev/blueprints-1/Blueprint_Subsystem_Architecture_v1.2.md`
   — Read §3 (projects & references) and §5 (asset schema) in full.
   Then read the patches:
   `.dev/blueprints-1/Blueprint_Subsystem_Architecture_v1.2_InlinePatches.md`
   `.dev/blueprints-1/Blueprint_Subsystem_Architecture_v1.2_FinalResolutions.md`
3. **Task Definitions:** `.dev/blueprints-1/TASK-DETAIL.md`
   — Read TASK-P0-001, TASK-P0-002, TASK-P0-003 in full (the authoritative spec).
4. **Existing toolkit namespace convention:** `FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj`
   — note `<RootNamespace>Fdp.Toolkit</RootNamespace>` (singular, no 's').
5. **`FdpJsonOptionsRegistry`:** `FDP/Engine/Fdp.Core/Serialization/FdpJsonOptionsRegistry.cs`
   — understand `DefaultRelaxed`; `BlueprintJsonServices` must delegate to it.
6. **Example xUnit test project:** any `.Tests.csproj` under `Hrot/Subsystems/` to understand
   the xUnit version and pattern used in this repo.
7. **Developer workflow:** `.dev/.guides/DEV-GUIDE.md`

### Source Code Locations

- **New projects (all new):**
  - `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/`
  - `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Generators/`
  - `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/`
  - `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/`
- **Toolkit folder (add subfolder, no new .csproj):**
  - `FDP/Toolkits/Fdp.Toolkits/Blueprints/`
- **AI Behaviors blueprints dir (create empty dir):**
  - `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/`
- **Solution file to update:**
  - `IOS-IG-SimHost.sln`
- **`Hrot.AI.Behaviors.csproj` to update:**
  - `Hrot/Subsystems/Hrot.AI.Behaviors/Hrot.AI.Behaviors.csproj`

### Build & Test Commands

```powershell
# From repo root:
dotnet build IOS-IG-SimHost.sln
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj
```

### Report Submission

**When done, submit your report to:**
`.dev/blueprints-1/reports/BATCH-01-REPORT.md`

**If you have questions, create:**
`.dev/blueprints-1/questions/BATCH-01-QUESTIONS.md`

---

## Context

Phase 0 creates the build skeleton before any production logic is written.  It installs
three things:
1. **Four projects** that form the whole Blueprint subsystem, plus the `Fdp.Toolkits/Blueprints`
   subfolder that will later receive runtime support types.
2. **Asset schema types** — the pure C# data model for `.bp.json` files that every later
   phase (compiler, runtime, editor) depends on.
3. **JSON round-trip tests** — proof that the schema is correct and serialization is stable.

If `dotnet build` is broken after this batch, nothing else can progress.

---

## Tasks

### Task 1: Project Skeleton & Filesystem Placement (TASK-P0-001)

**Full spec:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-p0-001----project-skeleton--filesystem-placement)

**Summary of deliverables:**

- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Hrot.Blueprints.Core.csproj`
  — `net8.0`, references `Fdp.Core` only, placeholder `.cs`.
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Generators/Hrot.Blueprints.Generators.csproj`
  — `netstandard2.0`, package refs `Microsoft.CodeAnalysis.CSharp 4.8.0` +
  `Microsoft.CodeAnalysis.Analyzers 3.3.4` (both `PrivateAssets="all"`), project ref
  `Hrot.Blueprints.Core` with `PrivateAssets="all"`, placeholder `BlueprintIncrementalGenerator.cs`
  stub with `[Generator]` attribute and empty body.
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Hrot.Blueprints.Editor.csproj`
  — `net8.0`, references `Hrot.Blueprints.Core`, `Fdp.Core`, `Fdp.Presentation`, `Fdp.Toolkits`,
  placeholder `.cs`.
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj`
  — `net8.0`, xUnit test project, references `Hrot.Blueprints.Core` + `Fdp.Core` + xUnit,
  one placeholder `[Fact]` that asserts `true`.
- `FDP/Toolkits/Fdp.Toolkits/Blueprints/` subfolder with placeholder files per Roadmap §2.
  **This is NOT a new .csproj** — it lives inside the existing `Fdp.Toolkits.csproj`.
- `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/` directory (empty, for future `.bp.json`).
- Add all four new projects to `IOS-IG-SimHost.sln`.
- Modify `Hrot.AI.Behaviors.csproj` per Roadmap M0 acceptance: `EmitCompilerGeneratedFiles`,
  `CompilerGeneratedFilesOutputPath`, `DebugType`, `DebugSymbols`, generator `ProjectReference`
  with `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`, `AdditionalFiles` glob
  `Blueprints\**\*.bp.json`.

**Critical constraints (from TASK-DETAIL.md):**
- Generators project MUST target `netstandard2.0` (not `net8.0`).
- `Fdp.Toolkits.Blueprints` is a FOLDER inside `Fdp.Toolkits`, not a separate `.csproj`.
- `Hrot.Blueprints.Core` must reference `Fdp.Core` only (no `Fdp.Toolkits`).
- No `Hrot.Blueprints.Engine` project.
- Zero errors and zero warnings after `dotnet build`.

**Tests Required:**
- SC2: Generator load verification — create `test-malformed.bp.json` with invalid JSON `{`,
  run `dotnet build Hrot.AI.Behaviors.csproj`, assert at least one diagnostic from
  `BlueprintIncrementalGenerator` appears, then remove the file.
- SC3: `dotnet test Hrot.Blueprints.Tests.csproj` reports 1 passed, 0 failed.
- SC4: Verify `AdditionalFiles` glob resolves to zero items for the empty Blueprints dir.

---

### Task 2: Asset Schema Types (TASK-P0-002)

**Full spec:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-p0-002----asset-schema-types)

**Summary of deliverables:**

All types in `Hrot.Blueprints.Core.Assets` namespace:
- Enums: `BlueprintDispatchKind`, `BlackboardTierHint`, `AiPrimitiveIntent`, `AiPrimitiveHosting`,
  `GraphKind`, `NodeStatus` (check Architecture for all values).
- Record/classes: `BlueprintAsset`, `AiPrimitiveDecl`, `VariableDecl`, `ParameterDecl`,
  `EventDispatcherDecl`, `CustomEventDecl`, `BlueprintTypeRef`, `Graph`, `Node` (abstract),
  `Pin`, `Link`, `AssetMetadata`, `GraphMetadata`, `NodeMetadata`, `Header`.
- All 19 concrete `Node` subclasses (sealed) with `[JsonDerivedType]` on `Node` base:
  `FunctionCallNode`, `BranchNode`, `SequenceNode`, `GetVariableNode`, `SetVariableNode`,
  `LiteralNode`, `EventEntryNode`, `ReturnNode`, `CastNode`, `ArrayMakeNode`, `ArrayGetNode`,
  `LatentDelayNode`, `CallEventDispatcherNode`, `BindEventDispatcherNode`, `CallCustomEventNode`,
  `CallPeerBlueprintNode`, `ChannelCommandNode`, `WaitForChannelNode`, `WaitForEventNode`.
- `[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]` on `Node`.
- `BlueprintJsonServices` static class in `Hrot.Blueprints.Core` namespace with
  `Serialize(BlueprintAsset) -> string` and `Deserialize(string) -> BlueprintAsset?`,
  both using `FdpJsonOptionsRegistry.DefaultRelaxed`.

**Critical constraints (from TASK-DETAIL.md):**
- All 19 concrete `Node` subclasses must be present and `sealed`.
- `ChannelCommandNode`, `WaitForChannelNode`, `WaitForEventNode` are NEW in v1.2 — must be included.
- Use `System.Text.Json` exclusively. No Newtonsoft.
- Do NOT duplicate `FdpJsonOptionsRegistry` logic.
- If `[JsonPolymorphic]` conflicts with engine options, use the `CreateExtended` workaround pattern
  (copy `DefaultRelaxed` into a new `JsonSerializerOptions` instance, add the polymorphic resolver).

**Tests Required (in Hrot.Blueprints.Tests):**
- SC2: Reflection count of concrete `Node` subtypes equals 19.
- SC3: Discriminator round-trip for each of the 19 node types.
- SC4: `Deserialize` on JSON with unknown fields returns non-null without throwing; missing optional
  list fields produce empty lists (not null).

---

### Task 3: Asset JSON Round-Trip Tests (TASK-P0-003)

**Full spec:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-p0-003----asset-json-round-trip-tests)

**Summary of deliverables:**

xUnit test class `AssetJsonRoundTripTests` in `Hrot.Blueprints.Tests` with:

- Round-trip test for **Library** dispatch: 2+ graphs each with 1+ `FunctionCallNode`. Serialize,
  deserialize, re-serialize, assert byte-identical JSON.
- Round-trip test for **AiPrimitive** dispatch: `Intent=Action`, `Hostings=[BTreeAction, HsmAction]`,
  1 `ParameterDecl`, 1 `WorkingState` entry, graph with at least `ChannelCommandNode`,
  `WaitForChannelNode`, `WaitForEventNode`. Serialize → deserialize → re-serialize, assert identical.
- Round-trip test for **Instance** dispatch: 1+ `VariableDecl`, 2 graphs (`Function` + `Event`).
  Serialize → deserialize → re-serialize, assert identical.
- **Polymorphic node coverage**: one `Graph` with one node of each of the 19 concrete subtypes;
  after serialize + deserialize, assert each node's `GetType()` matches expected type.
- **Unknown-field tolerance**: deserialize JSON with `"unknownField"` at top level and inside a
  `Node`; assert no exception, assert known fields correct.
- **Missing-field defaulting**: deserialize minimal JSON with only `name`, `dispatch`, `assetId`;
  assert all list fields are non-null and empty.

**Critical constraints (from TASK-DETAIL.md):**
- Round-trip comparison must be text-based (compare JSON strings), not object graph.
- All 19 concrete `Node` subtypes must appear in at least one test assertion.
- The AiPrimitive sample must exercise `AiPrimitiveDecl`, `Parameters`, and `WorkingState`.
- Tests must pass with `dotnet test` in isolation (no engine host, no ECS running).

---

## Mandatory Developer Workflow: Test-Driven Task Progression

Follow this order for EVERY task:

1. Write a failing test first (or identify the SC that will prove correctness).
2. Implement the minimal code to make it pass.
3. Run `dotnet test` and confirm green before moving to the next task.
4. Never proceed to the next task while tests are red.
5. After all tasks are complete, run the full solution build:
   `dotnet build IOS-IG-SimHost.sln` — must produce zero errors, zero warnings.

---

## Testing Requirements

- All SC1–SC7 from TASK-P0-002 must be verified.
- All SC1–SC7 from TASK-P0-003 must be verified.
- All SC1–SC4 from TASK-P0-001 must be verified.
- Total minimum test count: at least 15 `[Fact]` or `[Theory]` methods across
  `AssetJsonRoundTripTests` + the schema reflection tests.
- Tests must assert values/behavior (not just "no exception" unless the SC specifies tolerance).
- The polymorphic round-trip test must assert `GetType()` for each of the 19 subtypes individually.

---

## Developer Insights (answer these in your report)

1. Did `[JsonPolymorphic]` conflict with `FdpJsonOptionsRegistry.DefaultRelaxed`?
   If yes, describe the exact workaround used.
2. Did the Generators `netstandard2.0` project require any special handling to compile
   (e.g., missing APIs that are net8.0-only)?
3. Were there any Architecture v1.2 §5 types that were unclear or missing discriminator strings?
4. Did the `InternalsVisibleTo` attribute need to be added to `Hrot.Blueprints.Core` for
   `Hrot.Blueprints.Tests`?
5. Any weak points spotted in the schema design or the JSON serialization approach?

---

## Report Format

Submit `.dev/blueprints-1/reports/BATCH-01-REPORT.md` with the following sections:

```markdown
# BATCH-01 Report

## Tasks Completed
[List each TASK-ID with status: Completed / Partial / Blocked]

## Test Results
[Full dotnet test output — copy/paste the summary line and any failures]

## Developer Insights
[Answer all 5 questions above]

## Deviations from Spec
[Any deliberate deviation with rationale; "none" is acceptable]

## Issues / Technical Debt
[Anything that was deferred or left incomplete with reason]

## Build Verification
[Output of `dotnet build IOS-IG-SimHost.sln` — show the last 20 lines]
```
